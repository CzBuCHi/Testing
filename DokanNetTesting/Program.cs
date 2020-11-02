using System;
using System.IO;
using System.Threading.Tasks;
using DokanNet;

namespace DokanNetTesting
{
    public static class Program
    {
        public const string BasePath = @"c:\projects\Testing\test\base\";
        public const string MinePath = @"c:\projects\Testing\test\mine\";
        public const string MountPath = @"c:\projects\Testing\test\mount\";

        private static void Main() {
            //FtpClient ftp = new FtpClient("127.0.0.1", "test", "test");
            //ftp.DownloadDirectoryAsync("/", MinePath).Wait();

            Directory.CreateDirectory(MountPath);
            DokanOperations dokanOperations = new DokanOperations(MinePath, BasePath);

            Task.Factory.StartNew(() => {
                dokanOperations.Mount(MountPath, DokanOptions.DebugMode | DokanOptions.EnableNotificationAPI, 1);
            });

            string line;
            do {
                line = Console.ReadLine();
                Console.Clear();
            } while (line != "q");

            Dokan.RemoveMountPoint(MountPath);
            Directory.Delete(MountPath);
        }
    }
}
