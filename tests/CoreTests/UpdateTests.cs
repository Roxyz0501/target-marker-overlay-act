using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TargetMarkerOverlay;
using TargetMarkerOverlay.Updater;

internal static partial class Program
{
    private static void RunUpdateTests(string root)
    {
        var stage = Path.Combine(root, "更新 準備"); var installed = Path.Combine(root, "登録 先");
        Directory.CreateDirectory(stage); Directory.CreateDirectory(installed);
        var source = Path.Combine(stage, "TargetMarkerOverlay.dll"); var target = Path.Combine(installed, "TargetMarkerOverlay.dll");
        File.WriteAllText(source, "new"); File.WriteAllText(target, "old");
        var newHash = Installer.Hash(source); var oldHash = Installer.Hash(target);
        Assert(UpdateSecurity.NormalizeRegisteredPath(new Uri(target).AbsoluteUri) == target, "registered file URI with Japanese and spaces");
        foreach (var invalid in new[] { "", "https://example.com/TargetMarkerOverlay.dll", "relative.dll" })
        {
            var rejected = false; try { UpdateSecurity.NormalizeRegisteredPath(invalid); } catch { rejected = true; }
            Assert(rejected, "reject invalid registered path " + invalid);
        }
        var backup = Installer.ReplaceWithBackup(source, target, newHash, oldHash);
        Assert(File.ReadAllText(target) == "new" && File.ReadAllText(backup) == "old", "automatic replacement and original backup");
        File.WriteAllText(target, "old"); var failed = false;
        try { Installer.ReplaceWithBackup(source, target, newHash, oldHash, (incoming, destination, saved) => { File.WriteAllText(destination, "broken"); throw new IOException("Injected failure"); }); }
        catch (IOException) { failed = true; }
        Assert(failed && File.ReadAllText(target) == "old", "failed replacement restores original");
        failed = false;
        try { Installer.ReplaceWithBackup(source, target, new string('0', 64), oldHash); } catch (InvalidDataException) { failed = true; }
        Assert(failed && File.ReadAllText(target) == "old", "corrupt source does not change installed DLL");
        File.WriteAllText(target, "changed"); failed = false;
        try { Installer.ReplaceWithBackup(source, target, newHash, oldHash); } catch (InvalidDataException) { failed = true; }
        Assert(failed && File.ReadAllText(target) == "changed", "concurrent installed change is preserved");
        var duplicate = Path.Combine(root, "duplicate.zip");
        using (var z = ZipFile.Open(duplicate, ZipArchiveMode.Create)) { z.CreateEntry("TargetMarkerOverlay.dll"); z.CreateEntry("TargetMarkerOverlay.dll"); }
        failed = false; try { UpdateSecurity.ValidateZip(duplicate); } catch (InvalidDataException) { failed = true; }
        Assert(failed, "duplicate ZIP entry rejected");
        Assert(UpdateSecurity.PlainTextNotes("## Changes\n- **Fix** [details](https://github.com) `code`") == "Changes\nFix details code", "release notes remove Markdown");
        var actualDll = typeof(PluginSettings).Assembly.Location; var actualUpdater = typeof(Installer).Assembly.Location;
        SemVersion version; SemVersion.TryParse(GitHubUpdateService.CurrentVersion, out version);
        UpdateSecurity.ValidateAssembly(actualDll, "TargetMarkerOverlay", version);
        UpdateSecurity.ValidateAssembly(actualUpdater, "TargetMarkerOverlay.Updater", version);
        var prepared = new PreparedUpdate { DllPath = actualDll, UpdaterPath = actualUpdater, Version = version, DllHash = UpdateSecurity.ComputeSha256(actualDll), UpdaterHash = UpdateSecurity.ComputeSha256(actualUpdater) };
        var packagePath = Path.Combine(root, "valid-package.zip");
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(actualDll, "TargetMarkerOverlay.dll");
            archive.CreateEntryFromFile(actualUpdater, "TargetMarkerOverlay.Updater.exe");
        }
        foreach (var corrupt in new[] { false, true })
        {
            using (var service = new GitHubUpdateService(new AssetHandler(File.ReadAllBytes(packagePath), UpdateSecurity.ComputeSha256(packagePath), corrupt)))
            {
                var rejected = false; PreparedUpdate verified = null;
                try { verified = service.DownloadAndPrepareAsync(GitHubUpdateService.FromStableRedirect(UpdateConfiguration.RepositoryUrl + "/releases/tag/v" + version), CancellationToken.None).GetAwaiter().GetResult(); }
                catch (InvalidDataException) { rejected = true; }
                Assert(corrupt ? rejected : verified != null && File.Exists(verified.UpdaterPath), corrupt ? "download rejects corrupt package" : "tag-pinned package verifies DLL and updater without REST API");
                if (verified != null) Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(verified.DllPath)), true);
            }
        }
        var start = prepared.CreateStartInfo(target, 123, 456, "ja");
        Assert(!start.UseShellExecute && start.CreateNoWindow && start.Arguments.Contains("--target \"" + target + "\"") && start.FileName == actualUpdater, "verified helper and quoted registered target arguments");
        File.Copy(actualDll, source, true); File.Copy(actualUpdater, Path.Combine(stage, "TargetMarkerOverlay.Updater.exe"), true);
        File.WriteAllText(target, "old");
        using (var parent = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 4") { UseShellExecute = false, CreateNoWindow = true }))
        {
            prepared.DllPath = source; prepared.UpdaterPath = Path.Combine(stage, "TargetMarkerOverlay.Updater.exe"); prepared.DllHash = Installer.Hash(source);
            using (var helper = Process.Start(prepared.CreateStartInfo(target, parent.Id, parent.StartTime.ToUniversalTime().Ticks, "ja")))
            {
                Assert(!helper.WaitForExit(250) && File.ReadAllText(target) == "old", "helper leaves loaded target untouched while parent lives");
                parent.WaitForExit();
                Assert(helper.WaitForExit(10000) && helper.ExitCode == 0 && Installer.Hash(target) == prepared.DllHash, "helper replaces registered target after parent exits then terminates");
            }
        }
    }

    private sealed class AssetHandler : HttpMessageHandler
    {
        private readonly byte[] package; private readonly string hash; private readonly bool corrupt;
        public AssetHandler(byte[] package, string hash, bool corrupt) { this.package = package; this.hash = hash; this.corrupt = corrupt; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri.Host != "github.com") throw new Exception("Unexpected REST request for assets");
            HttpContent content = request.RequestUri.AbsolutePath.EndsWith("SHA256SUMS.txt")
                ? (HttpContent)new StringContent((corrupt ? new string('0', 64) : hash) + "  TargetMarkerOverlay-v" + GitHubUpdateService.CurrentVersion + ".zip") : new ByteArrayContent(package);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private static async Task RunLiveReleaseTest()
    {
        if (Environment.GetEnvironmentVariable("TMO_LIVE_RELEASE") != "1") return;
        using (var service = new GitHubUpdateService())
        {
            var result = await service.CheckAsync(CancellationToken.None);
            Assert(result.Error == null && result.Release != null && result.Release.Version.ToString() == GitHubUpdateService.CurrentVersion, "public latest HEAD connects to installed release");
            var prepared = await service.DownloadAndPrepareAsync(result.Release, CancellationToken.None);
            Assert(File.Exists(prepared.UpdaterPath) && prepared.Version.ToString() == GitHubUpdateService.CurrentVersion, "public ZIP/SHA validates actual DLL and helper identities");
            Console.WriteLine("VERIFIED_PUBLIC_DLL " + prepared.DllPath);
        }
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public int HeadCount, ApiCount; public string Tag = "v1.5.0"; public int ApiStatus = 200; public bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (Fail) throw new HttpRequestException("offline");
            if (request.Method == HttpMethod.Head)
            {
                HeadCount++;
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri(UpdateConfiguration.RepositoryUrl + "/releases/tag/" + Tag);
                return Task.FromResult(response);
            }
            ApiCount++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)ApiStatus) { Content = new StringContent("{\"tag_name\":\"" + Tag + "\",\"draft\":false,\"prerelease\":false,\"body\":\"Notes\"}") });
        }
    }

    private static async Task RunNetworkTests()
    {
        var fake = new FakeGitHub();
        using (var service = new GitHubUpdateService(fake))
        {
            var result = await service.CheckAsync(CancellationToken.None, "1.5.0");
            Assert(!result.UpdateAvailable && fake.HeadCount == 1 && fake.ApiCount == 0, "unchanged version: one HEAD and zero REST calls");
            result = await service.CheckAsync(CancellationToken.None, "1.4.2");
            Assert(result.UpdateAvailable && result.Release.Notes == "Notes" && fake.ApiCount == 1, "new version: one bounded REST detail request");
            foreach (var code in new[] { 403, 429 })
            {
                fake.ApiStatus = code; result = await service.CheckAsync(CancellationToken.None, "1.4.2");
                Assert(result.UpdateAvailable && result.RateLimited && result.Release.Package.Url.EndsWith("/v1.5.0/TargetMarkerOverlay-v1.5.0.zip"), "HTTP " + code + " retains tag-pinned verified update route");
            }
            fake.Fail = true; result = await service.CheckAsync(CancellationToken.None, "1.4.2");
            Assert(!result.RateLimited && result.Error != null, "offline failure differs from HTTP rate limit");
        }
        foreach (var url in new[] { UpdateConfiguration.RepositoryUrl + "/releases/tag/v1.5.0-beta", "https://github.com/other/plugin/releases/tag/v1.5.0", UpdateConfiguration.RepositoryUrl + "/releases/tag/v1.5.0?bad", UpdateConfiguration.RepositoryUrl + "/releases/tag/v01.5.0" })
        {
            var failed = false; try { GitHubUpdateService.FromStableRedirect(url); } catch (InvalidDataException) { failed = true; }
            Assert(failed, "reject unstable or unrelated latest redirect");
        }
    }

    private static void RunUiTests(string root)
    {
        foreach (var language in new[] { "en", "ja", "zh-CN", "ko" })
        {
            var settings = new PluginSettings { Language = language }; settings.Normalize();
            using (var control = new SettingsControl(settings, new MarkerStateTracker()))
            {
                control.Size = new System.Drawing.Size(900, 720); control.CreateControl();
                var tabs = Descendants(control).OfType<TabControl>().Single();
                Assert(tabs.GetType().FullName == "Roxyz.Act.UI.PluginTabs" && tabs.SizeMode == TabSizeMode.Normal && tabs.Padding == new System.Drawing.Point(18, 6) && tabs.Font.Bold, "shared tab behavior " + language);
                var update = tabs.TabPages.Cast<TabPage>().Single(p => (string)p.Tag == "Update");
                Assert(Descendants(update).OfType<LinkLabel>().Any(l => l.Text == UpdateConfiguration.RepositoryUrl) && Descendants(update).Any(c => c.Text == Localization.Get(language, "Author")), "update author and clickable repository " + language);
                Assert(!Localization.Get(language, "HeaderSubtitle").Contains("Roxyz0501"), "header author removed " + language);
                Assert(Localization.Get(language, "UpdateRateLimited") != "UpdateRateLimited" && Localization.Get(language, "UpdatePathFailed") != "UpdatePathFailed", "localized update errors " + language);
                tabs.SelectedTab = update;
                using (var bitmap = new System.Drawing.Bitmap(control.Width, control.Height))
                {
                    control.DrawToBitmap(bitmap, control.ClientRectangle);
                    var output = Environment.GetEnvironmentVariable("TMO_UI_OUTPUT");
                    if (!string.IsNullOrEmpty(output)) { Directory.CreateDirectory(output); bitmap.Save(Path.Combine(output, "update-" + language + ".png")); }
                }
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
