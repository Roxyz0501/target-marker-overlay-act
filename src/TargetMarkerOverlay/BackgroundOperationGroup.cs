using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TargetMarkerOverlay
{
    public sealed class BackgroundOperationGroup
    {
        private readonly object sync = new object();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly HashSet<Task> tasks = new HashSet<Task>();
        private bool stopping;
        private Task stopTask;

        public bool TryStart(Func<CancellationToken, Task> operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            lock (sync)
            {
                if (stopping) return false;
                tasks.RemoveWhere(task => task.IsCompleted);
                var token = cancellation.Token;
                tasks.Add(Task.Run(async () => await operation(token).ConfigureAwait(false)));
                return true;
            }
        }

        public Task StopAndWaitAsync()
        {
            lock (sync)
            {
                if (stopTask != null) return stopTask;
                stopping = true;
                var pending = tasks.ToArray();
                stopTask = Task.Run(async () =>
                {
                    try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
                    try { await Task.WhenAll(pending).ConfigureAwait(false); }
                    catch { }
                    finally { cancellation.Dispose(); }
                });
                return stopTask;
            }
        }
    }
}
