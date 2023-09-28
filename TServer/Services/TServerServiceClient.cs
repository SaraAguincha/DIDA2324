using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TServer.Services
{
    public class TServerServiceClient : ClientTServerService.ClientTServerServiceBase
    {
        private GrpcChannel channel;
        private ClientTServerService.ClientTServerServiceClient client;


        public TServerServiceClient() { }

        // for debug purposes, should be used when a client connects and not a function
        public override Task<StatusReply> Status(
            StatusRequest request, ServerCallContext context)
        {
            Console.WriteLine("Deadline: " + context.Deadline);
            Console.WriteLine("Host: " + context.Host);
            Console.WriteLine("Method: " + context.Method);
            Console.WriteLine("Peer: " + context.Peer);
            return Task.FromResult(Stat(request));
        }

        public StatusReply Stat(StatusRequest request)
        {
            //Thread.Sleep(5001);
            this.channel = GrpcChannel.ForAddress("http://localhost:10000");
            client = new ClientTServerService.ClientTServerServiceClient(channel);

            StatusReply reply = new StatusReply { Status = true };

            return reply;
        }
    }
}
