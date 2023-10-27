using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Utilities
{
    // Utilities to be used in the DADTKV System, such as structs and methods

    // Structs

    // Struct to store the information of a server process
    public struct ServerProcessInfo
    {
        public string Id { get; }
        public string Type { get; }
        public string Url { get; }

        public ServerProcessInfo(string id, string type, string url)
        {
            this.Id = id;
            this.Type = type;
            this.Url = url;
        }
    }

    // Struct to store the state of a server process
    public struct ServerProcessState
    {
        public bool Crashed { get; }
        public (bool, List<string>) Suspects { get; }

        public ServerProcessState(bool crashed, bool suspects, List<string> ids)
        {
            this.Crashed = crashed;
            this.Suspects = (suspects, ids);
        }
    }

    // Struct to store the configuration of the servers given from the configuration file
    public struct ServersConfig
    {
        public List<ServerProcessInfo> TServers { get; }
        public List<ServerProcessInfo> LServers { get; }
        public (TimeSpan, int) Slot { get; }
        public Dictionary<string, ServerProcessState>[] ProcessStates { get; }

        public ServersConfig(List<ServerProcessInfo> tServers, List<ServerProcessInfo> lServers, TimeSpan start, int duration,
            Dictionary<string, ServerProcessState>[] processStates)
        {
            this.TServers = tServers;
            this.LServers = lServers;
            this.Slot = (start, duration);
            this.ProcessStates = processStates;
        }
    }

    public static class Resources
    {
        // Method to get the solution directory path
        public static string GetSolutionDirectoryInfo(string currentPath = null)
        {
            var directory = new DirectoryInfo(
                currentPath ?? Directory.GetCurrentDirectory());
            // Check if the directory contains a .sln file
            while (directory != null && !directory.GetFiles("*.sln").Any())
            {
                directory = directory.Parent;
            }
            // Can be null if not found but that will never happen as we always have a .sln file
            return directory.FullName;
        }

        // Read and parse the configuration file and store the information in a ServersConfig struct
        public static ServersConfig ParseConfigFile()
        {
            string solutionDir = GetSolutionDirectoryInfo();
            string configFile = solutionDir + "\\ManagementConsole\\configuration_sample.txt";

            // Check if the configuration file exists
            if (!File.Exists(configFile))
            {
                Console.WriteLine("Configuration file not found.");
                return new ServersConfig();
            }

            // Lists to store the information of the servers
            List<ServerProcessInfo> tServers = new List<ServerProcessInfo>();
            List<ServerProcessInfo> lServers = new List<ServerProcessInfo>();

            // Auxiliary list to store the information of all servers in order
            List<ServerProcessInfo> allServers = new List<ServerProcessInfo>();

            // Variables to store the slot information
            TimeSpan start = new TimeSpan();
            int duration = 10000;

            // Array to store the process states
            Dictionary<string, ServerProcessState>[] processStates = null;

            // Read the configuration file and store the information in the variables
            foreach (string line in File.ReadAllLines(configFile))
            {
                string[] args = line.Split(" ");

                // Process P commands
                if (args[0] == "P" && args[2] != "C")
                {
                    ServerProcessInfo serverInfo = new ServerProcessInfo(args[1], args[2], args[3]);
                    if (args[2] == "T")
                    {
                        tServers.Add(serverInfo);
                        allServers.Add(serverInfo);
                    }
                    else if (args[2] == "L")
                    {
                        lServers.Add(serverInfo);
                        allServers.Add(serverInfo);
                    }
                }
                // Process S commands
                else if (args[0] == "S")
                {
                    // Initialize the processStates array with the number of slots as the size
                    processStates = new Dictionary<string, ServerProcessState>[Int32.Parse(args[1])];
                }
                // Process T commands
                else if (args[0] == "T")
                {
                    string[] startArgs = args[1].Split(":");
                    start = new TimeSpan(Int32.Parse(startArgs[0]), Int32.Parse(startArgs[1]), Int32.Parse(startArgs[2]));
                }
                // Process D commands
                else if (args[0] == "D")
                {
                    duration = Int32.Parse(args[1]);
                }
                // Process F commands
                else if (args[0] == "F")
                {
                    int slot = Int32.Parse(args[1]);
                    processStates[slot -1] = new Dictionary<string, ServerProcessState>();
                    // Index of where the suspects start in the args array
                    int serverCountIndex = (allServers.Count + 2);
                    string[] state = new string[allServers.Count];
                    string[][] suspects = new string[args.Length - serverCountIndex][];

                    // Store the state of each server
                    for (int i = 2; i < serverCountIndex; i++)
                    {
                        state[i - 2] = args[i];
                    }
                    // Store the suspects
                    for (int i = serverCountIndex; i < args.Length; i++)
                    {
                        suspects[i - serverCountIndex] = args[i].Trim('(', ')').Split(',');
                    }
                    // Store the state of each server in the slot
                    for (int i = 0; i < state.Length; i++)
                    {
                        bool crashed = state[i] == "C";
                        bool suspectsBool = false;
                        List<string> suspectsId = new List<string>();
                        // If there are suspected processes
                        if (suspects.Length != 0)
                        {
                            foreach (string[] suspect in suspects)
                            {
                                // If the suspecting process is in the ordered list of all servers
                                if (suspect[1] == allServers[i].Id)
                                {
                                    suspectsBool = true;
                                    suspectsId.Add(suspect[0]);
                                }
                            }
                        }
                        // Add the state of the server to the slot
                        processStates[slot - 1].Add(allServers[i].Id, new ServerProcessState(crashed, suspectsBool, suspectsId));
                    }
                }
            }
            // Fill out the remaining slots with the last slot
            for (int i = 0; i < processStates.Length; i++)
            {
                if (processStates[i] == null && i > 0)
                {
                    processStates[i] = processStates[i - 1];
                }
            }

            return new ServersConfig(tServers, lServers, start, duration, processStates);
        }
    }
}