using Grpc.Core;
using Grpc.Net.Client;
using LServer.Services;
using Protos;
using System.Text.RegularExpressions;

// Lease Server Main
    class Program
{
    // Current epoch (has to be global variable)
    private static int epoch = 0;
    private const int DELTA = 10000;
    static void NextEpoch(object state)
    {
        epoch++;
        Console.WriteLine("Advanced to epoch number " + epoch.ToString());
    }
    public static void Main(string[] args)
    {
        // parsing according to config file
        // a TServer will be given the following information:
        // "LM2 L "http://localhost:10200"
        // additional information is needed:
        //  - number of processes running and respective ids and URL

        /*
       string arguments = Console.ReadLine();          //unnecessary when script implemented
       string[] initialArgs = arguments.Split(" ");    //unnecessary when script implemented


       string LManagerId = initialArgs[0];             //args[0] when script implemented

       string urlPattern = @"http://([^:/]+):(\d+)";
       Match match = Regex.Match(initialArgs[1], urlPattern);

       string hostname = match.Groups[1].Value;            // group 1 will contain the IP address
       int port = Int32.Parse(match.Groups[2].Value);      // group 2 will contain the port

       // after the server information, it receives the other processes information
       // TODO - TManagers List and LManagers List


       ServerPort serverPort;
       serverPort = new ServerPort(hostname, port, ServerCredentials.Insecure);
        */


        // placeholder information  ----------------------------------------------
        //string hostname = "localhost";
        //int port = 10200;
        //string lManagerId = "LM1";
        Dictionary<string, string> lServers = new Dictionary<string, string>();
        // TODO - Hardcoded
        lServers.Add("LM1", "http://localhost:20001");
        lServers.Add("LM2", "http://localhost:20002");
        lServers.Add("LM3", "http://localhost:20003");

        // ------------------------------------------------------------------------

        // Server configuration
        string processId = args[0];
        string hostname = args[1];
        int port = Int32.Parse(args[2]);

        ServerPort serverPort;
        serverPort = new ServerPort(hostname, port, ServerCredentials.Insecure);

        // all the functions of the LServer will be done here
        LServerService lServerService = new LServerService(processId, lServers);

        // all of the function call async related to clients, tservers and lservers
        LServerService_TServer tServerService = new LServerService_TServer(lServerService);

        // Bind all the services:
        // Client Services          (Client Commands)   -> currently the only one
        // TManagerServer Services  (Info disclosure)
        // LManagerServer Service   (Leases requests)
        Server server = new Server
        {
            Services = { TServerLServerService.BindService(tServerService) },
            Ports = { serverPort }
        };

        server.Start();

        //Configuring HTTP for client connections in Register method
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        // Timer related activities
        TimerCallback timerCallback = NextEpoch;
        Timer timer = new Timer(timerCallback, null, DELTA, DELTA);
        Console.WriteLine("Timer started at " + DateTime.Now);

        Console.WriteLine("Server is running in the port: " + port + " and is ready to accept requests...");
        while (true) ;
    }
}