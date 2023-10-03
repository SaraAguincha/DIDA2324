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

        // Lease request queue for this Lease Manager
        Queue<GrantLeaseRequest> leaseRequestQueue = new Queue<GrantLeaseRequest>();
        public LServerService(string lManagerID) 
        {
            this.lManagerID = lManagerID;
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

    }
}
