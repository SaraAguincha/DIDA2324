﻿using Client.Services;
using Google.Protobuf.Collections;
using Grpc.Net.Client;
using Protos;
using System.Text;
using System.Text.RegularExpressions;
using Utilities;


// Client
class Program
{
    // Current epoch (has to be global variable)
    private static int epoch = 0;

    private static bool stop = false;

    // Function to execute at the start of each epoch
    // Each epoch executes each DELTA miliseconds
    static void NextEpoch(object state)
    {
        epoch++;
        Console.WriteLine("Advanced to epoch number " + epoch.ToString());
        int numSlots = (int)state;
        if (epoch > numSlots)
        {
            Console.WriteLine("End of time slots.");
            stop = true;
        }
    }

    public static void Main(string[] args)
    {
        // Sleep until the specified time on args[2]
        TimeSpan waitToStart = DateTime.Parse(args[2]).Subtract(DateTime.Now);
        Console.WriteLine("Waiting for " + waitToStart.TotalMilliseconds + " milliseconds.");
        System.Threading.Thread.Sleep(waitToStart);

        // Configuring HTTP for client connections in Register method
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
        Dictionary<string, ClientLServerService.ClientLServerServiceClient> lServers = config.LServers.ToDictionary(
            key => key.Id,
            value =>
            {
                GrpcChannel channel = GrpcChannel.ForAddress(value.Url);
                return new ClientLServerService.ClientLServerServiceClient(channel);
            }
        );

        // Get the slot duration
        int duration = config.Slot.Item2;

        // Get the number of slots
        int numSlots = config.ProcessStates.Length;

        // Create the client service
        ClientService client = new ClientService(processId, tServers, lServers);

        // Get the client script
        string solutionDir = Resources.GetSolutionDirectoryInfo();
        string scriptPath = solutionDir + "\\Client\\Scripts\\" + script;
        Console.WriteLine("Script path: " + scriptPath);

        // Timer related activities
        TimerCallback timerCallback = NextEpoch;
        Timer timer = new Timer(timerCallback, numSlots, duration, duration);
        Console.WriteLine("Timer started at " + DateTime.Now);

        while (!stop)
        {
            // Read and parse the client script
            string[] lines = File.ReadAllLines(scriptPath);

            foreach (string line in lines)
            {
                if (stop) { break; }

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

                        // time spent until the tx is completed
                        DateTime txStartTime = DateTime.Now;


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
                        RepeatedField<DadInt>? txReply = client.TxSubmit(reads, writes).Result;

                        if (txReply == null) { break; }

                        if (txReply.Count == 0)
                        {                          
                            Console.WriteLine("Transaction with no reads available.");
                            break;
                        }

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
                        Thread.Sleep(waitTime);
                        Console.WriteLine("Finished waiting");
                        break;

                    // Status command
                    case "S":
                        if (strings.Length != 1)
                        {
                            Console.WriteLine("Invalid number of arguments.");
                            break;
                        }

                        bool statReply = client.Status().Result;
                        Console.WriteLine("Sent a status request to all servers.");
                        break;

                    // Ignore comments
                    case "#":
                        break;

                    default:
                        Console.WriteLine("Command '" + strings[0] + "' is invalid.");
                        break;
                }
            }
        }
        while (true) { }
    }
}