using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace TargetMarkerOverlay
{
    public static class PluginSettingsSerializer
    {
        public static byte[] Serialize(PluginSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            lock (settings)
            {
                using (var stream = new MemoryStream())
                {
                    new XmlSerializer(typeof(PluginSettings)).Serialize(stream, settings);
                    return stream.ToArray();
                }
            }
        }

        public static PluginSettings Deserialize(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            using (var stream = new MemoryStream(data, false))
            {
                var settings = (PluginSettings)new XmlSerializer(typeof(PluginSettings)).Deserialize(stream);
                settings.Normalize();
                return settings;
            }
        }
    }

    public sealed class SettingsPersistenceCoordinator
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

        private readonly object sync = new object();
        private readonly SemaphoreSlim saveGate = new SemaphoreSlim(1, 1);
        private readonly string path;
        private readonly TimeSpan interval;
        private readonly Func<string, byte[], Task> writer;
        private readonly Action<Exception> errorCallback;
        private readonly System.Threading.Timer timer;
        private byte[] currentSnapshot;
        private byte[] lastSavedSnapshot;
        private Task periodicTask;
        private Task<bool> shutdownTask;
        private bool stopping;
        private bool disposed;
        private Exception lastError;

        public SettingsPersistenceCoordinator(
            string path,
            byte[] currentSnapshot,
            byte[] lastSavedSnapshot,
            TimeSpan? interval = null,
            Func<string, byte[], Task> writer = null,
            Action<Exception> errorCallback = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A settings path is required.", nameof(path));
            this.path = path;
            this.interval = interval ?? DefaultInterval;
            if (this.interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
            this.writer = writer ?? AtomicSettingsFile.WriteAsync;
            this.errorCallback = errorCallback;
            this.currentSnapshot = Clone(currentSnapshot);
            this.lastSavedSnapshot = Clone(lastSavedSnapshot);
            timer = new System.Threading.Timer(OnTimer, null, this.interval, this.interval);
        }

        public Exception LastError
        {
            get { lock (sync) return lastError; }
        }

        public bool IsDirty
        {
            get { lock (sync) return !Equal(currentSnapshot, lastSavedSnapshot); }
        }

        public void UpdateSnapshot(byte[] snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            lock (sync)
            {
                if (stopping || disposed) return;
                currentSnapshot = Clone(snapshot);
            }
        }

        public Task<bool> SaveIfChangedAsync()
        {
            return SaveIfChangedCoreAsync(false);
        }

        public Task<bool> StopAndFlushAsync(byte[] finalSnapshot)
        {
            if (finalSnapshot == null) throw new ArgumentNullException(nameof(finalSnapshot));
            Task pending;
            lock (sync)
            {
                if (shutdownTask != null) return shutdownTask;
                if (disposed) return Task.FromResult(lastError == null);
                stopping = true;
                pending = periodicTask;
                timer.Change(Timeout.Infinite, Timeout.Infinite);
                shutdownTask = StopAndFlushCoreAsync(pending, finalSnapshot);
                return shutdownTask;
            }
        }

        private async Task<bool> StopAndFlushCoreAsync(Task pending, byte[] finalSnapshot)
        {
            using (var callbacksStopped = new ManualResetEvent(false))
            {
                if (timer.Dispose(callbacksStopped)) callbacksStopped.WaitOne();
            }

            if (pending != null) await pending.ConfigureAwait(false);
            lock (sync) currentSnapshot = Clone(finalSnapshot);

            var saved = await SaveIfChangedCoreAsync(true).ConfigureAwait(false);
            lock (sync) disposed = true;
            saveGate.Dispose();
            return saved;
        }

        private void OnTimer(object state)
        {
            lock (sync)
            {
                if (stopping || disposed || (periodicTask != null && !periodicTask.IsCompleted)) return;
                periodicTask = Task.Run(async () => await SaveIfChangedCoreAsync(false).ConfigureAwait(false));
            }
        }

        private async Task<bool> SaveIfChangedCoreAsync(bool allowStopping)
        {
            await saveGate.WaitAsync().ConfigureAwait(false);
            try
            {
                byte[] snapshot;
                lock (sync)
                {
                    if (disposed || (stopping && !allowStopping)) return true;
                    snapshot = Clone(currentSnapshot);
                    if (Equal(snapshot, lastSavedSnapshot))
                    {
                        lastError = null;
                        return true;
                    }
                }

                try
                {
                    await writer(path, snapshot).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    lock (sync) lastError = ex;
                    try { errorCallback?.Invoke(ex); } catch { }
                    return false;
                }

                lock (sync)
                {
                    lastSavedSnapshot = snapshot;
                    lastError = null;
                }
                return true;
            }
            finally
            {
                saveGate.Release();
            }
        }

        private static byte[] Clone(byte[] value) => value == null ? null : (byte[])value.Clone();

        private static bool Equal(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
    }

    public static class AtomicSettingsFile
    {
        public static Task WriteAsync(string path, byte[] contents)
        {
            var copy = (byte[])contents.Clone();
            return Task.Run(() => Write(path, copy));
        }

        public static void Write(string path, byte[] contents)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("Settings directory is unavailable.");
            Directory.CreateDirectory(directory);
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            var backup = path + ".bak-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(contents, 0, contents.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Replace(temp, path, backup);
                    try { if (File.Exists(backup)) File.Delete(backup); } catch { }
                }
                else File.Move(temp, path);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
    }
}
