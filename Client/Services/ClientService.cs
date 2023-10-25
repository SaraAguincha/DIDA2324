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
        private string currentServerId;
        private List<string> unavailableServers = new List<string>();
        private Dictionary<string, ClientTServerService.ClientTServerServiceClient> tServers;
        private Dictionary<string, ClientLServerService.ClientLServerServiceClient> lServers;
        private ClientTServerService.ClientTServerServiceClient server;

        public ClientService(string ClientId, Dictionary<string, ClientTServerService.ClientTServerServiceClient> TServers,
            Dictionary<string, ClientLServerService.ClientLServerServiceClient> LServers)
        {
            this.clientId = ClientId;
            this.tServers = TServers;
            this.lServers = LServers;

            // Initialy pick a random TServer to send the request (DEFAULT)
            Random random = new Random();
            int index = random.Next(TServers.Count);
            this.server = TServers.ElementAt(index).Value;
            this.currentServerId = TServers.ElementAt(index).Key;

            Console.WriteLine("Client " + clientId + " connected to TServer " + currentServerId);

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
            catch (RpcException)
            {
                unavailableServers.Add(currentServerId);
                Console.WriteLine("TxSubmit Error: The current " + currentServerId + " is unavailable");
                
                foreach (string tMServer in  tServers.Keys)
                {
                    if (!unavailableServers.Contains(tMServer))
                    {
                        this.server = tServers[tMServer];
                        this.currentServerId = tMServer;
                        Console.WriteLine("New connection made to: " + tMServer + ". \nPlease retry again now.");
                        break;
                    }
                }

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
                catch (RpcException)
                {
                    Console.WriteLine("Server " + tServer.Key + " is unavailable.");
                    unavailableServers.Add(tServer.Key);
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
                catch (RpcException)
                {
                    Console.WriteLine("Server " + lServer.Key + " is unavailable.");
                    return false;
                }
            }

            return true;
        }
    }
}