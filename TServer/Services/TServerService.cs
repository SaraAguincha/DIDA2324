using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Protos;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private Dictionary<string, string> allTServers;
        private int epochDuration;
        private Dictionary<string, DadInt> dadInts = new Dictionary<string, DadInt> ();

        // All received ReleasesRequests 
        private List<ReleaseLeaseRequest> releasesList = new List<ReleaseLeaseRequest> ();
        // TManagers' queue of access for each key
        private Dictionary<string, List<string>> attributedAccesses = new Dictionary<string, List<string>>();
        // keyAccess: Value can be 1. Who should release the key 2. This TManager 3. Empty string (key no longer needed)
        private Dictionary<string, string> keyAccess = new Dictionary<string, string> ();
        // Count how many epochs a certain key has gone without a change of access to this tManager
        private Dictionary<string, int> keyAccessKeep = new Dictionary<string, int> ();
        // Data from configuration file
        private Dictionary<string, ServerProcessState>[] processStates;
        // All the active wanted leases for this TManager
        private List<string> activeLeaseKeys = new List<string> ();

        // Majority (calculated in the constructor)
        int majority = 0;
        // Counter of replies of LMs, SendLeases service
        int consensusLeasesReceived = 0;


        // set all the server information from config
        public TServerService(string tManagerId,
                              Dictionary<string, string> tServers,
                              Dictionary<string, string> lServers,
                              int duration,
                              Dictionary<string, ServerProcessState>[] ProcessStates)
        {
            this.tManagerId = tManagerId;
            this.tServers = tServers;
            this.allTServers = tServers;
            this.lServers = lServers;
            this.epochDuration = duration;
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
            lock (this)
            {
                // DEBUG
                /*Console.WriteLine("Attributed Accesses:");
                foreach (var item in attributedAccesses)
                {
                    Console.Write($"Key: {item.Key} Queue Peek: {item.Value[0]} Queue Size: {item.Value.Count}\n");
                }
                Console.WriteLine("Key Access:");
                foreach(var item in keyAccess)
                {
                    Console.WriteLine($"Key: {item.Key} Manager: {item.Value}");
                }*/
                /*Console.WriteLine("Dad Ints:");
                foreach (var dadInt in this.dadInts)
                {
                    Console.WriteLine($"Key: {dadInt.Value.Key} Value: {dadInt.Value.Val}\n");
                }*/
                // END OF DEBUG
            }

            // Resets the counter of leases received
            consensusLeasesReceived = 0;

            this.tServers = this.allTServers;

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
                            if (this.tServers.ContainsKey(suspect))
                                this.tServers.Remove(suspect);
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
                if (!leaseKeys.Contains(key))
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
            Task.WaitAll(askLeaseReplies.ToArray(), this.epochDuration / 20);

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
                        if (!keyAccess.ContainsKey(leaseKey) || keyAccess[leaseKey] != tManagerId)
                            continue;

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
                            // If the key was already concluded, move on to the next one
                            // If the change of lease happened less than 2 epochs ago, continue to the next key
                            if (!keyAccessKeep.ContainsKey(leaseKey) || concludedKeys.Contains(leaseKey) || keyAccessKeep[leaseKey] < 2)
                            {
                                Console.WriteLine($"Continue 1 for key: {leaseKey}");
                                continue;
                            }

                            Console.WriteLine($"AVANCED FOR {leaseKey}");

                            // Number of positive acks
                            int quorum = 0;
                            List<Task<AskReleaseReply>> taskList = new List<Task<AskReleaseReply>>();
                            // Ask all other TManagers if it makes sense to release the suspicious lease
                            AskReleaseRequest askReleaseRequest = new AskReleaseRequest
                            {
                                Key = leaseKey,
                                From = this.tManagerId,
                                To = keyAccess[leaseKey]
                            };
                            try
                            {
                                foreach (var tServer in this.tServerInstances)
                                {
                                    var askReleaseReply = tServer.Value.AskReleaseAsync(askReleaseRequest);
                                    taskList.Add(askReleaseReply.ResponseAsync);
                                }
                                Task.WaitAll(taskList.ToArray(), this.epochDuration / 10);
                            }
                            catch (Exception ex) 
                            {
                                Console.WriteLine("Exception at transaction:" + ex.ToString()); 
                            }

                            // Check if the responses where majorly positive
                            foreach (var task in taskList)
                            {
                                if (task.IsCompletedSuccessfully && task.Result.Ack)
                                    quorum++;
                            }
                            if (quorum >= 0)
                            {
                                keyAccess[leaseKey] = "";
                                this.keyAccessKeep[leaseKey] = 0;
                            }
                        }
                    }
                }
            }
            // End of critical section
            // responds with the DadInts the client wants to read
            // If they do not exist, returns an empty list
            TxSubmitReply reply = new TxSubmitReply { DadInts = { dadIntsToReply } };

            Console.WriteLine("\nCONCLUDED TRANSACTION\n");

            // TODO - Propagate written values (don't only send values when the leases change)

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
                    if (this.keyAccess[key] != this.tManagerId)
                    {
                        Console.WriteLine($"WARNING - Queue peek is wrong for: {key} and ID: {this.tManagerId}");
                        return false;
                    }
                    // Only release lease if any other TManager wants it
                    if (this.attributedAccesses[key].Last() != this.tManagerId)
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
                        this.keyAccess[key] = "";
                        this.keyAccessKeep[key] = 0;
                        Console.WriteLine($"\nRealesed Lease of Key: {key} and ID: {this.tManagerId}\n");
                    }
                }
                Task.WaitAll(releaseReplyList.ToArray(), this.epochDuration / 20);

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
                if (keyAccess[request.Key] == this.tManagerId)
                    return new ReleaseLeaseReply { Ack = false };

                this.releasesList.Add(request);
                this.keyAccessKeep[request.Key] = 0;
                if (keyAccess[request.Key] == request.TManagerId)
                {
                    this.keyAccess[request.Key] = this.tManagerId;
                    Monitor.PulseAll(this);
                }
                return new ReleaseLeaseReply { Ack = true };    
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
            // TODO - Think a bit about if this is a good idea
            if (attributedAccesses[request.Key].Contains(this.tManagerId))
                Monitor.PulseAll(this);
            return new AskReleaseReply { Ack = true };
            /*lock (this)
            {
                Console.WriteLine($"\nASK RELEASE: From: {request.From} To: {request.To}\n");
                // TManager is suspected anyways, reply with ack to the release
                bool leaseWasReleased = false;
                bool managerIsAlive = false;

                if (this.attributedAccesses.ContainsKey(request.Key) && this.tServers.ContainsKey(request.To))
                {
                    foreach (var release in releasesList)
                    {
                        if (release.TManagerId == request.To)
                        {
                            if (release.Key == request.Key)
                                leaseWasReleased = true;
                            managerIsAlive = true;
                        }
                    }
                    // TODO - Implement this
                    if (!leaseWasReleased && managerIsAlive)
                    {
                        return new AskReleaseReply { Ack = false };
                    }

                }
                keyAccessKeep[request.Key] = 0;
                if (attributedAccesses[request.Key].Contains(this.tManagerId))
                    Monitor.PulseAll(this);
                return new AskReleaseReply { Ack = true };
            }*/
        }
        
        // Evoked at the start of every epoch, LManagers send to the TManagers the queue of leases to be used in this epoch
        public SendLeasesReply SendLeases(SendLeasesRequest request)
        { 
            // Start of critical section
            Monitor.Enter(this);
            try
            {
                // Only if has not updated the Leases in this epoch
                if (consensusLeasesReceived != 0)
                    return new SendLeasesReply { Ack = true };
                var attributedAccessesCopy = attributedAccesses.ToDictionary(entry => entry.Key, entry => entry.Value);
                foreach (var access in attributedAccessesCopy)
                {
                    attributedAccesses[access.Key] = new List<string> { access.Value.Last() };
                }

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
                            if (attributedAccesses.ContainsKey(key))
                            {
                                attributedAccesses[key].Add(lease.TManagerId);
                            }
                            else
                            {
                                attributedAccesses.Add(key, new List<string> { lease.TManagerId });
                                keyAccessKeep[key] = 0;
                            }

                            if (lease.TManagerId == this.tManagerId)
                                wantKey.Add(key);
                        }
                    }
                    foreach (var access in attributedAccesses)
                    {
                        string tManagerAccess = "";
                        if (access.Value.Contains(this.tManagerId))
                        {
                            int selfIndex = access.Value.IndexOf(this.tManagerId);
                            tManagerAccess = access.Value[Math.Max(0, selfIndex - 1)];
                        }
                        if (!keyAccess.ContainsKey(access.Key))
                            keyAccess.Add(access.Key, tManagerAccess);
                        keyAccess[access.Key] = tManagerAccess;

                        /*if (access.Value.Contains(this.tManagerId) && access.Value.Count > 1)
                            keyAccessKeep[access.Key]++;
                        else
                            keyAccessKeep[access.Key] = 0;*/

                        if (access.Value[0] == this.tManagerId &&
                            access.Value.Count > 1 &&
                            !wantKey.Contains(access.Key))
                        {
                            Console.WriteLine("\nInertia BroadcastRelease\n");
                            BroadcastRelease(access.Key, this.dadInts[access.Key].Val, true);
                        }   
                    }
                    // Notify that there has been a change to keyAccessQueue
                    Monitor.PulseAll(this);

                }
                foreach (var access in attributedAccesses)
                {
                    if (access.Value.Contains(this.tManagerId) && keyAccess[access.Key] != this.tManagerId)
                        keyAccessKeep[access.Key]++;
                    else
                        keyAccessKeep[access.Key] = 0;
                    if (keyAccessKeep[access.Key] >= 2 && request.Leases.Count == 0)
                    {
                        Monitor.PulseAll(this);
                    }
                }

                releasesList = new List<ReleaseLeaseRequest>();
                Console.WriteLine("Key Access:");
                foreach (var item in keyAccess)
                {
                    Console.WriteLine($"Key: {item.Key} Manager: {item.Value} Keep: {keyAccessKeep[item.Key]}");
                } 

                return new SendLeasesReply { Ack = true };
            }
            catch (Exception ex) 
            {
                Console.WriteLine($"Exception at send leases: {ex}");
                return new SendLeasesReply { Ack = false }; 
            }
            finally
            {
                // End of critical section
                consensusLeasesReceived++;
                Monitor.Exit(this);   
            }
        }

        // State function to reply to client tstatus requests
        public TStatusReply State(TStatusRequest request)
        {
            //Print the server id and the status
            //Console.WriteLine("I am server " + this.tManagerId + " and I am alive!");

            TStatusReply reply = new TStatusReply { Status = true };

            return reply;
        }
    }
}
