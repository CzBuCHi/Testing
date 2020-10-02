using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DokanNet;
using DokanNet.Logging;

namespace ProjectsManager
{
    class Program
    {
        static void Main()
        {
            var basePath = @"c:\ProjectsManager\Base\";

            var minePath = @"c:\ProjectsManager\Mine\";
            var workingPath = @"c:\ProjectsManager\Working\";

            var mirror = new DokanOperations(basePath, minePath);
            

            Task.Factory.StartNew(() => {
                if (!Directory.Exists(workingPath)) {
                    Directory.CreateDirectory(workingPath);
                }

                mirror.Mount(workingPath, DokanOptions.DebugMode | DokanOptions.EnableNotificationAPI, 1, new NullLogger());
            });

            Console.WriteLine(@"Mounted");
            string line;
            do {
                line = Console.ReadLine();
                Console.Clear();
            } while (line != "q");

            Dokan.RemoveMountPoint(workingPath);
            Directory.Delete(workingPath);
            Console.WriteLine(@"Success");


        }
    }
}
