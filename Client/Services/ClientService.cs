using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Services
{
    public class ClientService : ClientTServerService.ClientTServerServiceBase
    {
        private readonly GrpcChannel channel;
        private readonly ClientTServerService.ClientTServerServiceClient client;
        private Server server;
        private string hostname;

        public ClientService(string serverHostname, string clientHostname)
        {
            this.hostname = clientHostname;
            // setup the client side

            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            channel = GrpcChannel.ForAddress(serverHostname);
            client = new ClientTServerService.ClientTServerServiceClient(channel);
        }

        // connection to the corresponding TServer
        // TODO ((better way to do it))
        public void Start(string serverHostname, string clientHostname)
        {
            server = new Server
            {
                Services = { ClientTServerService.BindService(new ClientService(serverHostname, clientHostname)) },
                Ports = { new ServerPort("localhost", 10000, ServerCredentials.Insecure) }

            };
            server.Start();
        }

        // Client Commands
        public bool Status()
        {
            StatusRequest request = new StatusRequest { Ok = true };
            StatusReply reply = client.Status(new StatusRequest(request));

            return reply.Status;
        }

        public void ServerShutdown()
        {
            server.ShutdownAsync().Wait();
        }
    }
}