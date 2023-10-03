using System;
using System.Diagnostics;
using System.IO;
using Utilities;

namespace ManagementConsole
{
    // Management Console for the DADTKV System where the configuration files are read and the processes are started
    class Program
    {
        static void Main(string[] args)
        {
            // Gets the full path of the configuration file
            string solutionDir = Resources.GetSolutionDirectoryInfo();
            string configFile = solutionDir + "\\ManagementConsole\\configuration_sample.txt";

            // Checks if the configuration file exists
            if(!File.Exists(configFile))
            {
                Console.WriteLine("Configuration file not found.");
                return;
            }
            
            //Console.WriteLine("Waiting for input...");
            //Console.ReadKey();
            //ServersConfig config = Resources.ParseConfigFile();

            // TODO - read the configuration file and start the processes for each line starting with a "P" (work in progress)
            foreach (string line in File.ReadAllLines(configFile))
            {
                string[] arguments = line.Split(" ");
                if (arguments[0] == "P")
                {
                    // TODO - start the process with the given arguments (work in progress, have to do some research)
                    //StartNewProcess(arguments);
                }
            }
        }
    }
}