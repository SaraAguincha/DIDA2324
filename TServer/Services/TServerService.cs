using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Protos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Utilities;

// All of the services that the Tserver does
namespace TServer.Services
{
    public class TServerService
    {
        // TODO - store the clients connected to the server

        // Perhaps dictionaries are not the most efficient, but might help with debugging (hopefully C:)
        private Dictionary<string, GrpcChannel> channels = new Dictionary<string, GrpcChannel>();
        private Dictionary<string, TServerLServerService.TServerLServerServiceClient> lServerInstances = new Dictionary<string, TServerLServerService.TServerLServerServiceClient>();
        private Dictionary<string, TServerTServerService.TServerTServerServiceClient> tServerInstances = new Dictionary<string, TServerTServerService.TServerTServerServiceClient>();

        // Server attributes
        private string tManagerId;
        private Dictionary<string, string> lServers;
        private Dictionary<string, string> tServers;
        private Dictionary<string, DadInt> dadInts = new Dictionary<string, DadInt> ();
        private List<int> tServersSuspected = new List<int>();


        // ReleasesRequests that arrived too early.
        private List<ReleaseLeaseRequest> releasesPending = new List<ReleaseLeaseRequest> ();
        // TManagers' queue of access for each key
        private Dictionary<string, Queue<string>> keyAccessQueue = new Dictionary<string, Queue<string>>();
        // Count how many epochs a certain key has gone without a change of lease even while having TManagers next in the queue
        private Dictionary<string, int> keyAccessChange = new Dictionary<string, int> ();

        private Dictionary<string, ServerProcessState>[] processStates;

        // All the active wanted leases for this TManager
        private List<string> activeLeaseKeys = new List<string> ();

        // Majority (calculated in the constructor)
        int majority = 0;


        // set all the server information from config
        public TServerService(string tManagerId, Dictionary<string, string> tServers, Dictionary<string, string> lServers,
            Dictionary<string, ServerProcessState>[] ProcessStates)
        {
            this.tManagerId = tManagerId;
            this.tServers = tServers;
            this.lServers = lServers;
            this.processStates = ProcessStates;

            // Majority should be half the servers plus one. However we don't add one to exclude the self TManager.
            this.majority = tServers.Count;

            // Populate the dictionary of LServer connections
            foreach (KeyValuePair<string, string> lserver in this.lServers)
            {
                GrpcChannel channel = GrpcChannel.ForAddress(lserver.Value);
                channels.Add(lserver.Key, channel);
                lServerInstances.Add(lserver.Key, new TServerLServerService.TServerLServerServiceClient(channel));
            }

            // populate all of tservers connections
            foreach (KeyValuePair<string, string> tserver in this.tServers)
            {
                //Console.WriteLine(tserver.Value);
                GrpcChannel channel = GrpcChannel.ForAddress(tserver.Value);
                channels.Add(tserver.Key, channel);
                tServerInstances.Add(tserver.Key, new TServerTServerService.TServerTServerServiceClient(channel));
            }
        }

        /* Transaction submitted by client
         *      - clientID
         *      - DadInts to read
         *      - DadInts to write
         *      
         *  For the TManager to reply, it needs a lease from the LManager
         *  After receiving permission it will submit the transaction, report to the other Tmanagers,
         *  store the DadInt value and release the lease
         */

        // Execute at the beginning of each epoch
        public void slotBeginning(int epoch)
        {
            // DEBUG
            Console.WriteLine("Key Access Queue:");
            foreach (var item in keyAccessQueue)
            {
                Console.Write($"Key: {item.Key} Queue Peek: {item.Value.Peek()} Queue Size: {item.Value.Count}\n");
            }
            /*Console.WriteLine("Dad Ints:");
            foreach (var dadInt in this.dadInts)
            {
                Console.WriteLine($"Key: {dadInt.Value.Key} Value: {dadInt.Value.Val}\n");
            }*/
            // END OF DEBUG

            foreach (var key in keyAccessChange)
            {
                if (keyAccessQueue[key.Key].Count > 1)
                    keyAccessChange[key.Key]++;
                else
                    keyAccessChange[key.Key] = 0;
            }

            // Kill the process if it's crashed in the process state for this epoch
            if (processStates[epoch - 1] != null)
            {
                if (processStates[epoch - 1][this.tManagerId].Crashed)
                {
                    Environment.Exit(0);
                }
            }

            // Get the suspected lServers from the processStates and add them to the list
            if (processStates[epoch - 1] != null)
            {
                foreach (KeyValuePair<string, ServerProcessState> server in this.processStates[epoch - 1])
                {
                    // Chech which entry is the current server
                    if (server.Key == this.tManagerId && server.Value.Suspects.Item1)
                    {
                        // Add the suspected servers to the list by their last character
                        foreach (string suspect in server.Value.Suspects.Item2)
                        {
                            if (!this.tServersSuspected.Contains(Int32.Parse(suspect.Substring(suspect.Length - 1))))
                            {
                                this.tServersSuspected.Add(Int32.Parse(suspect.Substring(suspect.Length - 1)));
                            }
                        }
                    }
                }
            }
        }

        public TxSubmitReply Transaction(TxSubmitRequest request)
        {
            // Prepares the request for the Lease Manager
            RepeatedField<string> reads = request.Key;
            RepeatedField<DadInt> writes = request.DadInts;

            // leaseKeys - a list of all the keys needed in the lease (non-repeating)
            // activeLeaseKeys - a list of all the keys that haven't been read/written in all active transactions for this TManager
            // concludedKeys - leaseKeys that have been concluded and their corresponding lease has been released
            // (repeats if one transaction uses the same key more than once)
            List<string> leaseKeys = new List<string>();
            List<string> concludedKeys = new List<string>();

            foreach (string key in reads)
            {
                if(!leaseKeys.Contains(key))
                    leaseKeys.Add(key);
                activeLeaseKeys.Add(key);
            }

            foreach (DadInt dadInt in writes)
            {
                if (!leaseKeys.Contains(dadInt.Key))
                    leaseKeys.Add(dadInt.Key);
                activeLeaseKeys.Add(dadInt.Key);
            }

            AskLeaseRequest askLeaseRequest = new AskLeaseRequest { TManagerId = this.tManagerId, Key = { leaseKeys } };

            // requests a lease from all of the LServers
            List<Task<AskLeaseReply>> askLeaseReplies = new List<Task<AskLeaseReply>>();
            // for each LServer starts a task with a request
            foreach (KeyValuePair<string, TServerLServerService.TServerLServerServiceClient> lServerInstance in this.lServerInstances)
            {
                // for each entry in LServers, makes a request for a lease
                AsyncUnaryCall<AskLeaseReply> leaseReply = lServerInstance.Value.AskLeaseAsync(askLeaseRequest);
                askLeaseReplies.Add(leaseReply.ResponseAsync);
            }
            // TODO
            // wait for the majority of the tasks to get a response
            // sort what it receives and return one reply to the function Transaction
            Task.WaitAll(askLeaseReplies.ToArray(), 500);

            // for now it returns the first response
            AskLeaseReply askLeaseReply = askLeaseReplies.First().Result;

            int numberLeasesNeeded = reads.Count + writes.Count;

            // List of dadInts to use as reply (read request by the client)
            List<DadInt> dadIntsToReply = new List<DadInt>();
            bool readWriteOperation;

            // Start of critical section
            lock (this)
            {

                // While not all keys of the request have been written/read, the transaction is not complete
                for (int i = 0; i < numberLeasesNeeded;)
                {
                    // Check wheter or not the next for loop releases leases
                    readWriteOperation = false;

                    foreach (var leaseKey in leaseKeys)
                    {
                        // If this manager is not allowed to access the key, check the next key
                        if (!keyAccessQueue.ContainsKey(leaseKey) || keyAccessQueue[leaseKey].Peek() != tManagerId)
                            continue;

                        else
                        {
                            int value = 0;
                            bool writtenValue = false;
                            // Write to the key if present in the client request
                            foreach (DadInt dInt in writes)
                            {
                                if (dInt.Key == leaseKey)
                                {
                                    if (dadInts.ContainsKey(dInt.Key))
                                        dadInts[dInt.Key] = dInt;
                                    else
                                        dadInts.Add(dInt.Key, new DadInt(dInt));
                                    value = dInt.Val;
                                    writtenValue = true;
                                }
                            }
                            // Read the key if present in the client request
                            foreach (string readKey in reads)
                            {
                                if (readKey == leaseKey)
                                {
                                    if (dadInts.ContainsKey(readKey))
                                        dadIntsToReply.Add(dadInts[readKey]);
                                }
                            }
                            // One of the keys was written/read, so we can increment i
                            i++;
                            readWriteOperation = true;
                            activeLeaseKeys.Remove(leaseKey);
                            //leaseKeys.Remove(leaseKey);
                            // If there is another instance of the key in another requests, don't release the lease just yet
                            if (!activeLeaseKeys.Contains(leaseKey))
                            {
                                concludedKeys.Add(leaseKey);
                                BroadcastRelease(leaseKey, value, writtenValue);
                            }
                            continue;
                        }
                    }
                    // If no keys were written/read, wait for a pulse (a change in keyAccessQueue - invoked by ReleaseLease or SendLeases)
                    if (!readWriteOperation)
                    {
                        Console.WriteLine("I am waiting for:");
                        foreach (var key in leaseKeys)
                        {
                            Console.WriteLine($"Key: {key}");
                        }
                        Monitor.Wait(this);

                        // After having received a pulse, the TManager that's holding the lease for some key might have crashed
                        foreach (var leaseKey in leaseKeys)
                        {
                            
                            if (!keyAccessChange.ContainsKey(leaseKey))
                            {
                                Console.WriteLine($"Continue 1 for key: {leaseKey}");
                                continue;
                            }

                            // If the key was already concluded, move on to the next one
                            // If the change of lease happened less than 2 epochs ago, continue to the next key
                            if (concludedKeys.Contains(leaseKey) || keyAccessChange[leaseKey] < 2)
                            {
                                Console.WriteLine($"Continue 2 for key: {leaseKey}. First condition: {concludedKeys.Contains(leaseKey)}. Second Condition: {keyAccessChange[leaseKey]}");
                                continue;
                            }

                            Console.WriteLine($"\nAdvance for key: {leaseKey}\n");

                            // Number of positive acks
                            int quorum = 0;
                            List<Task<AskReleaseReply>> taskList = new List<Task<AskReleaseReply>>();
                            // Ask all other TManagers if it makes sense to release the suspicious lease
                            AskReleaseRequest askReleaseRequest = new AskReleaseRequest
                            {
                                Key = leaseKey,
                                From = this.tManagerId,
                                To = keyAccessQueue[leaseKey].Peek() 
                            };
                            foreach (var tServer in this.tServerInstances)
                            {
                                var askReleaseReply = tServer.Value.AskReleaseAsync(askReleaseRequest);
                                taskList.Add(askReleaseReply.ResponseAsync);
                            }
                            Task.WaitAll(taskList.ToArray(), 500);

                            // Check if the responses where majorly positive
                            foreach (var task in taskList)
                            {
                                if (task.IsCompletedSuccessfully && task.Result.Ack)
                                    quorum++;
                            }
                            if (quorum >= majority)
                            {
                                keyAccessQueue[leaseKey].Dequeue();
                                this.keyAccessChange[leaseKey] = 0;
                            }
                        }
                    }
                }
                // End of critical section
            }

            // responds with the DadInts the client wants to read
            // If they do not exist, returns an empty list
            TxSubmitReply reply = new TxSubmitReply { DadInts = { dadIntsToReply } };

            Console.WriteLine("\nCONCLUDED TRANSACTION\n");

            return reply;
        }

        // Function that broadcasts the release and releases locally if necessary
        private bool BroadcastRelease(string key, int value, bool writtenValue) 
        {
            try
            {
                List<Task<ReleaseLeaseReply>> releaseReplyList = new List<Task<ReleaseLeaseReply>>();
                lock (this)
                {
                    if (this.keyAccessQueue[key].Peek() != this.tManagerId)
                    {
                        Console.WriteLine($"WARNING - Queue peek is wrong for: {key} and ID: {this.tManagerId}");
                        return false;
                    }
                    // Only release lease if any other TManager wants it
                    if (this.keyAccessQueue[key].Count > 1)
                    {
                        foreach (var tServer in this.tServerInstances)
                        {
                            ReleaseLeaseRequest releaseLeaseRequest = new ReleaseLeaseRequest
                            {
                                Key = key,
                                Written = writtenValue,
                                TManagerId = this.tManagerId
                            };
                            if (writtenValue)
                                releaseLeaseRequest.Value = value;

                            // This call has to be async, if we wait for the response we might get soft locked
                            var releaseReply = tServer.Value.ReleaseLeaseAsync(releaseLeaseRequest);
                            releaseReplyList.Add(releaseReply.ResponseAsync);
                        }
                        this.keyAccessQueue[key].Dequeue();
                        this.keyAccessChange[key] = 0;
                        Console.WriteLine($"Realesed Lease of Key: {key} and ID: {this.tManagerId}");
                    }
                }
                Task.WaitAll(releaseReplyList.ToArray(), 500);

                int completedTask = 0;
                foreach (var task in releaseReplyList)
                {
                    if (task.IsCompletedSuccessfully)
                        completedTask++;
                }

                if (completedTask >= majority)
                    return true;
                else
                    return false;
            }
            catch (Exception e) 
            {
                Console.WriteLine($"WARNING - Exception in Release Lease Reply of Key: {key} and ID: {this.tManagerId}\nException: {e}");
                return false; 
            }
        }

        // Function that releases the lease for a certain key, giving the lease to the next in queue
        public ReleaseLeaseReply ReleaseLease(ReleaseLeaseRequest request)
        {
            // Start of critical section
            Monitor.Enter(this);
            try
            {
                foreach (var release in this.releasesPending)
                {
                    if (keyAccessQueue[release.Key].Peek() == release.TManagerId)
                    {
                        this.keyAccessQueue[release.Key].Dequeue();
                        this.keyAccessChange[release.Key] = 0;
                        Monitor.PulseAll(this);
                    }
                }
                if (keyAccessQueue[request.Key].Peek() == request.TManagerId)
                {
                    this.keyAccessQueue[request.Key].Dequeue();
                    this.keyAccessChange[request.Key] = 0;
                    Monitor.PulseAll(this);
                    return new ReleaseLeaseReply { Ack = true };
                }
                else
                {
                    this.releasesPending.Add(request);
                    return new ReleaseLeaseReply { Ack = false };
                }
            }
            catch
            {
                return new ReleaseLeaseReply { Ack = false }; 
            }
            finally 
            {
                // End of critical section
                Monitor.Exit(this); 
            }
        }

        // Function evoked when some TManager suspect that a lease is being held by a crashed manager
        public AskReleaseReply AskRelease(AskReleaseRequest request) 
        {
            lock (this)
            {
                if (this.keyAccessQueue.ContainsKey(request.Key))
                {
                    // If the peek of the queue of the key corresponds to the ID of:
                    // - From: Perhaps the server that sent the request didn't receive the releaseLease from the server that had the lease
                    // - To: Perhaps the server is really crashed and is holding a lease
                    if (this.keyAccessQueue[request.Key].Peek() == request.From || this.keyAccessQueue[request.Key].Peek() == request.To)
                    {
                        this.keyAccessQueue[request.Key].Dequeue();
                        this.keyAccessChange[request.Key] = 0;
                        Monitor.PulseAll(this);
                        return new AskReleaseReply { Ack = true };
                    }
                }
                return new AskReleaseReply { Ack = false };
            }
        }
        
        // Evoked at the start of every epoch, LManagers send to the TManagers the queue of leases to be used in this epoch
        public SendLeasesReply SendLeases(SendLeasesRequest request)
        {
            // Start of critical section
            Monitor.Enter(this);
            try
            {
                Console.WriteLine($"Received the following number of leases: {request.Leases.Count}");

                List<string> wantKey = new List<string>();

                // Populate the keyAccess dictionary
                if (request.Leases.Count > 0)
                {
                    foreach (Lease lease in request.Leases)
                    {
                        // for each key present in one lease, verify if the dictionary already has an entry
                        // if it hasn't, add a new entry with that key
                        // if it has, only adds the Tmanager to the Queue
                        foreach (string key in lease.Key)
                        {
                            if (keyAccessQueue.ContainsKey(key))
                            {
                                keyAccessQueue[key].Enqueue(lease.TManagerId);
                            }
                            else
                            {
                                keyAccessQueue.Add(key, new Queue<string>(new[] { lease.TManagerId }));
                                keyAccessChange[key] = 0;
                            }

                            if (lease.TManagerId == this.tManagerId)
                                wantKey.Add(key);
                        }
                    }
                    foreach (var keyAccess in keyAccessQueue)
                    {
                        if (keyAccess.Value.Peek() == this.tManagerId &&
                            keyAccess.Value.Count > 1 &&
                            !wantKey.Contains(keyAccess.Key))
                        {
                            //Console.WriteLine("\nInertia BroadcastRelease\n");
                            BroadcastRelease(keyAccess.Key, this.dadInts[keyAccess.Key].Val, true);
                        }
                    }
                }
                // Notify that there has been a change to keyAccessQueue
                Monitor.PulseAll(this);
                return new SendLeasesReply { Ack = true };
            }
            catch { return new SendLeasesReply { Ack = false }; }
            finally
            {
                // End of critical section
                Monitor.Exit(this);
            }
        }

        public StatusReply State(StatusRequest request)
        {
            // TODO - use a broadcast algorithm to contact the other servers (2PC p.e)
            StatusReply reply = new StatusReply { Status = true };

            return reply;
        }
    }
}
