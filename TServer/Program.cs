using Grpc.Core;
using Grpc.Net.Client;
using Protos;
using System.Text.RegularExpressions;
using TServer.Services;
using Utilities;

// Transaction Server
class Program
{
    public static void Main(string[] args)
    {   
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

        // All the functions of the TServer will be done here
        TServerService tServerService = new TServerService(processId, tServers, lServers);

        // All of the function call async related to clients, tservers and lservers
        TServerService_Client clientService = new TServerService_Client(tServerService);

        TServerService_LServer lServerService = new TServerService_LServer(tServerService);

        Server server = new Server
        {
            Services = { ClientTServerService.BindService(clientService),
                         TServerLServerService.BindService(lServerService)},
            Ports = { serverPort }
        };

        server.Start();

        // Configuring HTTP for client connections in Register method
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        
        Console.WriteLine("Server is running on port: " + port + " and is ready to accept requests...");
        while (true) ;
    }
}