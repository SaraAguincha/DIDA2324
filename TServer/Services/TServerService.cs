using Google.Protobuf.Collections;
using Grpc.Core;
using Grpc.Net.Client;
using Protos;
using System;
using System.Collections.Generic;
using System.Linq;
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
        
        private Dictionary<string, Queue<string>> keyAccessQueue = new Dictionary<string, Queue<string>>();

        private Dictionary<string, ServerProcessState>[] processStates;


        // set all the server information from config
        public TServerService(string tManagerId, Dictionary<string, string> tServers, Dictionary<string, string> lServers,
            Dictionary<string, ServerProcessState>[] ProcessStates)
        {
            this.tManagerId = tManagerId;
            this.tServers = tServers;
            this.lServers = lServers;
            this.processStates = ProcessStates;
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
            //DEBUG
            Console.WriteLine("Key Access Queue:");
            foreach (var item in keyAccessQueue)
            {
                Console.Write("Key: {0} Queue Peek: {1} Queue Size: {2}\n", item.Key, item.Value.Peek(), item.Value.Count);
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
            // TODO - a way to not repeat this verification every time in every command for every client,
            // right now it only works for one client.. Choose one way to store the clients/channels
            /* is this necessary ??
            if (client == null)
            {
                this.channel = GrpcChannel.ForAddress("http://localhost:10000");
                this.client = new ClientTServerService.ClientTServerServiceClient(channel);
            }*/

            // prepares the request for the Lease Manager
            RepeatedField<string> reads = request.Key;
            RepeatedField<DadInt> writes = request.DadInts;

            // makes a list of all the keys needed in the lease
            List<string> leaseKeys = new List<string>();

            foreach (string key in reads)
            {
                if (!leaseKeys.Contains(key))
                    leaseKeys.Add(key);
            }

            foreach (DadInt dadInt in writes)
            {
                if (!leaseKeys.Contains(dadInt.Key))
                    leaseKeys.Add(dadInt.Key);
            }

            AskLeaseRequest askLeaseRequest = new AskLeaseRequest { TManagerId = this.tManagerId, Key = { leaseKeys }};

            // TODO - should be async, currently not
            //AskLeaseReply leaseReply = RequestLease(leaseRequest);

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
            Console.WriteLine("server response to askLease: " + askLeaseReply.Ack);

            // TODO - wait for the access to the keys
            // SHOULD ALWAYS WORK! - add a way to forcefully break and get a lease (in case a TM crashes while having it)
            // HERE BLOCKS!
            int numberLeasesNeeded = leaseKeys.Count;

            for (int i = 0; i < numberLeasesNeeded;)
            {
                if (keyAccessQueue.ContainsKey(leaseKeys[i]))
                {
                    string firstInQueue = keyAccessQueue[leaseKeys[i]].Peek();
                    if (firstInQueue == tManagerId)
                        i++;
                    Thread.Sleep(100);
                }
            }

            // when all of the keys are covered by the lease (always after the for)
            // updates/adds the DadInts from the write part of the transaction
            foreach (DadInt dInt in writes)
            {
                if (dadInts.ContainsKey(dInt.Key))
                    dadInts[dInt.Key] = dInt;
                else
                    dadInts.Add(dInt.Key, new DadInt (dInt));
                // TODO - this should only be done after all the databases between the TManagers are consistent
                BroadcastRelease(dInt.Key);
            }

            // Transaction reply is only about the reads
            // if there is no DadInt to read, returns empty
            List<DadInt> dadIntsToReply = new List<DadInt>();
            foreach (string key in reads)
            {
                if (dadInts.ContainsKey(key))
                    dadIntsToReply.Add(dadInts[key]);
                // TODO - this should only be done after all the databases between the TManagers are consistent
                BroadcastRelease(key);
            }

            // responds with the DadInts the client wants to read
            // If it they do not exist, returns an empty list
            TxSubmitReply reply = new TxSubmitReply { DadInts = { dadIntsToReply } };

            // TODO before replying broadcast to the other TServers and update the DadInts values

            return reply;
        }

        private bool BroadcastRelease(string key) 
        {
            if (this.keyAccessQueue[key].Count > 1)
            {
                foreach (var tServer in this.tServerInstances)
                {
                    var releaseReply = tServer.Value.ReleaseLease(new ReleaseLeaseRequest { Key = key, TManagerId = this.tManagerId });
                    if (!releaseReply.Ack)
                    {
                        Console.WriteLine("WARNING - Something really wrong happened with the Release Lease Reply of Key: {0} and ID: {1}", key, this.tManagerId);
                        return false;
                    }
                }
                if (this.keyAccessQueue[key].Peek() == this.tManagerId)
                {
                    this.keyAccessQueue[key].Dequeue();
                    Console.WriteLine("Realesed Lease of Key: {0} and ID: {1}\n", key, this.tManagerId);
                }
            }
            return true;
        }

        public ReleaseLeaseReply ReleaseLease(ReleaseLeaseRequest request)
        {
            try
            {
                if (this.keyAccessQueue[request.Key].Peek() == request.TManagerId)
                {
                    this.keyAccessQueue[request.Key].Dequeue();
                    return new ReleaseLeaseReply { Ack = true };
                }
                else
                    return new ReleaseLeaseReply { Ack = false };
            }
            catch { return new ReleaseLeaseReply { Ack = false }; };
        }


        public StatusReply State(StatusRequest request)
        {
            /* TODO - is this necessary ??
            if (client == null) {
                this.channel = GrpcChannel.ForAddress("http://localhost:10000");
                this.client = new ClientTServerService.ClientTServerServiceClient(channel);
            }*/

            // TODO - use a broadcast algorithm to contact the other servers (2PC p.e)
            StatusReply reply = new StatusReply { Status = true };

            return reply;
        }
        
        // when the LServer leader broadcasts its leaseQueue, TServer receives it
        // and populates their own access lease dictionary
        public SendLeasesReply SendLeases(SendLeasesRequest request)
        {
            SendLeasesReply reply = new SendLeasesReply { Ack = true };

            // TODO - Functions itself - process the ordered leases in the TManager

            Console.WriteLine("Received the following number of leases: " + request.Leases.Count);
            
            // populate the keyAccess dictionary
            // TODO - verify if it is populated in the right order
            if (request.Leases.Count > 0) { 
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
                            keyAccessQueue.Add(key, new Queue<string> (new [] { lease.TManagerId }));
                    }
                }
                // DEBUG    ----------------------------------------------------------
                /*foreach (KeyValuePair<string,Queue<string>> keyA in keyAccessQueue)
                {
                    Console.WriteLine("Lease with name: " + keyA.Key + "\n");
                    foreach (string id in keyA.Value)
                    {
                        Console.WriteLine("Has this TManagers waiting: " + id);
                    }
                }
                //          ----------------------------------------------------------*/
            }
            return reply;
        }
    }
}
