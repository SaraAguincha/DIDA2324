﻿using System;
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
    // TODO not sure if this is the best way to store the server process states but leaving this for now
    public struct ServerProcessState
    {
        public bool Crashed { get; }
        public (bool, int) Suspects { get; }

        public ServerProcessState(bool crashed, bool suspects, int id)
        {
            this.Crashed = crashed;
            this.Suspects = (suspects, id);
        }
    }

    // Struct to store the configuration of the servers given from the configuration file
    public struct ServersConfig
    {
        public List<ServerProcessInfo> TServers { get; }
        public List<ServerProcessInfo> LServers { get; }
        public (TimeSpan, int) Slot { get; }
        // TODO not sure if this is the best way to store the server process states but leaving this for now
        public Dictionary<int, ServerProcessState>[] ProcessStates { get; }

        public ServersConfig(List<ServerProcessInfo> tServers, List<ServerProcessInfo> lServers, TimeSpan start, int duration,
            Dictionary<int, ServerProcessState>[] processStates)
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

            // Variables to store the slot information
            TimeSpan start = new TimeSpan();
            int duration = 10000;

            Dictionary<int, ServerProcessState>[] processStates = null;

            // Read the configuration file and store the information in the variables
            foreach (string line in File.ReadAllLines(configFile))
            {
                string[] args = line.Split(" ");

                if (args[0] == "P" && args[2] != "C")
                {
                    ServerProcessInfo serverInfo = new ServerProcessInfo(args[1], args[2], args[3]);
                    if (args[2] == "T")
                    {
                        tServers.Add(serverInfo);
                    }
                    else if (args[2] == "L")
                    {
                        lServers.Add(serverInfo);
                    }
                }
                else if (args[0] == "S")
                {
                    processStates = new Dictionary<int, ServerProcessState>[Int32.Parse(args[1])];
                }
                else if (args[0] == "T")
                {
                    string[] startArgs = args[1].Split(":");
                    start = new TimeSpan(Int32.Parse(startArgs[0]), Int32.Parse(startArgs[1]), Int32.Parse(startArgs[2]));
                }
                else if (args[0] == "D")
                {
                    duration = Int32.Parse(args[1]);
                }
                else if (args[0] == "F")
                {
                    /*
                      In a F command:
                       - The first integer indentifies the time slot it describes
                       - It is followed by a sequence of (N)ormal or (C)rashed characters,
                         one for each server process in the system
                       - The line ends with a sequence of pairs indentifying suspected processes
                         each pair includes the ids of the suspecting process and the suspected process,
                         e.g. (A,B) means that process A suspects process B
                       Example of F command: F 1 N N N N N N (TM1,TM2) (lease3,LM2)
                    */
                    int slot = Int32.Parse(args[1]);
                    processStates[slot -1] = new Dictionary<int, ServerProcessState>();
                    string[] state = null;
                    string[][] suspects = null;
                    int serverCountIndex = (tServers.Count + lServers.Count + 2);
                    for (int i = 2; i < serverCountIndex; i++)
                    {
                        state[i - 2] = args[i];
                    }
                    for (int i = serverCountIndex; i < args.Length; i++)
                    {
                        suspects[i - serverCountIndex] = args[i].Trim('(', ')').Split(',');
                    }
                    // I have idea but takes a while and is too late and zzz time
                    // Perhaps tie the numbers on the id to the index of the dictionary
                    // Or might have to change the dictionary to string instead of int on the key
                }
            }
            return new ServersConfig(tServers, lServers, start, duration, processStates);
        }
    }
}