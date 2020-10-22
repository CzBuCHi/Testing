using System;
using System.Threading.Tasks;
using Testing.Services;

namespace Testing
{
    public class Program
    {
        private static async Task MainAsync() {
            Project project = new Project("test1.cstech.cz");
            await project.Pull();



            Task.Factory.StartNew(() => {
                project.Watch();
            });


        }

        public static void Main() {
            MainAsync().Wait();
            Console.WriteLine(@"DONE");
            Console.ReadLine();

            //string minePath = @"c:\projects\Testing\test\Mine\";
            //string workingPath = @"c:\projects\Testing\test\Working\";

            //DokanOperations mirror = new DokanOperations(basePath, minePath);
            //Task.Factory.StartNew(() => {
            //    if (!Directory.Exists(workingPath)) {
            //        Directory.CreateDirectory(workingPath);
            //    }

            //    mirror.Mount(workingPath, DokanOptions.DebugMode | DokanOptions.EnableNotificationAPI, 1, new NullLogger());
            //});

            //Console.WriteLine(@"Mounted");

            //// todo: FileSystemWatcher on minePath 
            //// todo: changes upload to server & update in basePath
            //// todo: tortoise merge if server modified on text files
            //// todo: warning dialog if server modified on binary files

            //string line;
            //do {
            //    line = Console.ReadLine();
            //    Console.Clear();
            //} while (line != "q");

            //Dokan.RemoveMountPoint(workingPath);
            //Directory.Delete(workingPath);
            //Console.WriteLine(@"Success");
        }
    }
}
