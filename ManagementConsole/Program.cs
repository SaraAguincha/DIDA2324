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

        static Process StartNewProcess(string[] args, DateTime startTime)
        {
            // Starts a new process with the given arguments
            // Returns the process
            string solutionDir = Resources.GetSolutionDirectoryInfo();
            string clientPath = solutionDir + "\\Client\\bin\\Debug\\net6.0\\Client.exe";
            string tServerPath = solutionDir + "\\TServer\\bin\\Debug\\net6.0\\TServer.exe";
            string lServerPath = solutionDir + "\\LServer\\bin\\Debug\\net6.0\\LServer.exe";

            if (args[2] == "C")
            {
                return SetProcessInfo(clientPath, args[1] + " " + args[3] + " " + startTime.ToString("HH:mm:ss"));
            }
            else if (args[2] == "T" || args[2] == "L")
            {
                string url = args[3].Remove(0, 7);
                string hostname = url.Split(":")[0];
                string port = url.Split(":")[1];

                if (args[2] == "T")
                {
                    return SetProcessInfo(tServerPath, args[1] + " " + hostname + " " + port + " " + startTime.ToString("HH:mm:ss"));
                }
                else
                {
                    return SetProcessInfo(lServerPath, args[1] + " " + hostname + " " + port + " " + startTime.ToString("HH:mm:ss"));
                }
            }
            else
            {
                Console.WriteLine("Invalid arguments on configuration file.");
                return null;
            }
        }

        // Method to build a project
        static int RunDotnetBuild(string solutionDir, string projectFile, string configuration)
        {
            // Use the dotnet build command to build the project
            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build \"{projectFile}\" -c {configuration}",
                    WorkingDirectory = solutionDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();

            // Print the build output to the console
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            Console.WriteLine(output);
            Console.WriteLine(error);

            return process.ExitCode;
        }

        static void Main(string[] args)
        {
            // Gets the full path of the configuration file
            string solutionDir = Resources.GetSolutionDirectoryInfo();
            string configFile = solutionDir + "\\ManagementConsole\\configuration_sample.txt";

            // Checks if the configuration file exists
            if (!File.Exists(configFile))
            {
                Console.WriteLine("Configuration file not found.");
                return;
            }

            // Gets the full path of the project files
            string clientProjectFile = solutionDir + "\\Client\\Client.csproj";
            string tServerProjectFile = solutionDir + "\\TServer\\TServer.csproj";
            string lServerProjectFile = solutionDir + "\\LServer\\LServer.csproj";
            string buildConfiguration = "Debug";

            // Builds the projects
            int exitCodeClient = RunDotnetBuild(solutionDir, clientProjectFile, buildConfiguration);
            int exitCodeTServer = RunDotnetBuild(solutionDir, tServerProjectFile, buildConfiguration);
            int exitCodeLServer = RunDotnetBuild(solutionDir, lServerProjectFile, buildConfiguration);

            // Checks if the builds were successful
            if (exitCodeClient != 0)
            {
                Console.WriteLine("Client build failed. Check for build errors before running the executable.");
                return;
            }
            else if (exitCodeTServer != 0)
            {
                Console.WriteLine("TServer build failed. Check for build errors before running the executable.");
                return;
            }
            else if (exitCodeLServer != 0)
            {
                Console.WriteLine("LServer build failed. Check for build errors before running the executable.");
                return;
            }
            Console.WriteLine("Build successful.");

            // Get the current time and add 5 secs to it
            DateTime currentTime = DateTime.Now;
            DateTime startTime = currentTime.AddSeconds(5);
            Console.WriteLine("Current time: " + currentTime.ToString("HH:mm:ss") + "\n" +
                "Start time: " + startTime.ToString("HH:mm:ss") + "\n");

            // Parses the configuration file and starts the processes
            foreach (string line in File.ReadAllLines(configFile))
            {
                string[] arguments = line.Split(" ");
                if (arguments[0] == "P")
                {
                    StartNewProcess(arguments, startTime);
                }
            }

            // Give the choice to the user to stop all the processes or to exit the management console
            Console.WriteLine("Press 's' to stop all processes, 'p' to restart all processes or 'q' to quit the management console.");
            while (true)
            {
                switch (Console.ReadKey().KeyChar)
                {
                    // Stop all processes
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
                        Console.WriteLine("\nStopped all processes.");
                        break;
                    // Close the management console
                    case 'q':
                        return;
                    // Restart all processes
                    case 'p':
                        currentTime = DateTime.Now;
                        startTime = currentTime.AddSeconds(5);

                        foreach (string line in File.ReadAllLines(configFile))
                        {
                            string[] arguments = line.Split(" ");
                            if (arguments[0] == "P")
                            {
                                StartNewProcess(arguments, startTime);
                            }
                        }
                        Console.WriteLine("\nRestarted all processes.");
                        break;
                    default:
                        Console.WriteLine("\nInvalid command.");
                        break;
                }
            }
        }
    }
}