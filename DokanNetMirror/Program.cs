using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DokanNet;
using DokanNet.Logging;

namespace DokanNetMirror
{
    internal static class Program
    {
        // ReSharper disable once InconsistentNaming
        private const int SW_MAXIMIZE = 3;

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(System.IntPtr hWnd, int cmdShow);

        public static int ProcessId;

        private static void Main() {
            Process p = Process.GetCurrentProcess();
            ShowWindow(p.MainWindowHandle, SW_MAXIMIZE);

            ProcessStartInfo processStartInfo = new ProcessStartInfo { WorkingDirectory = @"c:\projects\Testing\test\", FileName = "cmd.exe" };
            Process process = Process.Start(processStartInfo);
            ProcessId = process.Id;

            try {
                string basePath = @"c:\projects\Testing\test\base";
                string minePath = @"c:\projects\Testing\test\mine";
                string mountPath = @"c:\projects\Testing\test\mount";

                Task.Factory.StartNew(() => {
                    Notify.Start(basePath, mountPath);
                    DokanOperations dokanOperations = new DokanOperations(basePath, minePath);
                    dokanOperations.Mount(mountPath, DokanOptions.EnableNotificationAPI, 1, new NullLogger());
                });

                Task.Factory.StartNew(() => {
                    do {
                        Console.ReadLine();
                        Console.Clear();
                    } while (!process.HasExited);
                });

                process.WaitForExit();

                Notify.Stop();
                Dokan.RemoveMountPoint(mountPath);

                Console.WriteLine(@"Success");
            } catch (DokanException ex) {
                Console.WriteLine(@"Error: " + ex.Message);
            }
        }
    }
}
