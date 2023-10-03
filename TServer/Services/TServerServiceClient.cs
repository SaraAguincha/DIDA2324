using Google.Protobuf.Collections;
using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Client related. Transactions and Status commands
namespace TServer.Services
{
    public class TServerServiceClient : ClientTServerService.ClientTServerServiceBase
    {
        // TODO - store the clients connected to the server
        // ((List)) of client id, channel and the service
        private ClientTServerService.ClientTServerServiceClient client;
        private GrpcChannel channel;

        // Server attributes
        private string TManagerId;
        private Dictionary<string,string> LServers;
        private Dictionary<string, string> TServers;


        // set all the server information from config
        public TServerServiceClient(string TManagerId, Dictionary<string, string> TServers, Dictionary<string,string> LServers) 
        {
            this.TManagerId = TManagerId;
            this.TServers = TServers;
            this.LServers = LServers;
        }


        /* Transaction submitted by client
         *      - clientID
         *      - DadInts to read
         *      - DadInts to write
         *      
         *  For the TManager to reply, it needs a lease from the LManager
         *  After receiving permission it will submit the transaction, report to the other Tmanagers,
         *  store the DadInt value and release the lease
         */
        public TxSubmitReply Transaction(TxSubmitRequest request)
        {
            // TODO - a way to not repeat this verification every time in every command for every client,
            // right now it only works for one client.. Choose one way to store the clients/channels
            /* is this necessary ??
            if (client == null)
            {
                this.channel = GrpcChannel.ForAddress("http://localhost:10000");
                this.client = new ClientTServerService.ClientTServerServiceClient(channel);
            }*/

            // prepares the request for the Lease Manager
            RepeatedField<string> reads = request.Key;
            RepeatedField<DadInt> writes = request.DadInts;

            // makes a list of all the keys needed in the lease
            List<string> leaseKeys = new List<string>();

            foreach (string key in reads)
            {
                if (!leaseKeys.Contains(key))
                    leaseKeys.Add(key);
            }

            foreach (DadInt dadInt in writes)
            {
                if(!leaseKeys.Contains(dadInt.Key))
                    leaseKeys.Add(dadInt.Key);
            }

            // Request a lease from all of the lease managers
            // TODO


            // currently responds with the dadints from writes
            TxSubmitReply reply = new TxSubmitReply { DadInts = { writes } };
            return reply;
        }


        public StatusReply State(StatusRequest request)
        {
            /* TODO - is this necessary ??
            if (client == null) {
                this.channel = GrpcChannel.ForAddress("http://localhost:10000");
                this.client = new ClientTServerService.ClientTServerServiceClient(channel);
            }*/

            // TODO - use a broadcast algorithm to contact the other servers (2PC p.e)
            StatusReply reply = new StatusReply { Status = true };

            return reply;
        }

        // ----------------------------------------------------------
        // TODO - separate into a different file for easier reading
        // calls the proto functions asyncronously
        public override Task<StatusReply> Status(StatusRequest request, ServerCallContext context)
        {
            Console.WriteLine("Deadline: " + context.Deadline);
            Console.WriteLine("Host: " + context.Host);
            Console.WriteLine("Method: " + context.Method);
            Console.WriteLine("Peer: " + context.Peer);

            return Task.FromResult(State(request));
        }

        public override Task<TxSubmitReply> TxSubmit(TxSubmitRequest request, ServerCallContext context)
        {
            Console.WriteLine("Deadline: " + context.Deadline);
            Console.WriteLine("Host: " + context.Host);
            Console.WriteLine("Method: " + context.Method);
            Console.WriteLine("Peer: " + context.Peer);

            return Task.FromResult(Transaction(request));
        }

    }
}
