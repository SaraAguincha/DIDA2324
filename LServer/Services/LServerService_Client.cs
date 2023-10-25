using Grpc.Core;
using Protos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Calls the services for the client asyncronously

namespace LServer.Services
{
    public class LServerService_Client : ClientLServerService.ClientLServerServiceBase
    {
        private readonly LServerService lServerService;

        public LServerService_Client (LServerService lServerService)
        {
            this.lServerService = lServerService;
        }

        public override Task<LStatusReply> LStatus(LStatusRequest request, ServerCallContext context)
        {
            Console.WriteLine("-----------------------");
            Console.WriteLine("Host: " + context.Host);
            Console.WriteLine("Method: " + context.Method);
            Console.WriteLine("Peer: " + context.Peer);
            Console.WriteLine("-----------------------");

            return Task.FromResult(lServerService.State(request));
        }
    }
}
