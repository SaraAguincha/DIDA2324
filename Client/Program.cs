using Client.Services;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using System.Text.RegularExpressions;

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

                    break;

                // Status command
                // TODO, not sure what the key for this command is, not specified
                case 'S': case 's':
                    bool reply = client.Status();
                    Console.WriteLine("server responded with: " + reply);
                    break;
                default:
                    Console.WriteLine("no command found :(");
                    break;
            }
        }
    }
}