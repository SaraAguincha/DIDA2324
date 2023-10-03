using Grpc.Core;
using Grpc.Net.Client;
using System.Text.RegularExpressions;

// Lease Server Main
    class Program
{
    public static void Main(string[] args)
    {
        // parsing according to config file
        // a TServer will be given the following information:
        // "LM2 L "http://localhost:10200"
        // additional information is needed:
        //  - number of processes running and respective ids and URL

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

        // Bind all the services:
        // Client Services          (Client Commands)   -> currently the only one
        // TManagerServer Services  (Info disclosure)
        // LManagerServer Service   (Leases requests)
        Server server = new Server
        {
            //Services = { LServerTServerService.BindService(new LserverServiceTServer()) },
            Ports = { serverPort }
        };

        server.Start();

        //Configuring HTTP for client connections in Register method
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        Console.WriteLine("Server is running in the port: " + port + " and is ready to accept requests...");
        while (true) ;
    }
}