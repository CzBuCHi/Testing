using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Testing.Services
{
    public interface IProject
    {
        Task Pull();
    }

    public delegate void WatcherChangeDelegate(WatcherChangeType changeType, [NotNull] string fullPath, string oldPath = null);

    public class Project : IProject
    {
        [NotNull]
        private readonly string _ApplicationIdentifier;

        [NotNull]
        private readonly FtpClient _Ftp;

        [NotNull]
        private readonly string _BasePath;

        [NotNull]
        private readonly string _MinePath;

        [NotNull]
        private readonly string _ServerPath;

        public Project([NotNull] string applicationIdentifier) {
            _ApplicationIdentifier = applicationIdentifier;

            string host = "127.0.0.1";
            _Ftp = new FtpClient(host, "test", "test");

            _BasePath = Path.Combine(@"c:\projects\Testing\test\Base", host);
            if (!Directory.Exists(_BasePath)) {
                Directory.CreateDirectory(_BasePath);
            }

            _MinePath = Path.Combine(@"c:\projects\Testing\test\Mine", host);
            if (!Directory.Exists(_MinePath)) {
                Directory.CreateDirectory(_MinePath);
            }

            _ServerPath = Path.Combine(@"c:\projects\Testing\test\Server", host);
            if (!Directory.Exists(_ServerPath)) {
                Directory.CreateDirectory(_ServerPath);
            }
        }

        public async Task Pull() {
            IReadOnlyCollection<FtpFileSystemInfo> list = await _Ftp.ListDirectoryTreeAsync("/" + _ApplicationIdentifier + "/html");
            string htmlPath = Path.Combine(_BasePath, _ApplicationIdentifier, "html");

            int prefix = _BasePath.Length;

            // sync directories to match server structure
            FtpDirectoryInfo[] ftpDirs = list.OfType<FtpDirectoryInfo>().ToArray();
            DirectoryInfo[] localDirs = new DirectoryInfo(htmlPath).GetDirectories("*", SearchOption.AllDirectories);
            IEnumerable<Tuple<FtpDirectoryInfo, DirectoryInfo>> joinDirs =
                ftpDirs.FullOuterJoin(localDirs,
                            o => o.FullName,
                            o => o.FullName.Substring(prefix).Replace('\\', '/'),
                            (ftp, local, key) => new { key, data = Tuple.Create(ftp, local) })
                       .OrderBy(o => o.key.Length)
                       .Select(o => o.data);

            foreach (Tuple<FtpDirectoryInfo, DirectoryInfo> pair in joinDirs) {
                Debug.Assert(pair != null, nameof(pair) + " != null");
                if (pair.Item1 == null) {
                    Debug.Assert(pair.Item2 != null, "pair.Item2 != null");
                    pair.Item2.Delete(true);
                } else {
                    string localPath = _BasePath + pair.Item1.FullName.Replace('/', '\\');
                    if (pair.Item2 == null) {
                        Directory.CreateDirectory(localPath);
                    }

                    Directory.SetLastWriteTime(localPath, pair.Item1.Modify);
                }
            }


            // sync files to match server, override local
            FtpFileInfo[] ftpFiles = list.OfType<FtpFileInfo>().ToArray();
            FileInfo[] localFiles = new DirectoryInfo(htmlPath).GetFiles("*", SearchOption.AllDirectories);
            IEnumerable<Tuple<FtpFileInfo, FileInfo>> joinFiles =
                ftpFiles.FullOuterJoin(localFiles,
                    o => o.FullName,
                    o => o.FullName.Substring(prefix).Replace('\\', '/'),
                    (ftp, local, key) => Tuple.Create(ftp, local)
                );

            // paraell foreach, max threads: 10
            SemaphoreSlim semaphore = new SemaphoreSlim(10);
            IEnumerable<Task> tasks = joinFiles.Select(async pair => {
                Debug.Assert(pair != null, nameof(pair) + " != null");

                // ReSharper disable once AccessToDisposedClosure
                await semaphore.WaitAsync();
                try {
                    if (pair.Item1 == null) {
                        Debug.Assert(pair.Item2 != null, "pair.Item2 != null");
                        pair.Item2.Delete();
                    } else {
                        bool download = true;
                        if (pair.Item2 != null) {
                            if (pair.Item1.Modify == pair.Item2.LastWriteTime) {
                                // skip downloading of unchanged files
                                download = false;
                            } else {
                                pair.Item2.Delete();
                            }
                        }

                        if (download) {
                            string localPath = _BasePath + pair.Item1.FullName.Replace('/', '\\');
                            await _Ftp.DownloadFileAsync(pair.Item1.FullName, localPath);
                            File.SetLastWriteTime(localPath, pair.Item1.Modify);
                        }
                    }
                } finally {
                    // ReSharper disable once AccessToDisposedClosure
                    semaphore.Release();
                }
            });

            Task.WhenAll(tasks).Wait();
        }

        public void Watch() {
            Watcher watcher = new Watcher(_MinePath);
            watcher.Change += OnWatcherOnChange;
            watcher.EnableRaisingEvents = true;
        }

        private void OnWatcherOnChange(WatcherChangeType type, string path, string oldPath) {
            switch (type) {
                case WatcherChangeType.Created: {
                    Console.WriteLine("Created: " + path);
                    break;
                }
                case WatcherChangeType.Deleted: {
                    Console.WriteLine("Deleted: " + path);
                        break;
                }
                case WatcherChangeType.Changed: {
                    Console.WriteLine("Changed: " + path);
                        break;
                }
                case WatcherChangeType.Renamed: {
                    Console.WriteLine("Renamed: " + path + " | " + oldPath);
                        break;
                }
                default: {
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
                }
            }
        }
    }

    public class Watcher : IDisposable
    {
        private const int CacheTimeMilliseconds = 250;

        [NotNull]
        private readonly CacheItemPolicy _CacheItemPolicy;

        [NotNull]
        private readonly MemoryCache _MemCache = MemoryCache.Default;

        [NotNull]
        private readonly FileSystemWatcher _Watcher;

        public Watcher([NotNull] string fullPath) {
            _Watcher = new FileSystemWatcher(fullPath);
            _Watcher.IncludeSubdirectories = true;
            _Watcher.Changed += WatcherOnChanged;
            _Watcher.Created += WatcherOnChanged;
            _Watcher.Deleted += WatcherOnChanged;
            _Watcher.Error += WatcherOnError;
            _Watcher.Renamed += WatcherOnRenamed;

            _CacheItemPolicy = new CacheItemPolicy {
                RemovedCallback = OnRemovedFromCache
            };
        }

        public event WatcherChangeDelegate Change;

        public bool EnableRaisingEvents {
            get { return _Watcher.EnableRaisingEvents; }
            set { _Watcher.EnableRaisingEvents = value; }
        }

        public void Dispose() {
            _Watcher.EnableRaisingEvents = false;
            _Watcher.Changed -= WatcherOnChanged;
            _Watcher.Created -= WatcherOnChanged;
            _Watcher.Deleted -= WatcherOnChanged;
            _Watcher.Error -= WatcherOnError;
            _Watcher.Renamed -= WatcherOnRenamed;
            _Watcher.Dispose();
        }

        private static WatcherChangeType GetWatcherChangeType(WatcherChangeTypes types) {
            switch (types) {
                case WatcherChangeTypes.Created: return WatcherChangeType.Created;
                case WatcherChangeTypes.Deleted: return WatcherChangeType.Deleted;
                case WatcherChangeTypes.Changed: return WatcherChangeType.Changed;
                case WatcherChangeTypes.Renamed: return WatcherChangeType.Renamed;
                default:                         throw new ArgumentOutOfRangeException(nameof(types), types, null);
            }
        }

        private void OnChange(WatcherChangeType changetype, [NotNull] string fullpath, string oldpath) {
            Change?.Invoke(changetype, fullpath, oldpath);
        }

        private void OnRemovedFromCache([NotNull] CacheEntryRemovedArguments args) {
            if (args.RemovedReason != CacheEntryRemovedReason.Expired) {
                return;
            }

            Debug.Assert(args.CacheItem?.Value != null, "args.CacheItem?.Value != null");
            FileSystemEventArgs changeInfo = (FileSystemEventArgs) args.CacheItem.Value;

            if (changeInfo.ChangeType == WatcherChangeTypes.Renamed) {
                RenamedEventArgs renamed = (RenamedEventArgs) changeInfo;
                OnChange(GetWatcherChangeType(changeInfo.ChangeType), renamed.FullPath, renamed.OldFullPath);
            } else {
                OnChange(GetWatcherChangeType(changeInfo.ChangeType), changeInfo.FullPath, null);
            }
        }

        private static void WatcherOnError(object sender, [NotNull] ErrorEventArgs args) {
            throw args.GetException() ?? new Exception("Unknown FileSystemWatcher error.");
        }

        private void WatcherOnChanged(object sender, [NotNull] FileSystemEventArgs args) {
            _CacheItemPolicy.AbsoluteExpiration = DateTimeOffset.Now.AddMilliseconds(CacheTimeMilliseconds);
            _MemCache.AddOrGetExisting(args.ChangeType + "_" + args.Name, args, _CacheItemPolicy);
        }

        private void WatcherOnRenamed(object sender, [NotNull] RenamedEventArgs args) {
            _CacheItemPolicy.AbsoluteExpiration = DateTimeOffset.Now.AddMilliseconds(CacheTimeMilliseconds);
            _MemCache.AddOrGetExisting(args.ChangeType + "_" + args.Name, args, _CacheItemPolicy);
        }
    }

    public enum WatcherChangeType
    {
        Created,
        Deleted,
        Changed,
        Renamed
    }
}
