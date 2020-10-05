using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DokanNet;
using DokanNet.Logging;

namespace ProjectsManager
{
    internal class Program
    {
        private static void Main() {
            string basePath = @"c:\projects\Testing\test\Base\";

            string host = "127.0.0.1";
            FtpClient ftp = new FtpClient(host, "test", "test");
            IReadOnlyCollection<FtpFileSystemInfo> data = ftp.ListDirectoryTreeAsync("/test1.cstech.cz/html/App_Code").Result;

            string rootPath = Path.GetFullPath(Path.Combine(basePath, host, "test1.cstech.cz", "html", "App_Code"));
            if (!Directory.Exists(rootPath)) {
                Directory.CreateDirectory(rootPath);
            }

            SemaphoreSlim semaphore = new SemaphoreSlim(10);

            // directory tree sync
            List<string> validLocalDirs = new List<string>();
            foreach (FtpDirectoryInfo info in data.OfType<FtpDirectoryInfo>()) {
                string fullPath = Path.GetFullPath(Path.Combine(basePath, host, info.FullName.Substring(1)));
                if (!Directory.Exists(fullPath)) {
                    Directory.CreateDirectory(fullPath);
                }

                validLocalDirs.Add(fullPath);
            }

            DirectoryInfo[] localDirs = new DirectoryInfo(rootPath).GetDirectories("*", SearchOption.AllDirectories);
            foreach (DirectoryInfo directoryInfo in localDirs.Where(o => !validLocalDirs.Contains(o.FullName))) {
                directoryInfo.Delete(true);
            }

            var localFiles = new DirectoryInfo(rootPath).GetFiles("*", SearchOption.AllDirectories);

            // todo: delete local if not in data
            // todo: download from server if local not exists or local last write time != server modified
            // todo: diff on text files (cs, cshtml, aspx, ascx, js, css, scss, config, widget, resx)

            Stopwatch w = new Stopwatch();
            w.Start();


            var tasks = data.OfType<FtpFileInfo>().Select(async item => {
                Debug.Assert(item != null, nameof(item) + " != null");

                // ReSharper disable once AccessToDisposedClosure
                await semaphore.WaitAsync();
                try {
                    Console.WriteLine(w.Elapsed +  " | Download: " + item.FullName);
                    string fullPath = Path.GetFullPath(Path.Combine(basePath, host, item.FullName.Substring(1)));
                    await ftp.DownloadFileAsync(item.FullName, fullPath);
                } finally {
                    // ReSharper disable once AccessToDisposedClosure
                    semaphore.Release();
                }
            });

            Task.WhenAll(tasks).Wait();
            w.Stop();

            Console.WriteLine(@"Success: " + w.Elapsed);
            Console.ReadLine();
            return;

            string minePath = @"c:\projects\Testing\test\Mine\";
            string workingPath = @"c:\projects\Testing\test\Working\";

            DokanOperations mirror = new DokanOperations(basePath, minePath);
            Task.Factory.StartNew(() => {
                if (!Directory.Exists(workingPath)) {
                    Directory.CreateDirectory(workingPath);
                }

                mirror.Mount(workingPath, DokanOptions.DebugMode | DokanOptions.EnableNotificationAPI, 1, new NullLogger());
            });

            Console.WriteLine(@"Mounted");

            // todo: FileSystemWatcher on minePath 
            // todo: changes upload to server & update in basePath
            // todo: tortoise merge if server modified on text files
            // todo: warning dialog if server modified on binary files

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
