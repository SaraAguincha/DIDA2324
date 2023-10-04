using Client.Services;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Grpc.Core;
using Grpc.Net.Client;
using Protos;
using System.Text.RegularExpressions;

// Client
class Program
{
    public static void Main(string[] args)
    {
        // TODO - add properties from config instead of placeholders


        // Placeholder client information
        const string serverHostname = "localhost";
        const int serverPort = 10000;
        char lastChar = args[0][args[0].Length - 1];
        string serverUrl = $"http://127.0.0.1:1000{lastChar}";
        Console.WriteLine("Connecting to: " + serverUrl);

        string clientHostname = "localhost";
        Random random = new Random();
        int clientPort = random.Next(11000, 15001);                 // random port in the future
        string clientUrl = $"http://{clientHostname}:{clientPort.ToString()}";
        const string clientId = "c2";

        // Client configuration
        string processId = args[0];
        string script = args[1];
        
        ClientService client;
        client = new ClientService(clientId, serverUrl, clientUrl);

        // TODO - how to proceed when TServer crashes?
        client.server = new Server
        {
            Services = { ClientTServerService.BindService(client) },
            Ports = { new ServerPort(clientHostname, clientPort, ServerCredentials.Insecure) }

        };
        client.server.Start();



        Console.WriteLine("Client ready to make requests...");
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        while (true)
        {
            // Parsing according to the config files
            // First char will be the command to be executed!

            switch (Console.ReadKey().KeyChar)
            {
                // Transaction command, will have a list of DadInts to read and to write (may be empty)
                // has the following format:
                // T ("a-key-name","another-key-name") (<"name1",10>,<"name2",20>) 
                case 'T': case 't':
                    string readCommand = Console.ReadLine();
                    // string with two elements, list of reads and list of writes
                    string[] operations = Regex.Split(readCommand, @"\s+");

                    if (operations == null || operations?.Length != 3) 
                    { 
                        Console.WriteLine("Invalid number of arguments.");
                        break;
                    }

                    // List of dadInts to read and to write
                    List<string> reads = new List<string>();
                    List<DadInt> writes = new List<DadInt>();

                    string readPattern = @"""(.*?)""";
                    string writePattern = @"<""([0-9a-zA-Z-:_]*?)\"",(\d+)>";

                    Regex readRegex = new Regex(readPattern);
                    Regex writeRegex = new Regex(writePattern);

                    // Iterate through matches and add them to write and read list
                    foreach (Match match in readRegex.Matches(operations[1]))
                    {
                        //Groups[1] is the string between the quotes
                        reads.Add(match.Groups[1].Value);
                    }
                    foreach (Match match in writeRegex.Matches(operations[2]))
                    {
                        DadInt dadInt = new DadInt();
                        // Group[1] holds the key, Group[2] holds the value
                        dadInt.Key = match.Groups[1].Value;
                        if (Int32.TryParse(match.Groups[2].Value, out int intValue)) { dadInt.Val = intValue; }
                        writes.Add(dadInt);
                    }
                    //Console.WriteLine("Reads: " + reads.Count + "\nWrites: " + writes.Count);
                    //Console.WriteLine(reads[0] + "\n" + reads[1]);
                    //Console.WriteLine(writes[0].Key + "\n" + writes[1].Key);
                    RepeatedField<DadInt> txReply = client.TxSubmit(reads, writes).Result;

                    foreach (DadInt reply in txReply)
                    {
                        // when the transaction is not realised, a DadInt with key == abort is returned
                        if (reply.Key == "abort")
                        {
                            Console.WriteLine("Something went wrong during the transaction.. Please repeat again.");
                            break;
                        } 
                        Console.WriteLine($"DadInt with Id: {reply.Key}");
                        Console.WriteLine($"Has Value: {reply.Val}");
                    }

                    break;

                // Status command
                // TODO, not sure what the key for this command is, not specified
                case 'S': case 's':
                    bool statReply = client.Status();
                    Console.WriteLine("server responded with: " + statReply + "\n");
                    break;
                default:
                    Console.WriteLine("no command found :(");
                    break;
            }
        }
    }
}