using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace ProjectsManager
{
    public class Trottle<TArgs> : IDisposable
    {
        [NotNull]
        private readonly ConcurrentQueue<TArgs> _Args = new ConcurrentQueue<TArgs>();

        [NotNull]
        private readonly Task _Task;

        [NotNull]
        private readonly CancellationTokenSource _Token = new CancellationTokenSource();

        public Trottle([NotNull] Action<TArgs> action, TimeSpan interval) {
            _Task = Task.Factory.StartNew(() => {
                try {
                    while (true) {
                        TArgs args;
                        if (_Args.TryDequeue(out args)) {
                            action(args);
                        }

                        Thread.Sleep(interval);
                    }
                } catch (ThreadAbortException) {
                    // NOOP
                }
            }, _Token.Token);
        }

        public void Execute(TArgs args) {
            if (!_Args.Contains(args)) {
                _Args.Enqueue(args);
            }
        }

        public void Dispose() {
            _Token.Cancel();
            _Task.Wait(TimeSpan.FromMilliseconds(50));
            _Token.Dispose();
        }
    }
}
