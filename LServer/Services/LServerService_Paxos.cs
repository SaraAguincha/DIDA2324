using Grpc.Core;
using Protos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LServer.Services
{
    internal class LServerService_Paxos: PaxosService.PaxosServiceBase
    {
        private readonly LServerService lServerService;
        public LServerService_Paxos(LServerService lServerService) 
        {
            this.lServerService = lServerService;
        }

        public override Task<PromiseReply> Prepare(PrepareRequest request, ServerCallContext context)
        {
            Console.WriteLine("-----------------------");
            Console.WriteLine("Host: " + context.Host);
            Console.WriteLine("Method: " + context.Method);
            Console.WriteLine("Peer: " + context.Peer);
            Console.WriteLine("-----------------------");

            return Task.FromResult(lServerService.PaxosPrepare(request));
        }

        public override Task<AcceptedReply> Accept(AcceptRequest request, ServerCallContext context)
        {
            Console.WriteLine("-----------------------");
            Console.WriteLine("Host: " + context.Host);
            Console.WriteLine("Method: " + context.Method);
            Console.WriteLine("Peer: " + context.Peer);
            Console.WriteLine("-----------------------");

            return Task.FromResult(lServerService.PaxosAccept(request));
        }
    }
}
