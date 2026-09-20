using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using GetEQd.Diagnostics;
using GetEQd.Systemwide;
using GetEQd.Ui;
using GetEQd.Updates;

namespace GetEQd
{
    /// <summary>
    /// Entry point. Launches the console, or one of the headless modes used to verify a
    /// build without a person at the keyboard.
    /// </summary>
    public partial class App : Application
    {
        private const int AttachParentProcess = -1;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Length > 0)
            {
                string mode = e.Args[0].ToLowerInvariant();

                if (mode == "--selftest" || mode == "--render-preview")
                {
                    EnableConsoleOutput();
                    TextWriter output = Console.Out;

                    int code = mode == "--selftest"
                        ? SelfTest.Run(e.Args, output)
                        : PreviewRenderer.Run(e.Args, output);

                    output.Flush();

                    // A WPF entry point returns void, so the process exit code comes from here.
                    Environment.ExitCode = code;
                    Shutdown(code);
                    return;
                }

                if (mode == "--check-updates")
                {
                    EnableConsoleOutput();
                    int code = UpdateChecker.Run(e.Args, Console.Out);
                    Console.Out.Flush();
                    Environment.ExitCode = code;
                    Shutdown(code);
                    return;
                }

                if (mode == "--apo-config")
                {
                    EnableConsoleOutput();
                    int code = ApoConfig.Run(e.Args, Console.Out);
                    Console.Out.Flush();
                    Environment.ExitCode = code;
                    Shutdown(code);
                    return;
                }

                if (mode == "--help" || mode == "-h" || mode == "/?")
                {
                    EnableConsoleOutput();
                    Console.WriteLine("getEQd for Windows");
                    Console.WriteLine();
                    Console.WriteLine("  getEQd.exe                                 open the console");
                    Console.WriteLine("  getEQd.exe --selftest [report.txt]         verify the audio engine");
                    Console.WriteLine("  getEQd.exe --render-preview out.png [...]  render the interface to a PNG");
                    Console.WriteLine("  getEQd.exe --check-updates                 ask GitHub Releases for a newer build");
                    Console.WriteLine("  getEQd.exe --apo-config [--preset id]      print the Equalizer APO configuration");
                    Console.Out.Flush();
                    Environment.ExitCode = 0;
                    Shutdown(0);
                    return;
                }
            }

            MainWindow window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        /// <summary>
        /// A GUI process starts with no console attached. When launched from a terminal we
        /// borrow the parent's so the headless modes can report what they found.
        /// </summary>
        private static void EnableConsoleOutput()
        {
            try
            {
                if (AttachConsole(AttachParentProcess))
                {
                    StreamWriter writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                    Console.SetOut(writer);
                }
            }
            catch
            {
                // Output still lands in the report file when there is no console to borrow.
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);
    }
}
