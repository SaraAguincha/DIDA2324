using Grpc.Net.Client;
using Protos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LServer.Services
{
    public class LServerService
    {
        // Server attributes
        private string lManagerID;
        private int processId;      // needed in order to select the leader


        private Dictionary<string, GrpcChannel> channels = new Dictionary<string, GrpcChannel>();
        private Dictionary<string, PaxosService.PaxosServiceClient> lServerInstances = new Dictionary<string, PaxosService.PaxosServiceClient>();
        private Dictionary<string, string> LServers;


        // Lease request queue for this Lease Manager
        Queue<GrantLeaseRequest> leaseRequestQueue = new Queue<GrantLeaseRequest>();
        public LServerService(string lManagerID, Dictionary<string, string> lServers) 
        {
            this.lManagerID = lManagerID;
            this.LServers = lServers;

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

        /*  Paxos: Prepare
         *  Sends a prepare request to all the other servers
         */
        public PromiseReply Prepare(PrepareRequest request)
        {
            List<PromiseReply> promisesReplies = new List<PromiseReply>();
            List<Task> tasks = new List<Task>();
            // for each LServer starts a task with a prepare request
            foreach (KeyValuePair<string, PaxosService.PaxosServiceClient> lServerInstances in this.lServerInstances)
            {
                // for each entry in LServers, run a task with the request for a lease
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
