using Grpc.Core;
using Grpc.Net.Client;
using Protos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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


        // Paxos related atributes
        int epoch = 0;          // should be ++ in the start of consensus perhaps?
        int highestRoundId = 0;
        bool isLeaderDead = false;

        Queue<AskLeaseRequest> leaseRequestQueue = new Queue<AskLeaseRequest>();
        Queue<Lease> leaseQueue = new Queue<Lease>();

        public LServerService(string lManagerID, int serverId, Dictionary<string, string> lServers, 
            Dictionary<string, string> tServers, List<int> LServersId) 
        {
            this.lManagerID = lManagerID;
            this.LServers = lServers;
            this.TServers = tServers;
            this.serverId = serverId;
            this.lServersId = LServersId;
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
            // TODO - dont create leases identical to existing ones...

            foreach (string key in request.Key)
            {
                Lease lease = new Lease
                {
                    TManagerId = request.TManagerId,
                    Key = { key }
                };
                // adds the lease request to the Queue
                leaseQueue.Enqueue(lease);
            }

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
            // Should have a lock when doing this
            // Prepares the request
            SendLeasesRequest leaseRequest = new SendLeasesRequest
            { 
                Epoch = this.epoch,
                Leases = { leaseQueue.ToArray() }
            };

            // TODO - WARNING - The operations between sending and reseting the queue must be atomic
            leaseQueue = new Queue<Lease>();

            List<Task<SendLeasesReply>> leaseReplies = new List<Task<SendLeasesReply>>();

            Console.WriteLine("Lease request has :" + leaseRequest.Leases.Count + " leases.");

            // Sends the list to every TManager that has sent a request
            foreach (KeyValuePair<string, TServerLServerService.TServerLServerServiceClient> tServerInstances in this.tServerInstances)
            {
                // for each entry in TServers, run a task with the current value in the Leases Queue
                AsyncUnaryCall<SendLeasesReply> leaseReply = tServerInstances.Value.SendLeasesAsync(leaseRequest);
                leaseReplies.Add(leaseReply.ResponseAsync);
            }
            // wait for the responses
            Task.WaitAll(leaseReplies.ToArray(), 500);

            // TODO - for now it returns the first response received
            return leaseReplies.First().Result.Ack;
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

            // TODO - Consider the fact that a proposer can fail after accept,
            // i.e fill the "previousRoundId" and "queue" fields of the promiseReply.
            // Consider using an accept request list
            if (request.RoundId > this.highestRoundId)
            {
                this.highestRoundId = request.RoundId;
                this.leaderId = request.ProposerId;

                reply = new PromiseReply { Epoch = request.Epoch, RoundId = request.RoundId };
                foreach (var leaseRequest in leaseRequestQueue)
                {
                    Lease paxosLease = new Lease { TManagerId = leaseRequest.TManagerId };
                    foreach (var key in leaseRequest.Key){ paxosLease.Key.Add(key); }
                    reply.Queue.Add(paxosLease);
                }
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
                reply = new AcceptedReply { Epoch = request.Epoch, RoundId = request.RoundId, ServerId = this.serverId };
                foreach (var lease in request.Queue) { reply.Queue.Add(lease); }
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
            // Leader verification
            
            int currentLeaderId = -1;
            this.isLeaderDead = true;    // false if the server does prepare or accept requests

            // leaderId will always be the server with lower id, and not suspected

            // if the leader has not been defined or is suspected, calculates the new leader
            // TODO - in case this.leaderId + 1, does not exist, it starts again from the beginning
            if (this.leaderId == 0 || this.lServersSuspected.Contains(this.leaderId))
            {
                foreach (int sId in this.lServersId)
                {
                    // do the mod (?), after some point, we should 
                    if (sId > this.leaderId && !this.lServersSuspected.Contains(sId))
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
            // LEADER AND ACCEPTORS BEHAVIORS

            // if the currentLeaderId is this server, waits for promiseReplies from the majority of lservers
            if (this.serverId == currentLeaderId)
            {
                // TODO - maybe run BroadcastLeases asynchronously (we might want to know the result of the function, though)
                bool succeededPrepare = ConsensusLeader(currentLeaderId, epoch);
                // TODO - if it doesn't succeed, backoff time and repeats. Stops when a promise reply sends a Nack, with the current leader being different
                BroadcastLeases();
            }

            // awaits for the leader to make a prepareRequest, and sends a promiseReply
            else
            {
                //ConsensusAcceptor(currentLeaderId);
                
                // Waits for some prepare/accept
                // if doesn't receive any from the leader in the beginning of the epoch
                Thread.Sleep(1000);
                // if leader is dead, add to the suspected list
                if (this.isLeaderDead)
                {
                    this.lServersSuspected.Add(this.leaderId);
                }
                else
                    Console.WriteLine("Everything is fine, Leader is alive and responsive!");
                this.leaderId = currentLeaderId;
            }
        }

        public bool ConsensusLeader(int currentLeaderId, int epoch) 
        {
            // Prepare Phase (Step 1)
            // only enters prepare phase in case it's a new leader!
            if (currentLeaderId != this.leaderId)
            {
                int currentRoundId = this.highestRoundId + 1;

                //List<Task<PromiseReply>> promiseReplies = new List<Task<PromiseReply>>();

                List<PromiseReply> promiseReplies = new List<PromiseReply>();
                List<Task> pTasks = new List<Task>();

                PrepareRequest prepareRequest = new PrepareRequest { Epoch = epoch, ProposerId = this.serverId, RoundId = currentRoundId };

                /*  NOT WORKING
                foreach (KeyValuePair<string, PaxosService.PaxosServiceClient> lServerInstances in this.lServerInstances)
                {
                    // for each entry in LServers, run a task with the request for a promise
                    AsyncUnaryCall<PromiseReply> promiseReply = lServerInstances.Value.PrepareAsync(prepareRequest);
                    promiseReplies.Add(promiseReply.ResponseAsync);
                }*/

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
                Task.WaitAll(pTasks.ToArray(), 500);

                // If the promise replies are not the majority, the prepare phase has failed
                // TODO - verify how the Quorum should be selected. Should not count with suspected servers p.e
                if (promiseReplies.Count < (this.lServersId.Count - this.lServersSuspected.Count) / 2)
                {
                    return false;
                }
                this.highestRoundId = currentRoundId;
                Console.WriteLine("I am the leader: " + serverId + ", and ran the prepare phase.");
            }

            // Accept Phase (Step 2)

            //List<Task<AcceptedReply>> acceptedReplies = new List<Task<AcceptedReply>>();
            List<AcceptedReply> acceptedReplies = new List<AcceptedReply>();
            List<Task> aTasks = new List<Task>();

            AcceptRequest acceptRequest = new AcceptRequest { Epoch = epoch, RoundId = this.highestRoundId };

            if (leaseQueue.Count > 0)
            {
                foreach (var lease in leaseQueue) { acceptRequest.Queue.Add(lease); }
            }
            /*  NOT WORKING
            foreach (KeyValuePair<string, PaxosService.PaxosServiceClient> lServerInstances in this.lServerInstances)
            {
                AsyncUnaryCall<AcceptedReply> acceptedReply = lServerInstances.Value.AcceptAsync(acceptRequest);
                acceptedReplies.Add(acceptedReply.ResponseAsync);
            }*/

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

            Task.WaitAll(aTasks.ToArray(), 500);

            // If the accepted replies are not the majority, the accept phase has failed
            // TODO - verify how the Quorum should be selected. Should not count with suspected servers p.e
            if (acceptedReplies.Count < (this.lServersId.Count - this.lServersSuspected.Count) / 2)
            {
                return false;
            }
            
            // TODO - maybe not do it here
            // reviews the suspected servers
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
                //Console.Write(lease.TManagerId + lease.Key + "\n");
            }

            this.leaderId = currentLeaderId;
            return true;
        }

        public bool ConsensusAcceptor(int currentLeaderId)
        {
            // if doesn't receive any accept/prepare from the leader in the beginning of the epoch
            // the it returns false
            Thread.Sleep(500);
            if (isLeaderDead)
            {
                return false;
            }
            return true;
        }
    }
}
