using Grpc.Core;
using Grpc.Net.Client;
using Protos;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Utilities;

namespace LServer.Services
{
    public class LServerService
    {
        // Server attributes
        private string lManagerID;
        private int serverId;      // needed in order to select the leader
        private int leaderId;

        private List<int> lServersId;
        private List<int> lServersSuspected = new List<int>();  // possibly a list with a string and an int

        private Dictionary<string, GrpcChannel> channels = new Dictionary<string, GrpcChannel>();
        private Dictionary<string, PaxosService.PaxosServiceClient> lServerInstances = new Dictionary<string, PaxosService.PaxosServiceClient>();
        private Dictionary<string, TServerLServerService.TServerLServerServiceClient> tServerInstances = new Dictionary<string, TServerLServerService.TServerLServerServiceClient>();
        private Dictionary<string, string> LServers;
        private Dictionary<string, string> TServers;

        private Dictionary<string, ServerProcessState>[] processStates;

        // Paxos related atributes
        int epoch = 0;
        int highestRoundId = 0;
        bool isLeader = false;
        bool isLeaderDead = false;
        int backoffTime = 100;

        Queue<AskLeaseRequest> leaseRequestQueue = new Queue<AskLeaseRequest>();
        List<Lease> leaseQueue = new List<Lease>();
        List<Lease> broadcastLeaseQueue = new List<Lease>();  // this is the value in paxos algorithm

        public LServerService(string lManagerID, int serverId, Dictionary<string, string> lServers, 
            Dictionary<string, string> tServers, List<int> LServersId, Dictionary<string, ServerProcessState>[] ProcessStates) 
        {
            this.lManagerID = lManagerID;
            this.LServers = lServers;
            this.TServers = tServers;
            this.serverId = serverId;
            this.lServersId = LServersId;
            this.processStates = ProcessStates;
            this.leaderId = 0;          // LeaderId is always > 0 ((for now))
           
            
            // populate all of lservers connections
            foreach (KeyValuePair<string, string> lserver in this.LServers)
            {
                //Console.WriteLine(lserver.Value);
                GrpcChannel channel = GrpcChannel.ForAddress(lserver.Value);
                channels.Add(lserver.Key, channel);
                lServerInstances.Add(lserver.Key, new PaxosService.PaxosServiceClient(channel));
            }

            // populate all of tservers connections
            foreach (KeyValuePair<string, string> tserver in this.TServers)
            {
                //Console.WriteLine(tserver.Value);
                GrpcChannel channel = GrpcChannel.ForAddress(tserver.Value);
                channels.Add(tserver.Key, channel);
                tServerInstances.Add(tserver.Key, new TServerLServerService.TServerLServerServiceClient(channel));
            }
        }

        /*
              _______ _____                              
             |__   __/ ____|                             
                | | | (___   ___ _ ____   _____ _ __ ___ 
                | |  \___ \ / _ \ '__\ \ / / _ \ '__/ __|
                | |  ____) |  __/ |   \ V /  __/ |  \__ \
                |_| |_____/ \___|_|    \_/ \___|_|  |___/
        */
        /* NewLease request by TServer
         * LServer replies with an ack, only at the end of an epoch
         * sends the list of Leases defined
         */
        public AskLeaseReply ProcessLeaseRequest(AskLeaseRequest request)
        {
            // Adds lease request to the queue (will be used in paxos)

            foreach (string key in request.Key)
            {
                Lease lease = new Lease
                {
                    TManagerId = request.TManagerId,
                    Key = { key }
                };
                // adds the lease request to the Queue if not there
                lock (leaseQueue)
                {
                    if (!leaseQueue.Contains(lease))
                        leaseQueue.Add(lease);
                }
            }
            Console.WriteLine("New LeaseRequest: " + request.Key + request.TManagerId);

            // Replies with an ack 
            AskLeaseReply leaseReply = new AskLeaseReply { Ack = true };
            Console.WriteLine("Response sent: " + leaseReply.Ack);

            return leaseReply;
        }

        /* 
         * Send the Leases in the beginning of a new epoch
         * in the beginning of the epoch, after deciding a leader
         * run the accept step of paxos, and send the current queue of leases to the tServers
         */
        public bool BroadcastLeases()
        {
            Console.WriteLine("Broadcasting Leases!");
            // Should have a lock when doing this
            // Prepares the request
            SendLeasesRequest leaseRequest = new SendLeasesRequest
            { 
                Epoch = this.epoch,
                Leases = { broadcastLeaseQueue }
            };

            List<SendLeasesReply> leaseReplies = new List<SendLeasesReply>();
            List<Task> t = new List<Task>();

            Console.WriteLine("Lease request has :" + leaseRequest.Leases.Count + " leases.");

            // Sends the list to every TManager that has sent a request
            foreach (KeyValuePair<string, TServerLServerService.TServerLServerServiceClient> tServerInstance in this.tServerInstances)
            {
                Task task = Task.Run(() =>
                {
                    try
                    {
                        SendLeasesReply leaseReply = tServerInstance.Value.SendLeases(leaseRequest);
                        leaseReplies.Add(leaseReply);
                    }
                    catch (RpcException ex)
                    {
                        //Console.WriteLine("Something whent wrong in a broadcast lease reply..." + ex.Status);
                    }
                    return Task.CompletedTask;
                });
                t.Add(task);
            }
            
            // waits some time for responses
            Task.WaitAll(t.ToArray(), 1000);

            // takes of the queue the first n elements that were broadcast
            lock (leaseQueue)
            {       
                leaseQueue.RemoveAll(lease => broadcastLeaseQueue.Contains(lease));
                broadcastLeaseQueue.Clear(); 
            }
            //Enumerable.Range(0, broadcastLeaseQueue.Count).Select(i => leaseQueue.Dequeue()).ToList();

            // TODO - for now it returns the first response received
            // verify if received, else the values will be lost
            return leaseReplies.First().Ack;
        }


        /*
              _       _____                              
             | |     / ____|                             
             | |    | (___   ___ _ ____   _____ _ __ ___ 
             | |     \___ \ / _ \ '__\ \ / / _ \ '__/ __|
             | |____ ____) |  __/ |   \ V /  __/ |  \__ \
             |______|_____/ \___|_|    \_/ \___|_|  |___/    
        */

        public PromiseReply PaxosPrepare(PrepareRequest request)
        {
            Console.WriteLine("Entered Paxos Prepare! (Step 1)");
            
            PromiseReply reply = new PromiseReply { Epoch = -1};
            
            // In case a leader receives a prepare request, compare the serverId and roundId, and if the serverId is higher,
            // accept the new leader and abort the prepare phase
            if (this.isLeader && request.ProposerId < this.serverId)
            {
                return reply;
            }

            // TODO - Consider the fact that a proposer can fail after accept,
            // i.e fill the "previousRoundId" and "queue" fields of the promiseReply.
            // Consider using an accept request list
            if (request.RoundId > this.highestRoundId)
            {
                this.highestRoundId = request.RoundId;
                this.leaderId = request.ProposerId;

                // sends its leaseQueue in case some is missing from the leader
                reply = new PromiseReply { Epoch = request.Epoch, RoundId = request.RoundId, Queue = { leaseQueue } };
            }
            else
                Console.WriteLine("Oops, roundID too low in prepare phase.");
            
            if (request.ProposerId == this.leaderId)
            {
                this.isLeaderDead = false;
            }

            return reply;
        }

        public AcceptedReply PaxosAccept(AcceptRequest request) 
        {
            Console.WriteLine("Entered Paxos Accept! (Step 2)");

            AcceptedReply reply = new AcceptedReply {  Epoch = -1 };

            if (request.RoundId >= this.highestRoundId)
            {
                // update broadcast value
                broadcastLeaseQueue.Clear();
                broadcastLeaseQueue = request.Queue.ToList();

                reply = new AcceptedReply { Epoch = request.Epoch, RoundId = request.RoundId, ServerId = this.serverId, Queue = { broadcastLeaseQueue } };

                lock (leaseQueue)
                {
                    leaseQueue.RemoveAll(lease => broadcastLeaseQueue.Contains(lease));
                }
            }
            else
                Console.WriteLine("Oops, roundID too low in accept phase.");

            // verify if not the leader can do an accept without prepare phase
            this.isLeaderDead = false;            

            return reply;
        }

        /*
              _____        __   ______   _____ 
             |  __ \ /\    \ \ / / __ \ / ____|
             | |__) /  \    \ V / |  | | (___  
             |  ___/ /\ \    > <| |  | |\___ \ 
             | |  / ____ \  / . \ |__| |____) |
             |_| /_/    \_\/_/ \_\____/|_____/ 
        */

        /*
         * Consensus: Main execution of the Paxos algorithm
         * Checks which process should be leader.
         * Then if it's the leader, sends a PrepareRequest to all the other servers notifying
         * If it's not the leader it will wait for a PrepareRequest.
         *      - if it receives, accepts leader
         *      - if not repeats the Function, because leader must have crashed
         */
        public void Consensus(int epoch)
        {
            // Kill the process if it's crashed in the process state for this epoch
            if (processStates[epoch - 1] != null)
            {
                if (processStates[epoch - 1][this.lManagerID].Crashed)
                {
                    Environment.Exit(0);
                }
            }

            // Update the epoch
            this.epoch = epoch;

            Console.WriteLine("Leases in broadcast: " + broadcastLeaseQueue.Count);


            // Get the suspected lServers from the processStates and add them to the list
            if (processStates[epoch - 1] != null)
            {
                foreach (KeyValuePair<string, ServerProcessState> server in this.processStates[epoch - 1])
                {
                    // Chech which entry is the current server
                    if (server.Key == this.lManagerID && server.Value.Suspects.Item1)
                    {
                        // Add the suspected servers to the list by their last character
                        foreach (string suspect in server.Value.Suspects.Item2)
                        {
                            if (!this.lServersSuspected.Contains(Int32.Parse(suspect.Substring(suspect.Length - 1))))
                            {
                                this.lServersSuspected.Add(Int32.Parse(suspect.Substring(suspect.Length - 1)));
                            }
                        }
                    }
                }
            }

            // Leader verification
            
            int currentLeaderId = -1;
            this.isLeaderDead = true;    // false if the server does prepare or accept requests

            // leaderId will always be the server with lower id, and not suspected

            // if the leader has not been defined or is suspected, calculates the new leader
            if (this.leaderId == 0 || this.lServersSuspected.Contains(this.leaderId))
            {
                foreach (int sId in this.lServersId)
                {
                    // makes it possible to loop through the Lservers
                    if (sId > (this.leaderId % this.lServersId.Count) && !this.lServersSuspected.Contains(sId))
                    {
                        currentLeaderId = sId;
                        break;
                    }
                }
            }
            // leader stays the same
            else
                currentLeaderId = this.leaderId;

            Console.WriteLine("IM SERVER: " + this.serverId);

            // if the currentLeaderId is this server, waits for promiseReplies from the majority of lservers
            if (this.serverId == currentLeaderId)
            {
                // TODO - maybe run BroadcastLeases asynchronously (we might want to know the result of the function, though)
                int succeededPrepare = ConsensusLeader(currentLeaderId, epoch);
                // TODO - if it doesn't succeed, backoff time and repeats. Stops when a promise reply sends a Nack, with the current leader being different
                switch (succeededPrepare)
                {
                    // Everything went fine and can broadcast the leases
                    case 1:
                        this.backoffTime = 100;
                        BroadcastLeases();
                        break;

                    // Didn't succeed in the prepare phase due to not enough promise/accept replies, backoff time and repeats
                    case 0:
                        Console.WriteLine("Didn't succeed in the prepare/accept phase due to not enough promise/accept replies.");
                        Thread.Sleep(this.backoffTime + (this.serverId * 10));
                        this.backoffTime *= 2;
                        Consensus(epoch);
                        break;

                    // Another server is leader, aborted the prepare phase
                    case -1:
                        Console.WriteLine("Another server wants to be leader, aborted the prepare phase.");
                        this.backoffTime = 100;
                        break;
                }
            }

            // awaits for the leader to make a prepareRequest, and sends a promiseReply
            else
            {
                //ConsensusAcceptor(currentLeaderId);
                
                // Waits for some prepare/accept
                // if doesn't receive any from the leader in the beginning of the epoch
                Thread.Sleep(4000);
                // if leader is dead, add to the suspected list
                if (this.isLeaderDead)
                {
                    Console.WriteLine("Server Leader is possibly dead :(");
                    if (!this.lServersSuspected.Contains(this.leaderId))
                    {
                        this.lServersSuspected.Add(this.leaderId);
                    }
                    this.leaderId = currentLeaderId;
                }
                else
                    Console.WriteLine("Everything is fine, Leader is alive and responsive!");

                this.backoffTime = 100;
            }
            Console.WriteLine("End of Consensus Epoch");
        }

        public int ConsensusLeader(int currentLeaderId, int epoch) 
        {
            this.isLeader = true;

            // Prepare Phase (Step 1)
            // only enters prepare phase in case it's a new leader!
            if (currentLeaderId != this.leaderId)
            {
                int currentRoundId = this.highestRoundId + 1;

                List<PromiseReply> promiseReplies = new List<PromiseReply>();
                List<Task> pTasks = new List<Task>();

                PrepareRequest prepareRequest = new PrepareRequest { Epoch = epoch, ProposerId = this.serverId, RoundId = currentRoundId };

                foreach (KeyValuePair<string, PaxosService.PaxosServiceClient> lServerInstances in this.lServerInstances)
                {
                    Task t = Task.Run(() =>
                    {
                        try
                        {
                            PromiseReply promiseReply = lServerInstances.Value.Prepare(prepareRequest);
                            promiseReplies.Add(promiseReply);
                        }
                        catch (RpcException ex)
                        {
                            //Console.WriteLine("Something whent wrong in a promise reply..." + ex.Status);
                        }
                        return Task.CompletedTask;
                    });
                    pTasks.Add(t);
                }

                // waits some time for responses
                Task.WaitAll(pTasks.ToArray(), 1500);

                // If the promise replies are not the majority, the prepare phase has failed
                if (promiseReplies.Count < (this.lServersId.Count - this.lServersSuspected.Count) / 2)
                {
                    Console.WriteLine("Leader Failed in promise replies...: " + promiseReplies.Count);
                    this.isLeader = false;
                    return 0;
                }

                // If it receives a reply with an epoch = -1, it means that the leader has changed
                foreach (PromiseReply promiseReply in promiseReplies)
                {
                    if (promiseReply.Epoch == -1)
                    {
                        this.isLeader = false;
                        return -1;
                    }
                }

                this.highestRoundId = currentRoundId;
                Console.WriteLine("I am the leader: " + serverId + ", and ran the prepare phase.");
                
                // call a function to verify and update the Queue of leases in case of missing leases
                /*foreach (PromiseReply promiseReply in promiseReplies)
                {
                    CompareLeaseQueue(promiseReply.Queue.ToList());
                    Console.WriteLine("Comparing . . .");
                }*/
            }

            // Accept Phase (Step 2)

            List<AcceptedReply> acceptedReplies = new List<AcceptedReply>();
            List<Task> aTasks = new List<Task>();

            // update the list of leases to broadcast and send as the new value to the acceptors
            // it may receive requests in this small fraction of time, locks the value that will be sent
            lock (leaseQueue)
            {
                lock (broadcastLeaseQueue)
                {
                    if (broadcastLeaseQueue.Count == 0)
                        broadcastLeaseQueue = leaseQueue;
                    else
                        foreach(var lease in leaseQueue)
                        {
                            if (!broadcastLeaseQueue.Contains(lease))
                                broadcastLeaseQueue.Add(lease);
                        }
                }
            }

            AcceptRequest acceptRequest = new AcceptRequest { Epoch = epoch, RoundId = this.highestRoundId , Queue = { broadcastLeaseQueue } };

            foreach (KeyValuePair<string, PaxosService.PaxosServiceClient> lServerInstances in this.lServerInstances)
            {
                Task t = Task.Run(() =>
                {
                    try
                    {
                        AcceptedReply acceptedReply = lServerInstances.Value.Accept(acceptRequest);
                        acceptedReplies.Add(acceptedReply);
                    }
                    catch (RpcException ex)
                    {
                        //Console.WriteLine("Something whent wrong in a accept reply..." + ex.Status);
                    }
                    return Task.CompletedTask;
                });
                aTasks.Add(t);
            }

            // waits some time for responses
            Task.WaitAll(aTasks.ToArray(), 1000);

            // If the accepted replies are not the majority, the accept phase has failed
            if (acceptedReplies.Count < (this.lServersId.Count - this.lServersSuspected.Count) / 2)
            {
                Console.WriteLine("Leader Failed in accept replies...: " + acceptedReplies.Count);
                this.isLeader = false;
                return 0;
            }

            // TODO - maybe not do it here
            // Reviews the suspected servers
            foreach (AcceptedReply acceptedReply in acceptedReplies)
            {
                if (this.lServersSuspected.Contains(acceptedReply.ServerId))
                {
                    this.lServersSuspected.Remove(acceptedReply.ServerId);
                }
            }

            Console.WriteLine("Here is the consensual queue: ");
            foreach (var lease in acceptRequest.Queue)
            {
                Console.Write(lease.TManagerId + lease.Key + "\n");
            }
            
            this.leaderId = currentLeaderId;
            this.isLeader = false;
            return 1;
        }

    
        // not yet used, but eventually
        public void CompareLeaseQueue(List<Lease> acceptorsQueue)
        {
            foreach (Lease lease in acceptorsQueue)
            {
                lock (leaseQueue)
                {
                    if (!leaseQueue.Contains(lease))
                        leaseQueue.Add(lease);
                }
            }
        }
    }
}
