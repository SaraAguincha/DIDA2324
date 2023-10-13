using Client.Services;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Grpc.Core;
using Grpc.Net.Client;
using Protos;
using System.Text.RegularExpressions;
using Utilities;


// Client
class Program
{
    public static void Main(string[] args)
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        
        // Client configuration
        string processId = args[0];
        string script = args[1];
        Console.WriteLine("Client Id: " + processId);

        // Receive info from the configuration file
        ServersConfig config = Resources.ParseConfigFile();

        // Get the server info from the config file
        Dictionary<string, ClientTServerService.ClientTServerServiceClient> tServers = config.TServers.ToDictionary(
            key => key.Id, 
            value =>
            {   GrpcChannel channel = GrpcChannel.ForAddress(value.Url);
                return new ClientTServerService.ClientTServerServiceClient(channel);
            }
        );
        (TimeSpan startTime, int duration) = config.Slot;

        // Create the client service
        ClientService client = new ClientService(processId, tServers);

        // Get the client script
        string solutionDir = Resources.GetSolutionDirectoryInfo();
        string scriptPath = solutionDir + "\\Client\\Scripts\\" + script + ".txt";
        Console.WriteLine("Script path: " + scriptPath);
        
        // Wait until the the time slot starts
        // TODO hardcoded for now cause still not sure how to incorporate the start time
        System.Threading.Thread.Sleep(3000);

        // Read and parse the client script
        string[] lines = File.ReadAllLines(scriptPath);

        foreach(string line in lines)
        {
            string[] strings = line.Split(" ", StringSplitOptions.RemoveEmptyEntries);

            if (strings.Length == 0) { continue; }

            switch (strings[0])
            {
                // Transaction command, will have a list of DadInts to read and to write (may be empty)
                // Has the following format:
                // T ("a-key-name","another-key-name") (<"name1",10>,<"name2",20>) 
                case "T":
                    // String list with three elements: command, list of reads and list of writes
                    string[] operations = Regex.Split(line, @"\s+");

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
                        // Groups[1] is the string between the quotes
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
                    RepeatedField<DadInt> txReply = client.TxSubmit(reads, writes).Result;

                    if (txReply == null) { break; }

                    foreach (DadInt reply in txReply)
                    {
                        // When the transaction is not completed, a DadInt with key == abort is returned
                        if (reply.Key == "abort")
                        {
                            Console.WriteLine("Something went wrong during the transaction.. Please repeat again.");
                            break;
                        } 
                        Console.WriteLine($"DadInt with Id: {reply.Key}");
                        Console.WriteLine($"Has Value: {reply.Val}");
                    }
                    break;

                // Wait command
                case "W":
                    if (strings.Length != 2)
                    {
                        Console.WriteLine("Invalid number of arguments.");
                        break;
                    }
                    if (!int.TryParse(strings[1], out int waitTime) || waitTime < 0)
                    {
                        Console.WriteLine("Invalid argument (must be a positive integer).");
                        break;
                    }
                    Console.WriteLine("Waiting for " + waitTime + " milliseconds.");
                    System.Threading.Thread.Sleep(waitTime);
                    Console.WriteLine("Finished waiting"); 
                    break;

                // Status command
                case "S":
                    if (strings.Length != 1) { 
                        Console.WriteLine("Invalid number of arguments.");
                        break;
                    }

                    bool statReply = client.Status().Result;
                    Console.WriteLine("Status: server responded with " + statReply);
                    break;

                // Ignore comments
                case "#":
                    break;

                default:
                    Console.WriteLine("Command '" + strings[0] + "' is invalid.");
                    break;
            }
        }
        // FOR TESTING PURPOSES, ACCEPTING INPUT FROM THE CONSOLE
        Console.WriteLine("Now accepting input from user...");
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

                    if (txReply == null) { break; }

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
                    Console.WriteLine();
                    bool statReply = client.Status().Result;
                    Console.WriteLine("server responded with: " + statReply);
                    break;
                default:
                    Console.WriteLine("no command found :(");
                    break;
            }
        }
    }
}