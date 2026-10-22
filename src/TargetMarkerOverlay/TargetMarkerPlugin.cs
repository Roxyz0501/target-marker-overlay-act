using Advanced_Combat_Tracker;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TargetMarkerOverlay
{
    public sealed class TargetMarkerPlugin : IActPluginV1
    {
        private readonly MarkerStateTracker tracker = new MarkerStateTracker();
        private PluginSettings settings;
        private SettingsControl control;
        private OverlayForm overlay;
        private Label status;
        private string settingsPath;
        private GitHubUpdateService updateService;
        private ReleaseInfo availableRelease;
        private SettingsPersistenceCoordinator settingsPersistence;
        private readonly object lifecycleSync = new object();
        private readonly BackgroundOperationGroup backgroundOperations = new BackgroundOperationGroup();
        private readonly ManualResetEventSlim logCallbacksDrained = new ManualResetEventSlim(true);
        private int activeLogCallbacks;
        private int updateCheckActive;
        private int updateDownloadActive;
        private bool stopping;

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            status = pluginStatusText;
            settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Advanced Combat Tracker", "Config", "TargetMarkerOverlay.xml");
            settings = LoadSettings();
            var lastKnownSettings = PluginSettingsSerializer.Serialize(settings);
            var initializedLanguage = string.IsNullOrWhiteSpace(settings.Language);
            if (initializedLanguage) settings.Language = Localization.FromUiCulture(CultureInfo.CurrentUICulture);
            settings.Language = Localization.Normalize(settings.Language);
            settings.Normalize();
            settingsPersistence = new SettingsPersistenceCoordinator(
                settingsPath,
                PluginSettingsSerializer.Serialize(settings),
                lastKnownSettings,
                errorCallback: ex => Ui(() =>
                {
                    if (status != null) status.Text = "Target Marker Overlay: " + Localization.Get(settings?.Language, "SaveError", ex.Message);
                }));
            tracker.Language = settings.Language;
            control = new SettingsControl(settings, tracker);
            control.SettingsChanged += OnSettingsChanged;
            control.CheckForUpdatesRequested += OnCheckForUpdatesRequested;
            control.InstallUpdateRequested += OnInstallUpdateRequested;
            control.PostponeUpdateRequested += OnPostponeUpdateRequested;
            pluginScreenSpace.Text = "Target Marker Overlay";
            pluginScreenSpace.Controls.Add(control);

            overlay = new OverlayForm(settings, tracker);
            overlay.BoundsChangedByUser += OnOverlayBoundsChanged;
            ActGlobals.oFormActMain.OnLogLineRead += OnLogLineRead;
            overlay.Show();
            overlay.ApplySettings(settings, true);
            status.Text = "Target Marker Overlay: " + Localization.Get(settings.Language, "StatusReady");
            updateService = new GitHubUpdateService();
            if (initializedLanguage) CaptureSettingsSnapshot();
            if (settings.CheckUpdatesOnStartup) StartUpdateCheck(false);
        }

        private void OnLogLineRead(bool isImport, LogLineEventArgs logInfo)
        {
            if (!TryEnterLogCallback()) return;
            try
            {
                if (logInfo == null || IsStopping) return;
                // ACT/FFXIVプラグインの版により、片方がnetwork形式、片方がparsed形式になる。
                tracker.ProcessLine(logInfo.originalLogLine);
                if (!string.Equals(logInfo.logLine, logInfo.originalLogLine, StringComparison.Ordinal))
                    tracker.ProcessLine(logInfo.logLine);

                string toggleText;
                lock (settings)
                {
                    if (isImport || settings == null || !settings.EchoToggleEnabled ||
                        string.IsNullOrWhiteSpace(settings.EchoToggleText)) return;
                    toggleText = settings.EchoToggleText;
                }
                string message;
                var matched = EchoCommandParser.TryGetMessage(logInfo.logLine, out message) ||
                              EchoCommandParser.TryGetMessage(logInfo.originalLogLine, out message);
                if (!matched || !string.Equals(message.Trim(), toggleText.Trim(), StringComparison.OrdinalIgnoreCase)) return;
                Ui(ToggleOverlayFromEcho);
            }
            finally { ExitLogCallback(); }
        }

        private void ToggleOverlayFromEcho()
        {
            if (IsStopping || settings == null) return;
            lock (settings) settings.OverlayEnabled = !settings.OverlayEnabled;
            control?.SetOverlayEnabled(settings.OverlayEnabled);
            overlay?.ApplySettings(settings);
            CaptureSettingsSnapshot();
            if (status != null)
                status.Text = "Target Marker Overlay: " + Localization.Get(settings.Language, "StatusEcho", settings.OverlayEnabled ? "ON" : "OFF");
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            if (IsStopping) return;
            lock (settings) settings.Normalize();
            tracker.Language = settings.Language;
            overlay?.ApplySettings(settings);
            CaptureSettingsSnapshot();
        }

        private void OnOverlayBoundsChanged(object sender, EventArgs e) => CaptureSettingsSnapshot();

        private void OnCheckForUpdatesRequested(object sender, EventArgs e) => StartUpdateCheck(true);

        private void StartUpdateCheck(bool userInitiated)
        {
            if (Interlocked.CompareExchange(ref updateCheckActive, 1, 0) != 0) return;
            if (!StartBackgroundOperation(async token =>
            {
                try { await CheckForUpdatesAsync(userInitiated, token).ConfigureAwait(false); }
                finally { Interlocked.Exchange(ref updateCheckActive, 0); }
            })) Interlocked.Exchange(ref updateCheckActive, 0);
        }

        private async Task CheckForUpdatesAsync(bool userInitiated, CancellationToken token)
        {
            if (IsStopping || control == null || updateService == null) return;
            Ui(() => control.ShowUpdateChecking());
            var result = await updateService.CheckAsync(token).ConfigureAwait(false);
            if (IsStopping) return;
            Ui(() =>
            {
                availableRelease = result.Release;
                lock (settings) settings.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
                CaptureSettingsSnapshot();
                control.ShowUpdateResult(result);
                if (result.UpdateAvailable && result.Release != null && !string.Equals(settings.SkippedUpdateVersion, result.Release.Version.ToString(), StringComparison.OrdinalIgnoreCase))
                    status.Text = "Target Marker Overlay: " + Localization.Get(settings.Language, "UpdateAvailable", GitHubUpdateService.CurrentVersion, result.Release.Version);
                else if (userInitiated && !result.IsConfigured)
                    status.Text = "Target Marker Overlay: " + Localization.Get(settings.Language, "UpdateRepoMissing");
            });
        }

        private void OnInstallUpdateRequested(object sender, EventArgs e)
        {
            if (IsStopping || availableRelease == null || updateService == null) return;
            if (Interlocked.CompareExchange(ref updateDownloadActive, 1, 0) != 0) return;
            var release = availableRelease;
            if (!StartBackgroundOperation(async token =>
            {
                try
                {
                    Ui(() => control.ShowUpdateInstallState("UpdateDownloading"));
                    try
                    {
                        var staged = await updateService.DownloadAndPrepareAsync(release, token).ConfigureAwait(false);
                        Ui(() => control.ShowUpdateInstallState("UpdatePrepared", staged));
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { Ui(() => control.ShowUpdateInstallState("UpdateCorrupt", ex.Message, true)); }
                }
                finally { Interlocked.Exchange(ref updateDownloadActive, 0); }
            })) Interlocked.Exchange(ref updateDownloadActive, 0);
        }

        private void OnPostponeUpdateRequested(object sender, EventArgs e)
        {
            if (IsStopping || availableRelease == null) return;
            lock (settings) settings.SkippedUpdateVersion = availableRelease.Version.ToString();
            CaptureSettingsSnapshot();
            control.ShowUpdateInstallState("UpdateSkipped");
        }

        private bool StartBackgroundOperation(Func<CancellationToken, Task> operation)
        {
            return backgroundOperations.TryStart(async token =>
            {
                try { await operation(token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Ui(() =>
                    {
                        if (control != null && !control.IsDisposed)
                            control.ShowUpdateInstallState("UpdateFailed", ex.Message, true);
                    });
                }
            });
        }

        private bool TryEnterLogCallback()
        {
            lock (lifecycleSync)
            {
                if (stopping) return false;
                if (activeLogCallbacks++ == 0) logCallbacksDrained.Reset();
                return true;
            }
        }

        private void ExitLogCallback()
        {
            lock (lifecycleSync)
            {
                if (--activeLogCallbacks == 0) logCallbacksDrained.Set();
            }
        }

        private bool IsStopping { get { lock (lifecycleSync) return stopping; } }

        private void Ui(Action action)
        {
            if (IsStopping) return;
            var target = control;
            if (target == null || target.IsDisposed || !target.IsHandleCreated) return;
            if (target.InvokeRequired)
            {
                try
                {
                    target.BeginInvoke(new Action(() =>
                    {
                        if (!IsStopping && control != null && !control.IsDisposed) action();
                    }));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }
            if (!IsStopping && !target.IsDisposed) action();
        }

        public void DeInitPlugin()
        {
            lock (lifecycleSync)
            {
                if (stopping) return;
                stopping = true;
            }
            var backgroundDrain = backgroundOperations.StopAndWaitAsync();
            ActGlobals.oFormActMain.OnLogLineRead -= OnLogLineRead;
            control?.StopAcceptingInput();
            if (control != null)
            {
                control.SettingsChanged -= OnSettingsChanged;
                control.CheckForUpdatesRequested -= OnCheckForUpdatesRequested;
                control.InstallUpdateRequested -= OnInstallUpdateRequested;
                control.PostponeUpdateRequested -= OnPostponeUpdateRequested;
            }
            overlay?.StopPendingBoundsSaveAndCapture();
            if (overlay != null) overlay.BoundsChangedByUser -= OnOverlayBoundsChanged;
            logCallbacksDrained.Wait();

            try { backgroundDrain.GetAwaiter().GetResult(); } catch { }
            var saveSucceeded = false;
            try
            {
                saveSucceeded = settingsPersistence == null || settingsPersistence
                    .StopAndFlushAsync(PluginSettingsSerializer.Serialize(settings)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                if (status != null) status.Text = "Target Marker Overlay: " + Localization.Get(settings?.Language, "SaveError", ex.Message);
            }
            overlay?.Close();
            overlay?.Dispose();
            control?.Dispose();
            updateService?.Dispose();
            logCallbacksDrained.Dispose();
            if (status != null)
                status.Text = "Target Marker Overlay: " + (saveSucceeded
                    ? Localization.Get(settings?.Language, "StatusStopped")
                    : Localization.Get(settings?.Language, "SaveError", settingsPersistence?.LastError?.Message ?? "Unknown error"));
        }

        private PluginSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsPath)) return new PluginSettings();
                using (var stream = File.OpenRead(settingsPath))
                {
                    var buffer = new byte[stream.Length];
                    var read = 0;
                    while (read < buffer.Length)
                    {
                        var count = stream.Read(buffer, read, buffer.Length - read);
                        if (count == 0) break;
                        read += count;
                    }
                    if (read != buffer.Length) Array.Resize(ref buffer, read);
                    var value = PluginSettingsSerializer.Deserialize(buffer);
                    return value;
                }
            }
            catch
            {
                return new PluginSettings();
            }
        }

        private void CaptureSettingsSnapshot()
        {
            if (settings == null || settingsPersistence == null) return;
            settingsPersistence.UpdateSnapshot(PluginSettingsSerializer.Serialize(settings));
        }
    }
}
