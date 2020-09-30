using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            _Watcher.EnableRaisingEvents = false;
            _Watcher.Changed -= OnWatcherOnChanged;
            _Watcher.Created -= OnWatcherOnCreated;
            _Watcher.Deleted -= OnWatcherOnDeleted;
            _Watcher.Error -= OnWatcherOnError;
            _Watcher.Renamed -= OnWatcherOnRenamed;
            _Watcher.Dispose();
        }

        public void Download() {
            string applicationInfoIdentifier = "/" + _ApplicationInfo.ApplicationInfo.Identifier + "/html";

            // get all files & directories on server
            Dictionary<string, FtpFileSystemInfo> ftpInfos = ListFtpDirectory(applicationInfoIdentifier).ToDictionary(o => o.FullPath);

            // deletes all files / directories, that arent on server
            ClearUnknown(ftpInfos);

            // create directory structure
            foreach (FtpDirectoryInfo info in ftpInfos.Values.OfType<FtpDirectoryInfo>()) {
                string fullPath = _Configuration.LocalPath + "\\" + info.FullPath.Substring(1).Replace("/", "\\");
                if (!Directory.Exists(fullPath)) {
                    Directory.CreateDirectory(fullPath);
                }
            }

            // download files
            foreach (FtpFileInfo info in ftpInfos.Values.OfType<FtpFileInfo>()) {
                string fullPath = _Configuration.LocalPath + "\\" + info.FullPath.Substring(1).Replace("/", "\\");

                FileInfo localInfo = new FileInfo(fullPath);
                if (localInfo.Exists) {
                    if (localInfo.LastWriteTime  == info.Modify) {
                        // skip if exists & same last write time
                        continue;
                    }

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
                    _FtpClient.DownloadFile(info.FullPath, diffFullPath);
                    
                    Process process = Process.Start(_Configuration.TortoiseGitMerge, $@"/base ""{diffFullPath}"" /basename ""{serverName}"" /mine ""{fullPath}"" /minename ""{localName}"" /saverequired");
                    process.WaitForExit();
                    File.Delete(diffFullPath);
                    continue;
                }

                _FtpClient.DownloadFile(info.FullPath, fullPath);
                File.SetLastWriteTime(fullPath, info.Modify);
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
        [ItemNotNull]
        private IEnumerable<FtpFileSystemInfo> ListFtpDirectory([NotNull] string remotePath) {
            IEnumerable<FtpFileSystemInfo> list = _FtpClient.ListDirectory(remotePath);
            Stack<FtpDirectoryInfo> dirs = new Stack<FtpDirectoryInfo>();

            foreach (FtpFileSystemInfo info in list) {
                info.ParentPath = remotePath;
                yield return info;

                if (info is FtpDirectoryInfo directoryInfo) {
                    dirs.Push(directoryInfo);
                }
            }

            while (dirs.Count > 0) {
                FtpDirectoryInfo dir = dirs.Pop();

                // skip ewobjects
                if (dir.Name == "ewobjects") {
                    continue;
                }

                IEnumerable<FtpFileSystemInfo> subList = _FtpClient.ListDirectory(dir.FullPath);
                foreach (FtpFileSystemInfo info in subList) {
                    info.ParentPath = dir.FullPath;
                    yield return info;

                    if (info is FtpDirectoryInfo directoryInfo) {
                        dirs.Push(directoryInfo);
                    }
                }
            }
        }

        private void OnWatcherOnCreated(object sender, [NotNull] FileSystemEventArgs args) {
            Console.WriteLine("Create: " + args.FullPath);
        }

        private void OnWatcherOnDeleted(object sender, [NotNull] FileSystemEventArgs args) {
            Console.WriteLine("Delete: " + args.FullPath);
        }

        private void OnWatcherOnError(object sender, [NotNull] ErrorEventArgs args) {
            throw args.GetException() ?? new Exception("FileSystemWatcher: Unknown error");
        }

        private void OnWatcherOnChanged(object sender, [NotNull] FileSystemEventArgs args) {
            Console.WriteLine("Change: " + args.FullPath);
        }

        private void OnWatcherOnRenamed(object sender, [NotNull] RenamedEventArgs args) {
            Console.WriteLine("Rename: " + args.OldFullPath + " => " + args.FullPath);
        }
    }
}
