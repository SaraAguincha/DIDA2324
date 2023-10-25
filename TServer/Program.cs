using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Protos;
using System.Text.RegularExpressions;
using TServer.Services;
using Utilities;

// Transaction Server
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
        TServerService tServerService = (TServerService)state;
        tServerService.slotBeginning(epoch);
    }

    public static void Main(string[] args)
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

        // Get the process states from the configuration file
        Dictionary<string, ServerProcessState>[] processStates = config.ProcessStates;

        // Get the slot duration
        int duration = config.Slot.Item2;

        // All the functions of the TServer will be done here
        TServerService tServerService = new TServerService(processId, tServers, lServers, duration, processStates);

        // All of the function call async related to clients, tservers and lservers
        TServerService_Client clientService = new TServerService_Client(tServerService);

        TServerService_LServer lServerService = new TServerService_LServer(tServerService);

        TServerService_TServer tServerSelfService = new TServerService_TServer(tServerService);

        Server server = new Server
        {
            Services = { ClientTServerService.BindService(clientService),
                         TServerLServerService.BindService(lServerService),
                         TServerTServerService.BindService(tServerSelfService) },
            Ports = { serverPort }
        };

        server.Start();

        // Configuring HTTP for client connections in Register method
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        // Timer related activities
        TimerCallback timerCallback = NextEpoch;
        Timer timer = new Timer(timerCallback, tServerService, duration, duration);
        Console.WriteLine("Timer started at " + DateTime.Now);

        Console.WriteLine("Server is running on port: " + port + " and is ready to accept requests...");

        while (true) ;
    }
}