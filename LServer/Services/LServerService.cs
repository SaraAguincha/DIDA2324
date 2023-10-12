using Grpc.Core;
using Grpc.Net.Client;
using Protos;
using System;
using System.Collections.Generic;
using System.Linq;
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

        private List<int> lServersId = new List<int> {1,2,3};

        private Dictionary<string, GrpcChannel> channels = new Dictionary<string, GrpcChannel>();
        private Dictionary<string, PaxosService.PaxosServiceClient> lServerInstances = new Dictionary<string, PaxosService.PaxosServiceClient>();
        private Dictionary<string, TServerLServerService.TServerLServerServiceClient> tServerInstances = new Dictionary<string, TServerLServerService.TServerLServerServiceClient>();
        private Dictionary<string, string> LServers;
        private Dictionary<string, string> TServers;


        // Paxos related atributes
        int epoch = 0;          // should be ++ in the start of consensus perhaps?
        int highestRoundId = 0;

        Queue<AskLeaseRequest> leaseRequestQueue = new Queue<AskLeaseRequest>();
        Queue<Lease> leaseQueue = new Queue<Lease>();
        List<string> pendingTManagers = new List<string>();

        public LServerService(string lManagerID, int serverId, Dictionary<string, string> lServers, Dictionary<string, string> tServers) 
        {
            this.lManagerID = lManagerID;
            this.LServers = lServers;
            this.TServers = tServers;
            this.serverId = serverId;
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
                // adds the lease request to the Queue
                leaseQueue.Enqueue(lease);
                pendingTManagers.Add(request.TManagerId);
            }

            // Replies with an ack 
            AskLeaseReply leaseReply = new AskLeaseReply { Ack = true };
            Console.WriteLine("Response sent: " + leaseReply.Ack);

            return leaseReply;
        }

        /* TODO - complete function
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
            return reply;
        }

        public AcceptedReply PaxosAccept(AcceptRequest request) 
        {
            Console.WriteLine("Entered Paxos Accept! (Step 2)");

            AcceptedReply reply = new AcceptedReply {  Epoch = -1 };

            if (request.RoundId >= this.highestRoundId)
            {
                reply = new AcceptedReply { Epoch = request.Epoch, RoundId = request.RoundId };
                foreach (var lease in request.Queue) { reply.Queue.Add(lease); }
            }
            else
                Console.WriteLine("Oops, roundID too low in accept phase.");
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

        // Clunky, and it takes too much time because of thread sleeping. Should be possible to not use that.
        // TODO - implement the prepare and accept functions as seperate functions, make it more readable
        public void Consensus(int epoch)
        {
            int currentLeaderId;
            
            // what server should be leader

            // in case it's the first epoch, leaderId will always be lower than the first server id
            if (this.leaderId == 0)
            {
                // TODO - should search for the first element that is not suspected
                currentLeaderId = this.lServersId[0];
            }
            
            // assumes the leader is the same as the previous epoch (only changes when its crashed)
            // TODO - if the previous is suspected sends to the next lower one
            else 
                currentLeaderId= this.leaderId;

            // if the currentLeaderId is this server, waits for promiseReplies from the majority of lservers
            if (this.serverId == currentLeaderId)
            {
                // TODO - maybe run BroadcastLeases asynchronously (we might want to know the result of the function, though)
                BroadcastLeases();
                ConsensusLeader(currentLeaderId, epoch);
            }


            // awaits for the leader to make a prepareRequest, and sends a promiseReply
            else
                ConsensusAcceptor(currentLeaderId);
        }

        public bool ConsensusLeader(int currentLeaderId, int epoch) 
        {
            List<Task<PromiseReply>> promisesReplies = new List<Task<PromiseReply>>();
            List<Task<AcceptedReply>> acceptedReplies = new List<Task<AcceptedReply>>();
            //List<Task> tasks = new List<Task>();

            // Prepare Phase (Step 1)
            // should not be necessary to do a prepare phase in every epoch
            int currentRoundId = this.highestRoundId + 1;
            PrepareRequest prepareRequest = new PrepareRequest { Epoch = epoch, ProposerId = this.serverId, RoundId = currentRoundId };

            foreach (KeyValuePair<string, PaxosService.PaxosServiceClient> lServerInstances in this.lServerInstances)
            {
                // for each entry in LServers, run a task with the request for a promise
                AsyncUnaryCall<PromiseReply> promiseReply = lServerInstances.Value.PrepareAsync(prepareRequest);
                promisesReplies.Add(promiseReply.ResponseAsync);
            }

            // waits some time for responses
            Task.WaitAll(promisesReplies.ToArray(), 500);

            // TODO - If the promise replies are not the majority, return false
            // (Right now we're considering that we ALWAYS receive a positive reply from ALL other processes)

            Console.WriteLine("I am the leader: " + serverId + ", and ran the prepare phase.");
            // TODO - accept/propose phase

            // Accept Phase (Step 2)
            // TODO - Only send accept after receiving a promise majority
            AcceptRequest acceptRequest = new AcceptRequest { Epoch = epoch, RoundId = currentRoundId };
            foreach (var lease in leaseQueue) { acceptRequest.Queue.Add(lease); }

            foreach (KeyValuePair<string, PaxosService.PaxosServiceClient> lServerInstances in this.lServerInstances)
            {
                AsyncUnaryCall<AcceptedReply> acceptedReply = lServerInstances.Value.AcceptAsync(acceptRequest);
                acceptedReplies.Add(acceptedReply.ResponseAsync);
            }

            Task.WaitAll(acceptedReplies.ToArray(), 500);

            // TODO - If the accepted replies are not the majority, return false
            // (Right now we're considering that we ALWAYS receive a positive reply from ALL other processes)

            Console.WriteLine("Here is the consensual queue: ");
            foreach (var lease in acceptRequest.Queue)
            {
                Console.Write(lease.TManagerId + lease.Key + "\n");
            }

            // TODO - If a majority is received, send consensual queue to TManagers

            // TODO - Only reset queue if consensus succeded
            //leaseQueue = new Queue<Lease>();
            highestRoundId = currentRoundId;
            return true;
        }

        public bool ConsensusAcceptor(int currentLeaderId)
        {
            // TODO - Something?
            return false;
        }
    }
}
