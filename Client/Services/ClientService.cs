using Google.Protobuf.Collections;
using Grpc.Core;
using Grpc.Net.Client;
using Protos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Client.Services
{
    public class ClientService
    {
        private string clientId;
        private Dictionary<string, ClientTServerService.ClientTServerServiceClient> tServers;
        private Dictionary<string, ClientLServerService.ClientLServerServiceClient> lServers;
        private ClientTServerService.ClientTServerServiceClient server;

        public ClientService(string ClientId, Dictionary<string, ClientTServerService.ClientTServerServiceClient> TServers,
            Dictionary<string, ClientLServerService.ClientLServerServiceClient> LServers)
        {
            this.clientId = ClientId;
            this.tServers = TServers;
            this.lServers = LServers;

            // Pick a random TServer to send the request (DEFAULT)
            Random random = new Random();
            int index = random.Next(TServers.Count);
            this.server = TServers.ElementAt(index).Value;
            Console.WriteLine("Client " + clientId + " connected to TServer " + TServers.ElementAt(index).Key);

            // (FOR TESTING PURPOSES) THIS PICKS THE FIRST TSERVER, UNCOMMENT/COMMENT THE ABOVE LINES AND THIS ONE TO CHANGE THIS
            //this.server = TServers.ElementAt(0).Value;
            //Console.WriteLine("Client " + clientId + " connected to TServer " + TServers.ElementAt(0).Key);

        }

        // Asynchronous TxSubmit call
        public async Task<RepeatedField<DadInt>> TxSubmit(List<string> reads, List<DadInt> writes)
        {
            TxSubmitRequest request = new TxSubmitRequest { ClientId = this.clientId, Key = { reads }, DadInts = { writes } };

            // Perform a try catch to submit the transaction and catch exceptions
            try
            {
                TxSubmitReply reply = await server.TxSubmitAsync(request);
                return reply.DadInts;
            }
            catch (Grpc.Core.RpcException e)
            {
                Console.WriteLine("TxSubmit Error: " + e.Message);
                return null;
            }
        }

        // Asynchronous Status call
        public async Task<bool> Status()
        {
            // Send a tstatus request to all TServers and an lstatus request to all LServers
            TStatusRequest tRequest = new TStatusRequest { Ok = true };
            foreach (KeyValuePair<string, ClientTServerService.ClientTServerServiceClient> tServer in this.tServers)
            {
                try
                {   
                    TStatusReply tReply = await tServer.Value.TStatusAsync(tRequest);
                }
                catch (Grpc.Core.RpcException e)
                {
                    Console.WriteLine("Server " + tServer.Key + " is unavailable.");
                    return false;
                }
            }

            LStatusRequest lRequest = new LStatusRequest { Ok = true };
            foreach (KeyValuePair<string, ClientLServerService.ClientLServerServiceClient> lServer in this.lServers)
            {
                try
                {
                    LStatusReply lReply = await lServer.Value.LStatusAsync(lRequest);
                }
                catch (Grpc.Core.RpcException e)
                {
                    Console.WriteLine("Server " + lServer.Key + " is unavailable.");
                    return false;
                }
            }

            return true;
        }
    }
}