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


        // Lease request queue for this Lease Manager
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
                Lease lease = new Lease();
                lease.TManagerId = request.TManagerId;
                lease.Key = key;
                leaseReply.Leases.Add(lease);
            }

            // TODO - this is what should be done after paxos is executed
            //leaseRequestQueue = new Queue<GrantLeaseRequest>();

            return leaseReply;
        }

        /*
         * DoPaxos: Executes Paxos
         * Checks which process should be leader.
         * Then if it's the leader, sends a PrepareRequest to all the other servers notifying
         * If it's not the leader it will wait for a PrepareRequest.
         *      - if it receives, accepts leader
         *      - if not repeats the Function, because leader must have crashed
         */

        // Clunky, and it takes too much time because of thread sleeping. Should be possible to not use that.
        // TODO - implement the prepare and accept functions as seperate functions, make it more readable
        public bool DoPaxos()
        {
            int currentLeaderId;
            List<Grpc.Core.AsyncUnaryCall<PromiseReply>> promisesReplies = new List<Grpc.Core.AsyncUnaryCall<PromiseReply>>();
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
            {
                // prepares the PrepareRequest
                // TODO - use the right epoch and the right roundId
                PrepareRequest prepareRequest = new PrepareRequest { Epoch = 0, ProposerId = this.serverId, RoundId = 0 };

                foreach (KeyValuePair<string, PaxosService.PaxosServiceClient> lServerInstances in this.lServerInstances)
                {
                    // for each entry in LServers, run a task with the request for a promise
                    Task t = Task.Run(() =>
                    {
                        Grpc.Core.AsyncUnaryCall<PromiseReply> promiseReply = lServerInstances.Value.PrepareAsync(prepareRequest);
                        promisesReplies.Add(promiseReply);
                        return Task.CompletedTask;
                    });
                    tasks.Add(t);
                }

                // waits some time for responses
                System.Threading.Thread.Sleep(100);

                // If the promise replies are not the majority, return false
                // TODO - review the way the majority is calculated
                if (promisesReplies.Count < lServersId.Count / 2)
                {
                    Console.WriteLine("number of replies: " + promisesReplies.Count);
                    Console.WriteLine("I didn't have the majority :(");
                    return false;
                }

                Console.WriteLine("I am the leader and I concluded the prepare:" + serverId);
                // TODO - accept/propose phase

                return true;
            }

            // awaits for the leader to make a prepareRequest, and sends a promiseReply
            else
            {
                Task waitTask = Task.Run(async () =>
                {
                    // TODO - based on the id of the leader, get his id, currently hardcoded
                    // Receive a prepareRequest from the leader
                    PromiseReply promiseRequest = await this.lServerInstances["LM1"].PrepareAsync(new PrepareRequest());

                });

                // waits some time for a request
                System.Threading.Thread.Sleep(2000);
                if (waitTask.IsCompleted)
                {
                    Console.WriteLine("Current leader is: " + currentLeaderId + "and I am server:" + serverId);
                    return true;
                }

                // TODO - if false, repeat in order to select a new leader
                Console.WriteLine("Current leader is: " + currentLeaderId + "and I am server:" + serverId);
                Console.WriteLine("I had no response :(");
                return false;
            }
        }

        public PromiseReply PaxosPrepare(PrepareRequest request)
        {
            List<PromiseReply> promisesReplies = new List<PromiseReply>();
            List<Task> tasks = new List<Task>();
            // for each LServer starts a task with a prepare request
            foreach (KeyValuePair<string, PaxosService.PaxosServiceClient> lServerInstances in this.lServerInstances)
            {
                // for each entry in LServers, run a task with the request for a promise
                Task t = Task.Run(() =>
                {
                    lServerInstances.Value.PrepareAsync(request);
                });
                tasks.Add(t);
            }

            PromiseReply reply = new PromiseReply();
            return reply;
        }

        

    }
}
