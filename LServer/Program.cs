using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using LServer.Services;
using Protos;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Utilities;

// Lease Server
class Program
{
    // Current epoch (has to be global variable)
    private static int epoch = 0;

    // Function to execute at the start of each epoch
    // Each epoch executes each DELTA miliseconds
    static void NextEpoch(object state)
    {
        epoch++;
        Console.WriteLine("Advanced to epoch number " + epoch.ToString());
        LServerService lServerService = (LServerService)state;
        lServerService.Consensus(epoch);
    }
    public static async Task Main(string[] args)
    {
        // Sleep until the specified time on args[3]
        TimeSpan waitToStart = DateTime.Parse(args[3]).Subtract(DateTime.Now);
        Console.WriteLine("Waiting for " + waitToStart.TotalMilliseconds + " milliseconds.");
        System.Threading.Thread.Sleep(waitToStart);

        // Server configuration
        string processId = args[0];
        string hostname = args[1];
        int port = Int32.Parse(args[2]);

        ServerPort serverPort;
        serverPort = new ServerPort(hostname, port, ServerCredentials.Insecure);
        
        // Receive info from the configuration file
        ServersConfig config = Resources.ParseConfigFile();

        // Get the list of TServer and LServer processes and add them to a dictionary
        Dictionary<string, string> tServers = new Dictionary<string, string>();
        Dictionary<string, string> lServers = new Dictionary<string, string>();

        foreach (ServerProcessInfo tServer in config.TServers)
        {
            tServers.Add(tServer.Id, tServer.Url);
        }
        foreach (ServerProcessInfo lServer in config.LServers)
        {
            lServers.Add(lServer.Id, lServer.Url);
        }

        // Get process id in int format by matching the processId string to the id on the list of lServers
        int serverId = config.LServers.FindIndex(x => x.Id == processId) + 1;

        // Get all process ids in int format by matching the processId string to the id on the list of tServers and add to a list
        List<int> lServerIds = new List<int>();
        foreach (string lServerId in lServers.Keys)
        {
            lServerIds.Add(config.LServers.FindIndex(x => x.Id == lServerId) + 1);
        }

        // Get the process states from the configuration file
        Dictionary<string, ServerProcessState>[] processStates = config.ProcessStates;

        // Get the slot duration
        int duration = config.Slot.Item2;

        // All the functions of the LServer will be done here
        lServers.Remove(processId);
        LServerService lServerService = new LServerService(processId, serverId, lServers, tServers, lServerIds, duration, processStates);

        // All of the function call async related to clients, tservers and lservers
        LServerService_TServer tServerService = new LServerService_TServer(lServerService);

        LServerService_Paxos paxosService = new LServerService_Paxos(lServerService);

        LServerService_Client clientService = new LServerService_Client(lServerService);

        // Bind all the services:
        // Client Services          (Client Commands)   -> currently the only one
        // TManagerServer Services  (Info disclosure)
        // LManagerServer Service   (Leases requests)
        Server server = new Server
        {
            Services = { TServerLServerService.BindService(tServerService),
                         PaxosService.BindService(paxosService),
                         ClientLServerService.BindService(clientService) },
            Ports = { serverPort }
        };

        server.Start();

        //Configuring HTTP for client connections in Register method
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        // Timer related activities
        TimerCallback timerCallback = NextEpoch;
        Timer timer = new Timer(timerCallback, lServerService, duration, duration);
        Console.WriteLine("Timer started at " + DateTime.Now);

        Console.WriteLine("Server is running on port: " + port + " and is ready to accept requests...");

        while (true);
    }
}