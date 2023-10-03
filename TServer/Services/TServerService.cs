﻿using Google.Protobuf.Collections;
using Grpc.Core;
using Grpc.Net.Client;
using Protos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// All of the services that the Tserver does
namespace TServer.Services
{
    public class TServerService
    {
        // TODO - store the clients connected to the server
        // ((List)) of client id, channel and the service
        private ClientTServerService.ClientTServerServiceClient client;
        private GrpcChannel channel;

        // Server attributes
        private string TManagerId;
        private Dictionary<string, string> LServers;
        private Dictionary<string, string> TServers;
        //private Dictionary<string, int> dadInt;


        // set all the server information from config
        public TServerService(string TManagerId, Dictionary<string, string> TServers, Dictionary<string, string> LServers)
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
                if (!leaseKeys.Contains(dadInt.Key))
                    leaseKeys.Add(dadInt.Key);
            }

            // TODO - Request a lease from all of the lease managers
            // not yet implemented
            //LeaseRequest leaseRequest = new LeaseRequest { TManagerId = this.TManagerId, Key = { leaseKeys }};
            
            // TODO
            //LeaseReply leaseReply = RequestLease(leaseRequest);

            // TODO - Lease verification

            // currently responds with the dadints from writes
            TxSubmitReply reply = new TxSubmitReply { DadInts = { writes } };

            // TODO before replying broadcast to the other TServers and update the DadInts values

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

        public LeaseReply RequestLease(LeaseRequest request)
        {
            // requests a lease from all of the LServers
            List<LeaseReply> leaseReplies = new List<LeaseReply>();
            List<Task> tasks = new List<Task>();
            // for each LServer starts a task with a request
            foreach (var lserver in this.LServers)
            {
                // for each entry in LServers, run a task with the request for a lease
                Task t = Task.Run(() =>
                {

                });
                tasks.Add(t);
            }
            // wait for the majority of the tasks to get a response
            // sort what it receives and return one reply to the function Transaction

            LeaseReply reply = new LeaseReply();

            return reply;
        }

    }
}
