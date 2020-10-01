using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using ProjectsManager.ProjectsManagerService;

namespace ProjectsManager
{
    public class ProjectManager : IDisposable
    {
        [NotNull]
        private readonly HostingWebApplicationInfoComplex _ApplicationInfo;

        [NotNull]
        private readonly Configuration _Configuration;

        [NotNull]
        private readonly FtpClient _FtpClient;

        [NotNull]
        private readonly string _LocalPath;

        [NotNull]
        private readonly FileSystemWatcher _Watcher;
        
        public ProjectManager([NotNull] HostingWebApplicationInfoComplex applicationInfo, [NotNull] Configuration configuration) {
            _OnChanged = new Trottle<string>(OnChanged, TimeSpan.FromMilliseconds(50));

            _ApplicationInfo = applicationInfo;
            _Configuration = configuration;
            _FtpClient = new FtpClient(_ApplicationInfo.ServerInfo.BaseAddress, _Configuration.FtpUserName, _Configuration.FtpPassword);
            _LocalPath = Path.Combine(_Configuration.LocalPath, _ApplicationInfo.ApplicationInfo.Identifier, "html");

            if (!Directory.Exists(_LocalPath)) {
                Directory.CreateDirectory(_LocalPath);
            }

            _Watcher = new FileSystemWatcher(_LocalPath) {
                IncludeSubdirectories = true
            };
            _Watcher.Changed += OnWatcherOnChanged;
            _Watcher.Created += OnWatcherOnCreated;
            _Watcher.Deleted += OnWatcherOnDeleted;
            _Watcher.Error += OnWatcherOnError;
            _Watcher.Renamed += OnWatcherOnRenamed;
        }

        public bool Watch {
            get { return _Watcher.EnableRaisingEvents; }
            set { _Watcher.EnableRaisingEvents = value; }
        }

        public void Dispose() {
            _OnChanged.Dispose();

            _Watcher.EnableRaisingEvents = false;
            _Watcher.Changed -= OnWatcherOnChanged;
            _Watcher.Created -= OnWatcherOnCreated;
            _Watcher.Deleted -= OnWatcherOnDeleted;
            _Watcher.Error -= OnWatcherOnError;
            _Watcher.Renamed -= OnWatcherOnRenamed;
            _Watcher.Dispose();
        }

        public async Task Download() {
            string applicationInfoIdentifier = "/" + _ApplicationInfo.ApplicationInfo.Identifier + "/html";

            // get all files & directories on server
            Dictionary<string, FtpFileSystemInfo> ftpInfos = new Dictionary<string, FtpFileSystemInfo>();
            await foreach (FtpFileSystemInfo info in ListFtpDirectoryAsync(applicationInfoIdentifier)) {
                ftpInfos.Add(info.FullPath, info);
            }

            // deletes all files / directories, that arent on server
            ClearUnknown(ftpInfos);

            // create directory structure
            foreach (FtpDirectoryInfo info in ftpInfos.Values.OfType<FtpDirectoryInfo>()) {
                string fullPath = _Configuration.LocalPath + "\\" + info.FullPath.Substring(1).Replace("/", "\\");
                if (!Directory.Exists(fullPath)) {
                    Directory.CreateDirectory(fullPath);
                }
            }

            string[] diffExts = { ".cs", ".cshtml", ".aspx", ".ascx", ".js", ".css", ".scss", ".config" };

            // download files
            double totalSize = ftpInfos.Values.OfType<FtpFileInfo>().Sum(o => (double) o.Size);
            ulong downloadedSize = 0;
            foreach (FtpFileInfo info in ftpInfos.Values.OfType<FtpFileInfo>()) {
                string fullPath = _Configuration.LocalPath + "\\" + info.FullPath.Substring(1).Replace("/", "\\");

                FileInfo localInfo = new FileInfo(fullPath);
                if (localInfo.Exists) {
                    if (localInfo.LastWriteTime == info.Modify) {
                        // skip if exists & same last write time
                        continue;
                    }

                    if (diffExts.Any(info.Name.EndsWith)) {
                        // client & server file diff -> show TortoiseGitMerge
                        string serverName;
                        string localName;
                        if (localInfo.LastWriteTime > info.Modify) {
                            localName = "Local (newer)";
                            serverName = "Server";
                        } else {
                            localName = "Local";
                            serverName = "Server (newer)";
                        }

                        string diffFullPath = _Configuration.DiffPath + "\\" + info.Name + "." + DateTime.Now.Ticks;
                        await _FtpClient.DownloadFileAsync(info.FullPath, diffFullPath);

                        Process process = Process.Start(_Configuration.TortoiseGitMerge, $@"/base ""{diffFullPath}"" /basename ""{serverName}"" /mine ""{fullPath}"" /minename ""{localName}"" /saverequired");
                        process.WaitForExit();
                        File.Delete(diffFullPath);
                        continue;
                    }

                    Console.WriteLine("Ignored change: " + fullPath);
                    continue;
                }

                await _FtpClient.DownloadFileAsync(info.FullPath, fullPath);
                File.SetLastWriteTime(fullPath, info.Modify);

                Console.Write("\rDownloading: " + downloadedSize + " / " + totalSize + " ...");
                downloadedSize += info.Size;
            }
        }

        private void ClearUnknown([NotNull] Dictionary<string, FtpFileSystemInfo> ftpInfos) {
            Dictionary<string, FileSystemInfo> localInfos =
                new DirectoryInfo(_LocalPath)
                   .EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
                   .ToDictionary(o => o.FullName.Substring(_Configuration.LocalPath.Length).Replace("\\", "/"));

            foreach (KeyValuePair<string, FileSystemInfo> local in localInfos) {
                Debug.Assert(local.Key != null, "local.Key != null");
                Debug.Assert(local.Value != null, "local.Value != null");

                local.Value.Refresh();
                if (local.Value.Exists && !ftpInfos.ContainsKey(local.Key)) {
                    if (local.Value is DirectoryInfo directoryInfo) {
                        directoryInfo.Delete(true);
                    } else {
                        local.Value.Delete();
                    }
                }
            }
        }

        [NotNull]
        private async IAsyncEnumerable<FtpFileSystemInfo> ListFtpDirectoryAsync([NotNull] string remotePath) {
            // get children
            IAsyncEnumerable<FtpFileSystemInfo> list = _FtpClient.ListDirectoryAsync(remotePath);

            Queue<FtpDirectoryInfo> dirs = new Queue<FtpDirectoryInfo>();

            await foreach (FtpFileSystemInfo info in list) {
                info.ParentPath = remotePath;
                yield return info;

                if (info is FtpDirectoryInfo directoryInfo) {
                    dirs.Enqueue(directoryInfo);
                }
            }

            // get descendants
            while (dirs.Count > 0) {
                FtpDirectoryInfo dir = dirs.Dequeue();

                if (dir.Name == "ewobjects") {
                    continue;
                }

                IAsyncEnumerable<FtpFileSystemInfo> subList = _FtpClient.ListDirectoryAsync(dir.FullPath);
                await foreach (FtpFileSystemInfo info in subList) {
                    info.ParentPath = dir.FullPath;
                    yield return info;

                    if (info is FtpDirectoryInfo directoryInfo) {
                        dirs.Enqueue(directoryInfo);
                    }
                }
            }
        }

        private void OnCreated(string fullPath) {
            Console.WriteLine("Create: " + fullPath);
        }

        private void OnDeleted(string fullPath) {
            Console.WriteLine("Delete: " + fullPath);
        }

        private void OnChanged(string fullPath) {
            Console.WriteLine("Change: " + fullPath);
        }

        private void OnRenamed(string oldFullPath, string fullPath) {
            Console.WriteLine("Rename: " + oldFullPath + " => " + fullPath);
        }

        private void OnWatcherOnCreated(object sender, [NotNull] FileSystemEventArgs args) {
            OnCreated(args.FullPath);
        }

        private void OnWatcherOnDeleted(object sender, [NotNull] FileSystemEventArgs args) {
            OnDeleted(args.FullPath);
        }

        private void OnWatcherOnError(object sender, [NotNull] ErrorEventArgs args) {
            throw args.GetException() ?? new Exception("FileSystemWatcher: Unknown error");
        }

        [NotNull]
        private readonly Trottle<string> _OnChanged;

        private void OnWatcherOnChanged(object sender, [NotNull] FileSystemEventArgs args) {
            _OnChanged.Execute(args.FullPath);
        }

        private void OnWatcherOnRenamed(object sender, [NotNull] RenamedEventArgs args) {
            OnRenamed(args.OldFullPath, args.FullPath);
        }
    }
}
