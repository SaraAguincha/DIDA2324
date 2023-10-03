using Grpc.Core;
using Grpc.Net.Client;
using Protos;
using System.Text.RegularExpressions;
using TServer.Services;

// Transaction Server
class Program
{
    public static void Main(string[] args)
    {
        // parsing according to config file
        // a TServer will be given the following information:
        // "TM1 T http://localhost:10001"
        // additional information is needed:
        //  - number of processes running and respective ids and URL

        /*  WHEN CONFIG WORKS
        string arguments = Console.ReadLine();          //unnecessary when script implemented
        string[] initialArgs = arguments.Split(" ");    //unnecessary when script implemented


        string TManagerId = initialArgs[0];            //args[0] when script implemented

        string urlPattern = @"http://([^:/]+):(\d+)";
        Match match = Regex.Match(initialArgs[2], urlPattern);

        string hostname = match.Groups[1].Value;            // group 1 will contain the IP address
        int port = Int32.Parse(match.Groups[2].Value);      // group 2 will contain the port
 
        */

        // placeholder information  ----------------------------------------------
        string hostname = "localhost";
        int port = 10001;
        string TManagerId = "TM1";
        Dictionary<string, string> Tservers = new Dictionary<string, string>();
        Dictionary<string, string> Lservers = new Dictionary<string, string>();
        Lservers.Add("LM1", "http://localhost:10200");
        Lservers.Add("LM2", "http://localhost:10201");

        // ------------------------------------------------------------------------


        ServerPort serverPort;
        serverPort = new ServerPort(hostname, port, ServerCredentials.Insecure);

        // all the functions of the TServer will be done here
        TServerService TServerService = new TServerService(TManagerId, Tservers, Lservers);

        // all of the function call async related to clients, tservers and lservers
        TServerService_Client clientService = new TServerService_Client(TServerService);

        Server server = new Server
        {
            Services = { ClientTServerService.BindService(clientService) },
            Ports = { serverPort }
        };

        server.Start();

        //Configuring HTTP for client connections in Register method
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        
        Console.WriteLine("Server is running on port: " + port + " and is ready to accept requests...");
        while (true) ;
    }
}