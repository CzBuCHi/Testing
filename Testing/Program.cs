using System;
using System.IO;
using System.Threading.Tasks;
using DokanNet;
using DokanNet.Logging;
using JetBrains.Annotations;
using Testing.Services;

namespace Testing
{
    public class Program
    {
        public static void Main() {
            string basePath = Dir(@"c:\projects\Testing\test\Base");
            string minePath = Dir(@"c:\projects\Testing\test\Mine");
            string workingPath = Dir(@"c:\projects\Testing\test\Working");

            DokanOperations mirror = new DokanOperations(basePath, minePath);

            Task.Factory.StartNew(() => { mirror.Mount(workingPath, DokanOptions.DebugMode | DokanOptions.EnableNotificationAPI, 1, new NullLogger()); });

            Console.WriteLine(@"Mounted");

            string line;
            do {
                line = Console.ReadLine();
                Console.Clear();
            } while (line != "q");

            Dokan.RemoveMountPoint(workingPath);
            Directory.Delete(workingPath);
            Console.WriteLine(@"Success");

            //MainAsync().Wait();
            //Console.WriteLine(@"DONE");
            //Console.ReadLine();
        }

        [NotNull]
        private static string Dir([NotNull] string fullPath) {
            if (!Directory.Exists(fullPath)) {
                Directory.CreateDirectory(fullPath);
            }

            return fullPath;
        }

        //private static async Task MainAsync() {
        //    Project project = new Project("test1.cstech.cz");
        //    await project.Pull();
        //    project.Initialize();
        //}
    }
}
