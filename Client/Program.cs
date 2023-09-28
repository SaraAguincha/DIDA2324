using Client.Services;
using Grpc.Core;
using Grpc.Net.Client;

// Client
class Program
{
    public static void Main(string[] args)
    {
        // TODO - definir o que fazer em relacao ao url dos clients, random, hardcoded? e conneccao com TServer

        // placeholder client - TServer connection
        // private readonly GrpcChannel channel;
        const string clientUrl = "http://localhost:10000";
        const string serverUrl = "http://localhost:10001";
        ClientService client;

        client = new ClientService(serverUrl, clientUrl);
        client.Start(serverUrl, clientUrl);
        Console.WriteLine("Client ready to make requests...");
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        while (true)
        {
            // for test purposes
            if (Console.ReadKey().Key == ConsoleKey.P)
            {
                bool reply = client.Status();
                Console.Write("server responded with: " + reply);
            }
        }
    }
}