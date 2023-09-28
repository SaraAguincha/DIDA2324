using Grpc.Core;
using Grpc.Net.Client;
using TServer.Services;

// Transaction Server
class Program
{
    public static void Main(string[] args)
    {
        // placeholder
        const string clientUrl = "http://localhost:10000";
        const string serverUrl = "http://localhost:10001";


        const int port = 10001;
        const string hostname = "localhost";
        ServerPort serverPort;

        serverPort = new ServerPort(hostname, port, ServerCredentials.Insecure);

            Server server = new Server
            {
                Services = { ClientTServerService.BindService(new TServerServiceClient()) },
                Ports = { serverPort }
            };

        server.Start();

        //Configuring HTTP for client connections in Register method
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        while (true) ;
    }
}