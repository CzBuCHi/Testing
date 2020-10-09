using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Caching;
using JetBrains.Annotations;

namespace Testing.Services
{
    public interface IChangesWatcher
    {
        event ChangesWatcherChangeDelegate Change;
        void Start([NotNull] string fullPath);
        void Stop([NotNull] string fullPath);
    }

    public delegate void ChangesWatcherChangeDelegate(ChangesWatcherChangeType changeType, [NotNull] string fullPath, string oldPath = null);

    public class ChangesWatcher : IDisposable, IChangesWatcher
    {
        private const int CacheTimeMilliseconds = 250;

        [NotNull]
        private readonly CacheItemPolicy _CacheItemPolicy;

        [NotNull]
        private readonly MemoryCache _MemCache = MemoryCache.Default;

        [NotNull]
        private readonly Dictionary<string, FileSystemWatcher> _Watchers = new Dictionary<string, FileSystemWatcher>();

        public ChangesWatcher() {
            _CacheItemPolicy = new CacheItemPolicy {
                RemovedCallback = OnRemovedFromCache
            };
        }

        public event ChangesWatcherChangeDelegate Change;

        public void Start(string fullPath) {
            FileSystemWatcher watcher;
            lock (_Watchers) {
                if (_Watchers.ContainsKey(fullPath)) {
                    return;
                }

                watcher = new FileSystemWatcher(fullPath);
                _Watchers.Add(fullPath, watcher);
            }

            watcher.IncludeSubdirectories = true;
            watcher.Changed += WatcherOnChanged;
            watcher.Created += WatcherOnChanged;
            watcher.Deleted += WatcherOnChanged;
            watcher.Error += WatcherOnError;
            watcher.Renamed += WatcherOnRenamed;
            watcher.EnableRaisingEvents = true;
        }

        public void Stop(string fullPath) {
            FileSystemWatcher watcher;
            lock (_Watchers) {
                if (!_Watchers.TryGetValue(fullPath, out watcher)) {
                    return;
                }

                _Watchers.Remove(fullPath);
            }

            Debug.Assert(watcher != null, nameof(watcher) + " != null");
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= WatcherOnChanged;
            watcher.Created -= WatcherOnChanged;
            watcher.Deleted -= WatcherOnChanged;
            watcher.Error -= WatcherOnError;
            watcher.Renamed -= WatcherOnRenamed;
            watcher.Dispose();
        }

        private static ChangesWatcherChangeType GetChangeType(WatcherChangeTypes types) {
            switch (types) {
                case WatcherChangeTypes.Created: return ChangesWatcherChangeType.Created;
                case WatcherChangeTypes.Deleted: return ChangesWatcherChangeType.Deleted;
                case WatcherChangeTypes.Changed: return ChangesWatcherChangeType.Changed;
                case WatcherChangeTypes.Renamed: return ChangesWatcherChangeType.Renamed;
                default:                         throw new ArgumentOutOfRangeException(nameof(types), types, null);
            }
        }

        private static void WatcherOnError(object sender, [NotNull] ErrorEventArgs args) {
            throw args.GetException() ?? new Exception("Unknown FileSystemWatcher error.");
        }

        void IDisposable.Dispose() {
            foreach (string fullPath in _Watchers.Keys.ToArray()) {
                Debug.Assert(fullPath != null, nameof(fullPath) + " != null");
                Stop(fullPath);
            }
        }

        private void OnChange(ChangesWatcherChangeType changetype, string fullpath, string oldpath) {
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
                OnChange(GetChangeType(changeInfo.ChangeType), renamed.FullPath, renamed.OldFullPath);
            } else {
                OnChange(GetChangeType(changeInfo.ChangeType), changeInfo.FullPath, null);
            }
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

    public enum ChangesWatcherChangeType
    {
        Created,
        Deleted,
        Changed,
        Renamed
    }
}
