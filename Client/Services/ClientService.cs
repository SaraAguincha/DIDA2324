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
        private Dictionary<string, ClientTServerService.ClientTServerServiceClient> availableTServers;
        private Dictionary<string, ClientTServerService.ClientTServerServiceClient> tServers;
        private Dictionary<string, ClientLServerService.ClientLServerServiceClient> lServers;
        private ClientTServerService.ClientTServerServiceClient server;

        public ClientService(string ClientId, Dictionary<string, ClientTServerService.ClientTServerServiceClient> TServers,
            Dictionary<string, ClientLServerService.ClientLServerServiceClient> LServers)
        {
            this.clientId = ClientId;
            this.tServers = TServers;
            this.availableTServers = TServers;
            this.lServers = LServers;

           // Initially pick a server whose last digit of the id matches the last digit of the client id
           foreach (KeyValuePair<string, ClientTServerService.ClientTServerServiceClient> tServer in this.tServers)
            {
                // Extract the last two digits of the client id
                string lastTwoDigits = this.clientId.Substring(this.clientId.Length - 2);

                // If the last two digits are 10, connect to the last server in the list
                if (lastTwoDigits == "10")
                {
                    this.server = tServers.ElementAt(tServers.Count - 1).Value;
                    this.currentServerId = tServers.ElementAt(tServers.Count - 1).Key;
                    break;
                }

                // If the last digit of the server id matches the last digit of the client id, pick that server
                if (tServer.Key[tServer.Key.Length - 1] == this.clientId[this.clientId.Length - 1] || 
                    tServer.Key[tServer.Key.Length - 1] == (this.clientId[this.clientId.Length - 1] - 5 ))
                {
                    this.server = tServer.Value;
                    this.currentServerId = tServer.Key;
                    break;
                }
            }


            Console.WriteLine("Client " + clientId + " connected to TServer " + currentServerId);
        }

        // Asynchronous TxSubmit call
        public async Task<RepeatedField<DadInt>?> TxSubmit(List<string> reads, List<DadInt> writes)
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
                // If the current server is unavailable, remove it from the available servers and randomly pick a new one
                this.availableTServers.Remove(currentServerId);
                Console.WriteLine("TxSubmit Error: The current server " + currentServerId + " is unavailable.");

                if (availableTServers.Count == 0)
                {
                    Console.WriteLine("TxSubmit Error: No available servers.");
                }
                else
                {
                    Random random = new Random();
                    int index = random.Next(availableTServers.Count);
                    this.server = availableTServers.ElementAt(index).Value;
                    this.currentServerId = availableTServers.ElementAt(index).Key;
                    Console.WriteLine("New connection made to: " + this.currentServerId + ".");
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
                    this.availableTServers.Remove(tServer.Key);
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
                }
            }

            return true;
        }
    }
}