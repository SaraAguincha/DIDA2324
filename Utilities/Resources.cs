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

        public ServersConfig(List<ServerProcessInfo> tServers, List<ServerProcessInfo> lServers, TimeSpan startTime, int duration,
            Dictionary<int, ServerProcessState>[] processStates)
        {
            this.TServers = tServers;
            this.LServers = lServers;
            this.Slot = (startTime, duration);
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
    }
}