using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Timers;
using JetBrains.Annotations;
using Zeus;

namespace Testing.Services
{
    public class ChangeWatcher : IDisposable
    {
        public event ChangeWatcherDelegate Change;

        [NotNull]
        private readonly Dictionary<string, FileSystemWatcher> _Watchers = new Dictionary<string, FileSystemWatcher>();
        public void Start([NotNull] string fullPath) {
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
            watcher.Created += WatcherOnCreated;
            watcher.Deleted += WatcherOnDeleted;
            watcher.Error += WatcherOnError;
            watcher.Renamed += WatcherOnRenamed;
            watcher.EnableRaisingEvents = true;
        }

        public void Stop([NotNull] string fullPath) {
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
            watcher.Created -= WatcherOnCreated;
            watcher.Deleted -= WatcherOnDeleted;
            watcher.Error -= WatcherOnError;
            watcher.Renamed -= WatcherOnRenamed;
            watcher.Dispose();
        }

        void IDisposable.Dispose() {
            foreach (string fullPath in _Watchers.Keys) {
                Debug.Assert(fullPath != null, nameof(fullPath) + " != null");
                Stop(fullPath);
            }
        }

        private void WatcherOnCreated(object sender, [NotNull] FileSystemEventArgs args) {
            throw new NotImplementedException();
        }

        private void WatcherOnDeleted(object sender, [NotNull] FileSystemEventArgs args) {
            throw new NotImplementedException();
        }

        private void WatcherOnError(object sender, [NotNull] ErrorEventArgs args) {
            throw args.GetException() ?? new Exception("FileSystemWatcher unknown error.");
        }

        private void WatcherOnChanged(object sender, [NotNull] FileSystemEventArgs args) {
            throw new NotImplementedException();
        }

        private void WatcherOnRenamed(object sender, [NotNull] RenamedEventArgs args) {
            throw new NotImplementedException();
        }
    }

    public delegate void ChangeWatcherDelegate(ChangeWatcherChangeType changeType, [NotNull] string fullPath, string oldFullPaath = null);

    public enum ChangeWatcherChangeType
    {
        Created,
        Deleted,
        Changed,
        Renamed,
    }

    /// <summary>
    /// Provides Debounce() and Throttle() methods.
    /// Use these methods to ensure that events aren't handled too frequently.
    /// 
    /// Throttle() ensures that events are throttled by the interval specified.
    /// Only the last event in the interval sequence of events fires.
    /// 
    /// Debounce() fires an event only after the specified interval has passed
    /// in which no other pending event has fired. Only the last event in the
    /// sequence is fired.
    /// </summary>
    public class EventUtils
    {
        private Timer timer;

        /// <summary>
        /// Debounce an event by resetting the event timeout every time the event is 
        /// fired. The behavior is that the Action passed is fired only after events
        /// stop firing for the given timeout period.
        /// 
        /// Use Debounce when you want events to fire only after events stop firing
        /// after the given interval timeout period.
        /// 
        /// Wrap the logic you would normally use in your event code into
        /// the  Action you pass to this method to debounce the event.
        /// Example: https://gist.github.com/RickStrahl/0519b678f3294e27891f4d4f0608519a
        /// </summary>
        /// <param name="interval">Timeout in Milliseconds</param>
        /// <param name="action">Action[object] to fire when debounced event fires</param>
        /// <param name="param">optional parameter</param>
        public void Debounce(double interval, [NotNull] Action<object> action, object param = null) {
            // kill pending timer and pending ticks
            if (timer != null) {
                timer.Stop();
                timer.Dispose();
                timer = null;
            }

            // timer is recreated for each event and effectively
            // resets the timeout. Action only fires after timeout has fully
            // elapsed without other events firing in between
            timer = new Timer(interval);
            timer.Start();
            timer.Elapsed += delegate {
                timer?.Stop();
                timer = null;
                action.Invoke(param);
            };
        }
    }
}
