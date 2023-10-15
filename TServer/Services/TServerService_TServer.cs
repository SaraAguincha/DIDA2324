using Grpc.Core;
using Protos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TServer.Services
{
    public class TServerService_TServer: TServerTServerService.TServerTServerServiceBase
    {
        private readonly TServerService tServerService;

        public TServerService_TServer (TServerService tServerService)
        {
            this.tServerService = tServerService;
        }

        public override Task<ReleaseLeaseReply> ReleaseLease(ReleaseLeaseRequest request, ServerCallContext context)
        {
            Console.WriteLine("-----------------------");
            Console.WriteLine("Host: " + context.Host);
            Console.WriteLine("Method: " + context.Method);
            Console.WriteLine("Peer: " + context.Peer);
            Console.WriteLine("-----------------------");

            return Task.FromResult(tServerService.ReleaseLease(request));
        }
    }
}
