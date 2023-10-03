using Google.Protobuf.Collections;
using Grpc.Core;
using Grpc.Net.Client;
using Protos;
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
        private string hostname;
        private string clientId;
        public Server server;

        // TODO - add and set the other necessary information, clientId, TServers
        public ClientService(string clientId, string serverHostname, string clientHostname)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            // setup the client side
            this.hostname = clientHostname;
            this.clientId = clientId;
            channel = GrpcChannel.ForAddress(serverHostname);
            client = new ClientTServerService.ClientTServerServiceClient(channel);
        }

        // Client Commands
        // Asynchronous TxSubmit call
        public async Task<RepeatedField<DadInt>> TxSubmit(List<string> reads, List<DadInt> writes)
        {
            TxSubmitRequest request = new TxSubmitRequest { ClientId = this.clientId, Key = { reads }, DadInts = { writes } };
            TxSubmitReply reply = await client.TxSubmitAsync(request);

            return reply.DadInts;
        }


        // TODO - change for async, catch exceptions
        public bool Status()
        {
            StatusRequest request = new StatusRequest { Ok = true };
            StatusReply reply = client.Status(request);

            return reply.Status;
        }

        public void ServerShutdown()
        {
            server.ShutdownAsync().Wait();
        }
    }
}