using System;
using System.Diagnostics;
using System.IO;
using Utilities;

namespace ManagementConsole
{
    // Management Console for the DADTKV System where the configuration files are read and the processes are started
    class Program
    {
        static Process SetProcessInfo(string path, string info)
        {
            // Sets the information of a process
            // Returns the process
            ProcessStartInfo processInfo = new ProcessStartInfo();
            processInfo.FileName = path;
            processInfo.Arguments = info;
            processInfo.UseShellExecute = true;
            processInfo.CreateNoWindow = false;
            processInfo.WindowStyle = ProcessWindowStyle.Normal;
            return Process.Start(processInfo);
        }

        static Process StartNewProcess(string[] args)
        {
            // Starts a new process with the given arguments
            // Returns the process
            string solutionDir = Resources.GetSolutionDirectoryInfo();
            string clientPath = solutionDir + "\\Client\\bin\\Debug\\net6.0\\Client.exe";
            string tServerPath = solutionDir + "\\TServer\\bin\\Debug\\net6.0\\TServer.exe";
            string lServerPath = solutionDir + "\\LServer\\bin\\Debug\\net6.0\\LServer.exe";

            if (args[2] == "C")
            {
                string info = args[1] + " " + args[3];
                return SetProcessInfo(clientPath, info);
            }
            else if (args[2] == "T" || args[2] == "L")
            {
                string url = args[3].Remove(0, 7);
                string hostname = url.Split(":")[0];
                string port = url.Split(":")[1];

                if (args[2] == "T")
                {
                    return SetProcessInfo(tServerPath, args[1] + " " + hostname + " " + port);
                }
                else
                {
                    return SetProcessInfo(lServerPath, args[1] + " " + hostname + " " + port);
                }
            }
            else
            {
                Console.WriteLine("Invalid arguments on configuration file.");
                return null;
            }
        }

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

            foreach (string line in File.ReadAllLines(configFile))
            {
                string[] arguments = line.Split(" ");
                if (arguments[0] == "P")
                {
                    StartNewProcess(arguments);
                }
            }
            // Give the choice to the user to stop all the processes or to exit the management console
            Console.WriteLine("Press 's' to stop all processes or 'q' to quit the management console.");
            while (true)
            {
                switch (Console.ReadKey().KeyChar)
                {
                    case 's':
                        Process[] processes = Process.GetProcessesByName("Client");
                        foreach (Process process in processes)
                        {
                            process.Kill();
                        }
                        processes = Process.GetProcessesByName("TServer");
                        foreach (Process process in processes)
                        {
                            process.Kill();
                        }
                        processes = Process.GetProcessesByName("LServer");
                        foreach (Process process in processes)
                        {
                            process.Kill();
                        }
                        Console.WriteLine("");
                        break;
                    case 'q':
                        return;
                    default:
                        Console.WriteLine("\nInvalid command.");
                        break;
                }
            }
        }
    }
}