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
        private Dictionary<string, string> LServers;

        // Paxos related atributes
        int readTimestamp = 0;
        int writeTimestamp = 0;
        Queue<GrantLeaseRequest> leaseRequestQueue = new Queue<GrantLeaseRequest>();

        public LServerService(string lManagerID, int serverId, Dictionary<string, string> lServers) 
        {
            this.lManagerID = lManagerID;
            this.LServers = lServers;
            this.serverId = serverId;
            this.leaderId = 0;          // LeaderId is always > 0 ((for now))
            

            // populate all of lservers connections
            foreach (KeyValuePair<string, string> lserver in this.LServers)
            {
                Console.WriteLine(lserver.Value);
                GrpcChannel channel = GrpcChannel.ForAddress(lserver.Value);
                channels.Add(lserver.Key, channel);
                lServerInstances.Add(lserver.Key, new PaxosService.PaxosServiceClient(channel));
            }
        }

        public GrantLeaseReply GrantLease(GrantLeaseRequest request)
        {
            // Adds lease request to the queue (will be sent to paxos)
            //leaseRequestQueue.Enqueue(request);

            GrantLeaseReply leaseReply = new GrantLeaseReply();
            leaseReply.Epoch = 0; // TODO - Insert right epoch

            // TODO - For now, lease managers simply reply with whatever the GrantLeaseRequest asked (paxos missing - needed to establish an order)
            foreach (string key in request.Key)
            {
                Lease lease = new Lease
                {
                    TManagerId = request.TManagerId,
                    Key = { key }
                };
                // TODO - IMPORTANT - For now, the lease requests are being redirected to the TServer directly.
                // This function should wait until the CURRENT paxos round is being finished, after the CURRENT round ends,
                // it will grant the leases SEQUENTIALY to each of the TManager that has requested it (abiding the consensual order)
                leaseReply.Leases.Add(lease);
            }

            // TODO - this is what should be done after paxos is executed
            //leaseRequestQueue = new Queue<GrantLeaseRequest>();

            return leaseReply;
        }

        public PromiseReply PaxosPrepare(PrepareRequest request)
        {
            Console.WriteLine("Entered Paxos Prepare!");
            List<PromiseReply> promisesReplies = new List<PromiseReply>();
            List<Task> tasks = new List<Task>();
            
            PromiseReply reply = new PromiseReply { Epoch = -1};
            // TODO - Change epoch to the right one
            if (request.RoundId > readTimestamp && request.RoundId > writeTimestamp)
            {
                reply = new PromiseReply { Epoch = 0, ReadTimestamp = this.readTimestamp };
                foreach (var leaseRequest in leaseRequestQueue)
                {
                    Lease paxosLease = new Lease { TManagerId = leaseRequest.TManagerId };
                    foreach (var key in leaseRequest.Key)
                    {
                        paxosLease.Key.Add(key);
                    }
                    reply.Queue.Add(paxosLease);
                }
            }
            else
                Console.WriteLine("Oops, roundID too low.");
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
        public void Consensus()
        {
            int currentLeaderId;
            List<Task<PromiseReply>> promisesReplies = new List<Task<PromiseReply>>();
            List<Task> tasks = new List<Task>();
            
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
                ConsensusLeader(promisesReplies, tasks, currentLeaderId);
            // awaits for the leader to make a prepareRequest, and sends a promiseReply
            else
                ConsensusAcceptor(promisesReplies, tasks, currentLeaderId);
        }

        public bool ConsensusLeader(
            List<Task<PromiseReply>> promisesReplies,
            List<Task> tasks,
            int currentLeaderId) 
        {
            // prepares the PrepareRequest
            // TODO - use the right epoch and the right roundId
            PrepareRequest prepareRequest = new PrepareRequest { Epoch = 0, ProposerId = this.serverId, RoundId = 3 };

            foreach (KeyValuePair<string, PaxosService.PaxosServiceClient> lServerInstances in this.lServerInstances)
            {
                // for each entry in LServers, run a task with the request for a promise
                Grpc.Core.AsyncUnaryCall<PromiseReply> promiseReply = lServerInstances.Value.PrepareAsync(prepareRequest);
                promisesReplies.Add(promiseReply.ResponseAsync);
            }

            // waits some time for responses
            Task.WaitAll(promisesReplies.ToArray(), 500);

            foreach(var task in promisesReplies)
            {
                Console.WriteLine("HERE IS THE TASK RESULT: " + task?.Result?.Epoch);
            }

            // End of debug

            // If the promise replies are not the majority, return false
            // TODO - review the way the majority is calculated

            Console.WriteLine("I am the leader:" + serverId + ", and ran the prepare phase.");
            // TODO - accept/propose phase

            return true;
        }

        public bool ConsensusAcceptor(
            List<Task<PromiseReply>> promisesReplies,
            List<Task> tasks,
            int currentLeaderId)
        {

            // TODO - if false, repeat in order to select a new leader
            Console.WriteLine("Current leader is:" + currentLeaderId + " and I am server:" + serverId);
            //Console.WriteLine("I had no response :(");
            return false;
        }
    }
}
