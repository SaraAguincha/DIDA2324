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

        public override Task<AskLeaseReply> AskLease(AskLeaseRequest request, ServerCallContext context) 
        {
            Console.WriteLine("Method: " + context.Method);

            return Task.FromResult(lServerService.ProcessLeaseRequest(request));
        }
    }
}
