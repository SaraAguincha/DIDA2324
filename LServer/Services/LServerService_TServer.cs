using Grpc.Core;
using Protos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Calls the services for TServers asyncronously

namespace LServer.Services
{
    public class LServerService_TServer: TServerLServerService.TServerLServerServiceBase 
    {
        private readonly LServerService lServerService;

        public LServerService_TServer(LServerService lServerService)
        {
            this.lServerService = lServerService;
        }

        public override Task<NewLeaseReply> RequestNewLease(NewLeaseRequest request, ServerCallContext context) 
        {
            Console.WriteLine("Deadline: " + context.Deadline);
            Console.WriteLine("Host: " + context.Host);
            Console.WriteLine("Method: " + context.Method);
            Console.WriteLine("Peer: " + context.Peer);

            return Task.FromResult(lServerService.NewLease(request));
        }
    }
}
