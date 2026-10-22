using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using TargetMarkerOverlay;

internal static class Program
{
    private static int count;
    private static void Assert(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); count++; Console.WriteLine("PASS " + name); }

    private static void Main()
    {
        Assert(Localization.FromUiCulture(new CultureInfo("ja-JP")) == "ja", "OS Japanese mapping");
        Assert(Localization.FromUiCulture(new CultureInfo("zh-TW")) == "zh-CN", "OS Chinese mapping");
        Assert(Localization.FromUiCulture(new CultureInfo("ko-KR")) == "ko", "OS Korean mapping");
        Assert(Localization.FromUiCulture(new CultureInfo("fr-FR")) == "en", "OS fallback to English");
        Assert(Localization.Get("xx", "Support") == "Support", "translation fallback");
        Assert(Localization.Get("en", "Support") == "Support" && Localization.Get("ja", "Support") == "支援" && Localization.Get("zh-CN", "Support") == "支持" && Localization.Get("ko", "Support") == "후원", "four-language support tab names without symbols");
        var legacyXml = "<?xml version=\"1.0\"?><PluginSettings><OverlayEnabled>false</OverlayEnabled><OpacityPercent>75</OpacityPercent></PluginSettings>";
        PluginSettings legacy;
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacyXml))) legacy = (PluginSettings)new XmlSerializer(typeof(PluginSettings)).Deserialize(stream);
        Assert(legacy.Language == null && legacy.CheckUpdatesOnStartup && !legacy.OverlayEnabled && legacy.OpacityPercent == 75, "legacy Config backward compatibility");

        SemVersion v1, v2, pre;
        Assert(SemVersion.TryParse("v1.2.3", out v1), "SemVer v-tag parsing");
        Assert(SemVersion.TryParse("1.3.0", out v2) && v2.CompareTo(v1) > 0, "SemVer comparison");
        Assert(SemVersion.TryParse("1.3.0-beta.1", out pre) && pre.CompareTo(v2) < 0, "SemVer prerelease ordering");

        var json = "[{\"tag_name\":\"v1.4.0\",\"draft\":false,\"prerelease\":false,\"name\":\"Stable\",\"body\":\"Notes\",\"html_url\":\"https://github.com/o/r/releases/tag/v1.4.0\",\"assets\":[{\"name\":\"TargetMarkerOverlay-v1.4.0.zip\",\"browser_download_url\":\"https://github.com/o/r/releases/download/v1.4.0/a.zip\"},{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"https://github.com/o/r/releases/download/v1.4.0/SHA256SUMS.txt\"}]},{\"tag_name\":\"v2.0.0-beta\",\"draft\":false,\"prerelease\":true}]";
        var release = ReleaseParser.ParseLatestStable(json);
        Assert(release != null && release.Version.ToString() == "1.4.0" && release.Package != null && release.HashManifest != null, "stable Release JSON parsing");
        Assert(GitHubUpdateService.EvaluateReleaseJson(json, "1.3.0").UpdateAvailable, "update available response");
        Assert(!GitHubUpdateService.EvaluateReleaseJson(json, "1.4.0").UpdateAvailable, "no update response");
        Assert(!string.IsNullOrWhiteSpace(GitHubUpdateService.EvaluateReleaseJson("{broken", "1.3.0").Error), "malformed Release response is contained");
        Assert(UpdateSecurity.IsAllowedGitHubUrl("https://github.com/o/r/releases/download/v1/a.zip") && !UpdateSecurity.IsAllowedGitHubUrl("http://github.com/a") && !UpdateSecurity.IsAllowedGitHubUrl("https://evil.example/a"), "asset URL allowlist");

        var testCache = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".cache");
        Directory.CreateDirectory(testCache);
        var root = Path.Combine(testCache, "TMO-CoreTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var data = Path.Combine(root, "asset.zip"); File.WriteAllText(data, "test");
            var hash = UpdateSecurity.ComputeSha256(data);
            Assert(UpdateSecurity.FindExpectedHash(hash + "  asset.zip", "asset.zip") == hash, "SHA-256 manifest validation");
            Assert(!string.Equals(new string('0', 64), hash, StringComparison.OrdinalIgnoreCase), "corrupt asset hash mismatch");
            var safeZip = Path.Combine(root, "safe.zip");
            using (var z = ZipFile.Open(safeZip, ZipArchiveMode.Create)) { var e = z.CreateEntry("TargetMarkerOverlay.dll"); using (var w = new StreamWriter(e.Open())) w.Write("x"); }
            Assert(UpdateSecurity.ValidateZip(safeZip).Count == 1, "safe ZIP validation");
            var unsafeZip = Path.Combine(root, "unsafe.zip");
            using (var z = ZipFile.Open(unsafeZip, ZipArchiveMode.Create)) { var e = z.CreateEntry("../evil.dll"); using (var w = new StreamWriter(e.Open())) w.Write("x"); }
            var rejected = false; try { UpdateSecurity.ValidateZip(unsafeZip); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Zip Slip rejection");
            Assert(Localization.Get("en", "UpdatePrepared", "C:\\staged\\TargetMarkerOverlay.dll").Contains("will not start an installer"), "update completion does not leave a post-ACT installer");
            RunSettingsPersistenceTests(root).GetAwaiter().GetResult();
        }
        finally { try { Directory.Delete(root, true); } catch { } }
        Console.WriteLine("ALL PASS: " + count);
    }

    private static async Task RunSettingsPersistenceTests(string root)
    {
        var initial = Encoding.UTF8.GetBytes("unchanged");
        Assert(SettingsPersistenceCoordinator.DefaultInterval == TimeSpan.FromSeconds(60), "production persistence interval is exactly 60 seconds");
        var writes = 0;
        var unchanged = new SettingsPersistenceCoordinator(
            Path.Combine(root, "unchanged.xml"), initial, initial, TimeSpan.FromHours(1),
            (path, bytes) => { Interlocked.Increment(ref writes); return Task.FromResult(0); });
        Assert(await unchanged.StopAndFlushAsync(initial).ConfigureAwait(false) && writes == 0 && !File.Exists(Path.Combine(root, "unchanged.xml")), "unchanged settings perform zero writes");

        var revertedWrites = 0;
        var reverted = new SettingsPersistenceCoordinator(
            Path.Combine(root, "reverted.xml"), initial, initial, TimeSpan.FromHours(1),
            (path, bytes) => { Interlocked.Increment(ref revertedWrites); return Task.FromResult(0); });
        reverted.UpdateSnapshot(Encoding.UTF8.GetBytes("temporary edit"));
        reverted.UpdateSnapshot(initial);
        Assert(await reverted.StopAndFlushAsync(initial).ConfigureAwait(false) && revertedWrites == 0, "reverting to the saved settings performs zero writes");

        var coalescedPath = Path.Combine(root, "coalesced.xml");
        var coalescedWrites = 0;
        var coalesced = new SettingsPersistenceCoordinator(
            coalescedPath, initial, initial, TimeSpan.FromHours(1),
            (path, bytes) =>
            {
                Interlocked.Increment(ref coalescedWrites);
                File.WriteAllBytes(path, bytes);
                return Task.FromResult(0);
            });
        coalesced.UpdateSnapshot(Encoding.UTF8.GetBytes("edit-1"));
        coalesced.UpdateSnapshot(Encoding.UTF8.GetBytes("edit-2"));
        coalesced.UpdateSnapshot(Encoding.UTF8.GetBytes("edit-final"));
        Assert(await coalesced.SaveIfChangedAsync().ConfigureAwait(false) && coalescedWrites == 1 && File.ReadAllText(coalescedPath) == "edit-final", "continuous edits coalesce into one latest snapshot");
        Assert(await coalesced.StopAndFlushAsync(Encoding.UTF8.GetBytes("edit-final")).ConfigureAwait(false) && coalescedWrites == 1, "shutdown skips a snapshot already saved");

        var periodicValues = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var periodicPath = Path.Combine(root, "periodic.xml");
        var periodic = new SettingsPersistenceCoordinator(
            periodicPath, initial, initial, TimeSpan.FromMilliseconds(180),
            (path, bytes) =>
            {
                periodicValues.Enqueue(Encoding.UTF8.GetString(bytes));
                File.WriteAllBytes(path, bytes);
                return Task.FromResult(0);
            });
        periodic.UpdateSnapshot(Encoding.UTF8.GetBytes("burst-1"));
        periodic.UpdateSnapshot(Encoding.UTF8.GetBytes("burst-2"));
        periodic.UpdateSnapshot(Encoding.UTF8.GetBytes("burst-final"));
        await WaitForCount(() => periodicValues.Count, 1).ConfigureAwait(false);
        string periodicFirst;
        periodicValues.TryPeek(out periodicFirst);
        Assert(periodicFirst == "burst-final", "periodic check writes the last edit from the interval");
        periodic.UpdateSnapshot(Encoding.UTF8.GetBytes("next-interval"));
        await WaitForCount(() => periodicValues.Count, 2).ConfigureAwait(false);
        Assert(await periodic.StopAndFlushAsync(Encoding.UTF8.GetBytes("next-interval")).ConfigureAwait(false), "periodic coordinator stops and flushes cleanly");
        var afterStopCount = periodicValues.Count;
        await Task.Delay(250).ConfigureAwait(false);
        Assert(periodicValues.Count == afterStopCount && afterStopCount == 2, "timer performs no work after shutdown");

        var failedPeriodicCalls = 0;
        var failedPeriodicPath = Path.Combine(root, "periodic-retry.xml");
        var periodicRetry = new SettingsPersistenceCoordinator(
            failedPeriodicPath, initial, initial, TimeSpan.FromMilliseconds(180),
            (path, bytes) =>
            {
                if (Interlocked.Increment(ref failedPeriodicCalls) == 1) return Task.FromException(new IOException("simulated periodic failure"));
                return AtomicSettingsFile.WriteAsync(path, bytes);
            });
        var retrySnapshot = Encoding.UTF8.GetBytes("retry-at-next-period");
        periodicRetry.UpdateSnapshot(retrySnapshot);
        await WaitForCount(() => Volatile.Read(ref failedPeriodicCalls), 1).ConfigureAwait(false);
        await Task.Delay(40).ConfigureAwait(false);
        Assert(failedPeriodicCalls == 1 && periodicRetry.IsDirty && periodicRetry.LastError != null, "periodic failure retains dirty settings without an immediate retry");
        await WaitForCount(() => Volatile.Read(ref failedPeriodicCalls), 2).ConfigureAwait(false);
        Assert(await periodicRetry.StopAndFlushAsync(retrySnapshot).ConfigureAwait(false) && !periodicRetry.IsDirty && failedPeriodicCalls == 2, "failed periodic save retries at the next fixed interval");

        var finalSettingsPath = Path.Combine(root, "final-settings.xml");
        var settings = new PluginSettings { Language = "en", EchoToggleText = "first" };
        var savedSnapshot = PluginSettingsSerializer.Serialize(settings);
        settings.EchoToggleText = "last-before-exit";
        var finalSnapshot = PluginSettingsSerializer.Serialize(settings);
        var finalSaveCount = 0;
        var finalSave = new SettingsPersistenceCoordinator(
            finalSettingsPath, finalSnapshot, savedSnapshot, TimeSpan.FromHours(1),
            (path, bytes) =>
            {
                Interlocked.Increment(ref finalSaveCount);
                File.WriteAllBytes(path, bytes);
                return Task.FromResult(0);
            });
        Assert(await finalSave.StopAndFlushAsync(finalSnapshot).ConfigureAwait(false) && finalSaveCount == 1, "shutdown persists the latest unsaved change");
        var reloaded = PluginSettingsSerializer.Deserialize(File.ReadAllBytes(finalSettingsPath));
        Assert(reloaded.EchoToggleText == "last-before-exit", "saved settings reload with the final value");

        var protectedPath = Path.Combine(root, "protected.xml");
        File.WriteAllText(protectedPath, "existing-content");
        var oldBytes = Encoding.UTF8.GetBytes("existing-content");
        var newBytes = Encoding.UTF8.GetBytes("replacement-content");
        var protectedAttempts = 0;
        var protectedStore = new SettingsPersistenceCoordinator(
            protectedPath, newBytes, oldBytes, TimeSpan.FromHours(1),
            (path, bytes) =>
            {
                if (Interlocked.Increment(ref protectedAttempts) == 1) return Task.FromException(new IOException("simulated replace failure"));
                return AtomicSettingsFile.WriteAsync(path, bytes);
            });
        var saveFailed = !await protectedStore.SaveIfChangedAsync().ConfigureAwait(false);
        Assert(saveFailed && protectedStore.IsDirty && File.ReadAllText(protectedPath) == "existing-content", "failed replace preserves the old file and remains dirty");
        Assert(await protectedStore.StopAndFlushAsync(newBytes).ConfigureAwait(false) && protectedAttempts == 2 && File.ReadAllText(protectedPath) == "replacement-content", "failed save keeps the latest snapshot for the next scheduled save");

        var atomicPath = Path.Combine(root, "atomic.xml");
        AtomicSettingsFile.Write(atomicPath, oldBytes);
        AtomicSettingsFile.Write(atomicPath, newBytes);
        Assert(File.ReadAllText(atomicPath) == "replacement-content" && Directory.GetFiles(root, "atomic.xml.tmp-*").Length == 0, "atomic replacement succeeds and cleans its temporary file");

        var operations = new BackgroundOperationGroup();
        var operationEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operationRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operationFinished = 0;
        Assert(operations.TryStart(async token =>
        {
            token.Register(() => cancellationObserved.TrySetResult(true));
            operationEntered.TrySetResult(true);
            await operationRelease.Task.ConfigureAwait(false);
            Interlocked.Increment(ref operationFinished);
        }), "background group accepts work before shutdown");
        await operationEntered.Task.ConfigureAwait(false);
        var drain = operations.StopAndWaitAsync();
        await cancellationObserved.Task.ConfigureAwait(false);
        Assert(!drain.IsCompleted && !operations.TryStart(token => Task.FromResult(0)), "shutdown cancels and rejects new work while waiting for active work");
        operationRelease.TrySetResult(true);
        await drain.ConfigureAwait(false);
        Assert(operationFinished == 1 && drain.IsCompleted, "shutdown waits for non-cooperative work to actually finish");

        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlightCalls = 0;
        var inFlightPath = Path.Combine(root, "in-flight.xml");
        var inFlight = new SettingsPersistenceCoordinator(
            inFlightPath, initial, initial, TimeSpan.FromHours(1),
            async (path, bytes) =>
            {
                Interlocked.Increment(ref inFlightCalls);
                if (inFlightCalls == 1)
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                }
                File.WriteAllBytes(path, bytes);
            });
        inFlight.UpdateSnapshot(Encoding.UTF8.GetBytes("first-in-flight"));
        var firstWrite = inFlight.SaveIfChangedAsync();
        inFlight.UpdateSnapshot(Encoding.UTF8.GetBytes("changed-during-write"));
        await entered.Task.ConfigureAwait(false);
        var ending = inFlight.StopAndFlushAsync(Encoding.UTF8.GetBytes("changed-during-write"));
        await Task.Delay(60).ConfigureAwait(false);
        Assert(!ending.IsCompleted, "shutdown waits for an in-flight write");
        release.TrySetResult(true);
        Assert(await firstWrite.ConfigureAwait(false) && await ending.ConfigureAwait(false), "in-flight write and final flush complete in order");
        Assert(inFlightCalls == 2 && File.ReadAllText(inFlightPath) == "changed-during-write", "shutdown serializes writes and preserves edits made during a write");
        var callsAfterEnd = inFlightCalls;
        await Task.Delay(100).ConfigureAwait(false);
        Assert(inFlightCalls == callsAfterEnd, "no persistence work continues after the final flush");
    }

    private static async Task WaitForCount(Func<int> count, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (count() < expected && DateTime.UtcNow < deadline) await Task.Delay(10).ConfigureAwait(false);
        if (count() < expected) throw new TimeoutException("Timed out waiting for the periodic save.");
    }
}
