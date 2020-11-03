using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DokanNet;
using JetBrains.Annotations;

namespace DokanNetTesting
{
    public static class Program
    {
        public const string BasePath = @"c:\projects\Testing\test\base\";
        public const string FtpPath = @"c:\projects\Testing\test\ftp\a";
        public const string GitMerge = @"c:\Program Files\TortoiseGit\bin\TortoiseGitMerge.exe";
        public const string MinePath = @"c:\projects\Testing\test\mine\";
        public const string MountPath = @"c:\projects\Testing\test\mount\";
        public const string TheirsPath = @"c:\projects\Testing\test\theirs\";

        private static FtpClient _Ftp;

        private static void DokanOperationsOnAfterWriteFile([NotNull] string fileName) {
            string ftpPath = fileName.Replace('\\', '/');
            string minePath = MinePath.TrimEnd('\\') + fileName;
            string basePath = GetBasePath(minePath);

            DateTime? date = _Ftp.GetDateTimestampAsync(ftpPath).Result;
            DateTime lastWriteTime = File.GetLastWriteTime(basePath);

            if (date > lastWriteTime) {
                string theirsPath = TheirsPath.TrimEnd('\\') + fileName;
                _Ftp.DownloadFileAsync(ftpPath, theirsPath).Wait();

                if (FileContentEquals(minePath, theirsPath)) {
                    File.Delete(basePath);
                } else {
                    if (IsTextFile(minePath)) {
                        string attrs =
                            "/base " + basePath + " /basename BASE " +
                            "/mine " + minePath + " /minename MINE " +
                            "/theirs " + theirsPath + " /theirsname THEIRS " +
                            "/saverequired";
                        Process process = Process.Start(GitMerge, attrs);
                        process.WaitForExit();

                        if (process.ExitCode == 0) {
                            File.Delete(basePath);
                            _Ftp.UploadFileAsync(ftpPath, minePath).Wait();
                        }
                    } else {
                        Console.WriteLine("---");
                        Console.WriteLine("CONFLICT: " + minePath);
                        Console.WriteLine("---");
                    }
                }
            } else {
                File.Delete(basePath);
                _Ftp.UploadFileAsync(ftpPath, minePath).Wait();
            }
        }

        private static void DokanOperationsOnBeforeReadFile(string fileName) {
            string ftpPath = fileName.Replace('\\', '/');
            string minePath = MinePath.TrimEnd('\\') + fileName;
            string basePath = GetBasePath(minePath);

            DateTime? date = _Ftp.GetDateTimestampAsync(ftpPath).Result;
            DateTime lastWriteTime = File.GetLastWriteTime(minePath);

            if (date > lastWriteTime) {
                _Ftp.DownloadFileAsync(ftpPath, minePath).Wait();
            }
        }

        private static void DokanOperationsOnBeforeWriteFile([NotNull] string fileName) {
            string minePath = MinePath.TrimEnd('\\') + fileName;
            string basePath = GetBasePath(minePath);

            DateTime lastWriteTime = File.GetLastWriteTime(minePath);
            File.Copy(minePath, basePath, true);
            File.SetLastWriteTime(basePath, lastWriteTime);
        }

        private static bool FileContentEquals([NotNull] string firstPath, [NotNull] string secondPath) {
            FileInfo firstInfo = new FileInfo(firstPath);
            FileInfo secondInfo = new FileInfo(secondPath);

            if (!firstInfo.Exists || !secondInfo.Exists) {
                throw new InvalidOperationException("Both files must exist.");
            }

            if (firstInfo.Length != secondInfo.Length) {
                return false;
            }

            using (FileStream firstReader = File.OpenRead(firstPath)) {
                using (FileStream secondReader = File.OpenRead(firstPath)) {
                    while (!firstReader.CanRead) {
                        if (firstReader.ReadByte() != secondReader.ReadByte()) {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        [NotNull]
        private static string GetBasePath([NotNull] string minePath) {
            string relative = minePath.Substring(MinePath.Length).TrimStart('\\');
            return Path.Combine(BasePath, relative);
        }

        private static bool IsTextFile(string minePath) {
            string ext = Path.GetExtension(minePath);
            string[] exts = { ".cs", ".cshtml", ".ascx", ".aspx", ".master", ".json", ".js", ".ts", ".css", ".scss", ".config", ".resx", ".asax", ".widget" };
            return exts.Contains(ext);
        }

        private static void Main() {
            // reset client
            if (Directory.Exists(MinePath)) {
                Directory.Delete(MinePath, true);
            }

            Directory.CreateDirectory(MinePath);

            if (Directory.Exists(BasePath)) {
                Directory.Delete(BasePath, true);
            }
            Directory.CreateDirectory(BasePath);

            // download
            _Ftp = new FtpClient("127.0.0.1", "test", "test");
            _Ftp.DownloadDirectoryAsync("/", MinePath).Wait();

            // start dokan
            Directory.CreateDirectory(MountPath);
            DokanOperations dokanOperations = new DokanOperations(MinePath);
            dokanOperations.BeforeWriteFile += DokanOperationsOnBeforeWriteFile;
            dokanOperations.AfterWriteFile += DokanOperationsOnAfterWriteFile;
            dokanOperations.BeforeReadFile += DokanOperationsOnBeforeReadFile;
            Task.Factory.StartNew(() => { dokanOperations.Mount(MountPath, DokanOptions.DebugMode | DokanOptions.EnableNotificationAPI, 1); });

            string line;
            do {
                line = Console.ReadLine();
                Console.Clear();
            } while (line != "q");

            // cleanup
            Dokan.RemoveMountPoint(MountPath);
            Directory.Delete(MountPath);
        }
    }
}
