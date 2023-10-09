using Grpc.Core;
using Protos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Calls the services for the client asyncronously
 
namespace TServer.Services
{
    public class TServerService_Client : ClientTServerService.ClientTServerServiceBase
    {
        private readonly TServerService tServerService;

        public TServerService_Client (TServerService TServerService)
        {
            this.tServerService = TServerService;
        }

        public override Task<StatusReply> Status(StatusRequest request, ServerCallContext context)
        {
            Console.WriteLine("Deadline: " + context.Deadline);
            Console.WriteLine("Host: " + context.Host);
            Console.WriteLine("Method: " + context.Method);
            Console.WriteLine("Peer: " + context.Peer);

            return Task.FromResult(tServerService.State(request));
        }

        public override Task<TxSubmitReply> TxSubmit(TxSubmitRequest request, ServerCallContext context)
        {
            Console.WriteLine("Deadline: " + context.Deadline);
            Console.WriteLine("Host: " + context.Host);
            Console.WriteLine("Method: " + context.Method);
            Console.WriteLine("Peer: " + context.Peer);

            return Task.FromResult(tServerService.Transaction(request));
        }

    }

}

