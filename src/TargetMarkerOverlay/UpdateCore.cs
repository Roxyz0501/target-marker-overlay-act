using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TargetMarkerOverlay
{
    public static class UpdateConfiguration
    {
        public const string RepositoryOwner = "Roxyz0501";
        public const string RepositoryName = "target-marker-overlay-act";
        public const string PluginFileName = "TargetMarkerOverlay.dll";
        public const string UpdaterFileName = "TargetMarkerOverlay.Updater.exe";
        public const string RepositoryUrl = "https://github.com/Roxyz0501/target-marker-overlay-act";
        public static bool IsConfigured => !string.IsNullOrWhiteSpace(RepositoryOwner) && !string.IsNullOrWhiteSpace(RepositoryName);
    }

    public sealed class SemVersion : IComparable<SemVersion>
    {
        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Patch { get; private set; }
        public string PreRelease { get; private set; }

        public static bool TryParse(string text, out SemVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var value = text.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$")) return false;
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
            var plus = value.IndexOf('+');
            if (plus >= 0) value = value.Substring(0, plus);
            string pre = null;
            var dash = value.IndexOf('-');
            if (dash >= 0) { pre = value.Substring(dash + 1); value = value.Substring(0, dash); }
            var parts = value.Split('.');
            int major, minor, patch;
            if (parts.Length != 3 || !int.TryParse(parts[0], out major) || !int.TryParse(parts[1], out minor) || !int.TryParse(parts[2], out patch) || major < 0 || minor < 0 || patch < 0) return false;
            version = new SemVersion { Major = major, Minor = minor, Patch = patch, PreRelease = pre };
            return true;
        }

        public int CompareTo(SemVersion other)
        {
            if (other == null) return 1;
            var result = Major.CompareTo(other.Major); if (result != 0) return result;
            result = Minor.CompareTo(other.Minor); if (result != 0) return result;
            result = Patch.CompareTo(other.Patch); if (result != 0) return result;
            if (string.IsNullOrEmpty(PreRelease) && !string.IsNullOrEmpty(other.PreRelease)) return 1;
            if (!string.IsNullOrEmpty(PreRelease) && string.IsNullOrEmpty(other.PreRelease)) return -1;
            return string.Compare(PreRelease, other.PreRelease, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString() => Major + "." + Minor + "." + Patch + (string.IsNullOrEmpty(PreRelease) ? "" : "-" + PreRelease);
    }

    public sealed class ReleaseAsset
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }

    public sealed class ReleaseInfo
    {
        public string Tag { get; set; }
        public SemVersion Version { get; set; }
        public string Name { get; set; }
        public string Notes { get; set; }
        public string PageUrl { get; set; }
        public ReleaseAsset Package { get; set; }
        public ReleaseAsset HashManifest { get; set; }
    }

    public sealed class UpdateCheckResult
    {
        public bool IsConfigured { get; set; }
        public bool UpdateAvailable { get; set; }
        public string Error { get; set; }
        public bool RateLimited { get; set; }
        public ReleaseInfo Release { get; set; }
    }

    public static class ReleaseParser
    {
        public static ReleaseInfo ParseLatestStable(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
            var releases = serializer.Deserialize<List<Dictionary<string, object>>>(json);
            if (releases == null) return null;
            ReleaseInfo best = null;
            foreach (var raw in releases)
            {
                if (Bool(raw, "draft") || Bool(raw, "prerelease")) continue;
                var tag = String(raw, "tag_name");
                SemVersion version;
                if (!SemVersion.TryParse(tag, out version) || !string.IsNullOrEmpty(version.PreRelease)) continue;
                var info = new ReleaseInfo { Tag = tag, Version = version, Name = String(raw, "name"), Notes = String(raw, "body"), PageUrl = String(raw, "html_url") };
                object assetsObject;
                if (raw.TryGetValue("assets", out assetsObject))
                {
                    var assets = assetsObject as object[];
                    if (assets == null && assetsObject is System.Collections.ArrayList list) assets = list.ToArray();
                    foreach (var assetObject in assets ?? new object[0])
                    {
                        var asset = assetObject as Dictionary<string, object>;
                        if (asset == null) continue;
                        var item = new ReleaseAsset { Name = String(asset, "name"), Url = String(asset, "browser_download_url") };
                        if (item.Name == "TargetMarkerOverlay-v" + version + ".zip") info.Package = item;
                        if (string.Equals(item.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) info.HashManifest = item;
                    }
                }
                if (best == null || info.Version.CompareTo(best.Version) > 0) best = info;
            }
            return best;
        }

        private static bool Bool(Dictionary<string, object> source, string key) { object value; return source.TryGetValue(key, out value) && Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
        private static string String(Dictionary<string, object> source, string key) { object value; return source.TryGetValue(key, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null; }
    }

    public static class UpdateSecurity
    {
        private static readonly string[] AllowedHosts = { "github.com", "api.github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" };

        public static bool IsAllowedGitHubUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)) return false;
            return AllowedHosts.Any(host => string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase));
        }

        public static string PlainTextNotes(string notes)
        {
            var text = (notes ?? "");
            if (text.Length > 12000) text = text.Substring(0, 12000);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^\s{0,3}(#{1,6}|>|[-*])\s*", "");
            return text.Replace("**", "").Replace("`", "");
        }

        public static string NormalizeRegisteredPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("ACT plugin registration is missing.");
            Uri uri;
            if (Uri.TryCreate(value, UriKind.Absolute, out uri) && !string.IsNullOrEmpty(uri.Scheme))
            {
                if (!uri.IsFile || uri.IsUnc) throw new InvalidDataException("Only local registered DLL paths are supported.");
                value = uri.LocalPath;
            }
            if (!Path.IsPathRooted(value) || value.StartsWith(@"\\", StringComparison.Ordinal)) throw new InvalidDataException("Invalid registered DLL path.");
            var path = Path.GetFullPath(value);
            if (!File.Exists(path) || !string.Equals(Path.GetFileName(path), UpdateConfiguration.PluginFileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Registered TargetMarkerOverlay.dll was not found.");
            return path;
        }

        public static void ValidateAssembly(string path, string name, SemVersion version)
        {
            var assembly = AssemblyName.GetAssemblyName(path);
            var info = FileVersionInfo.GetVersionInfo(path);
            var expected = new Version(version.Major, version.Minor, version.Patch, 0);
            if (assembly.Name != name || assembly.Version != expected || info.CompanyName != "Roxyz0501" ||
                info.FileVersion != expected.ToString()) throw new InvalidDataException("Assembly identity, publisher, or release version mismatch.");
        }

        public static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static string FindExpectedHash(string manifest, string assetName)
        {
            foreach (var line in (manifest ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.Length < 64) continue;
                var hash = trimmed.Substring(0, 64);
                var name = trimmed.Substring(64).TrimStart(' ', '*');
                if (hash.All(Uri.IsHexDigit) && string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase)) return hash.ToLowerInvariant();
            }
            return null;
        }

        public static List<string> ValidateZip(string zipPath)
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal) { UpdateConfiguration.PluginFileName, UpdateConfiguration.UpdaterFileName, "README.md", "THIRD_PARTY_LICENSES.md" };
            var result = new List<string>();
            long total = 0;
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    var normalized = entry.FullName.Replace('\\', '/');
                    if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains("../") || normalized.Contains(":") || normalized.Contains("/")) throw new InvalidDataException("Unsafe archive entry: " + entry.FullName);
                    if (!allowed.Contains(normalized)) throw new InvalidDataException("Unexpected archive entry: " + entry.FullName);
                    if (result.Contains(normalized) || entry.Length > 32 * 1024 * 1024 || (total += entry.Length) > 64 * 1024 * 1024) throw new InvalidDataException("Duplicate or oversized archive entry.");
                    result.Add(normalized);
                }
            }
            if (!result.Contains(UpdateConfiguration.PluginFileName, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Plugin DLL is missing.");
            if (!result.Contains(UpdateConfiguration.UpdaterFileName)) throw new InvalidDataException("Verified updater is missing.");
            return result;
        }
    }

    public sealed class GitHubUpdateService : IDisposable
    {
        private readonly HttpClient client;
        public GitHubUpdateService(HttpMessageHandler handler = null)
        {
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TargetMarkerOverlay/" + CurrentVersion);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        public static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        public async Task<UpdateCheckResult> CheckAsync(CancellationToken token, string installedVersion = null)
        {
            if (!UpdateConfiguration.IsConfigured) return new UpdateCheckResult { IsConfigured = false };
            try
            {
                ReleaseInfo latest;
                using (var request = new HttpRequestMessage(HttpMethod.Head, UpdateConfiguration.RepositoryUrl + "/releases/latest"))
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429) throw new RateLimitException();
                    if ((int)response.StatusCode != 302 && (int)response.StatusCode != 301 && (int)response.StatusCode != 307 && (int)response.StatusCode != 308)
                        throw new InvalidDataException("Latest stable release redirect was not returned.");
                    var location = response.Headers.Location;
                    if (location == null) throw new InvalidDataException("Latest release location is missing.");
                    latest = FromStableRedirect(new Uri(new Uri(UpdateConfiguration.RepositoryUrl + "/releases/latest"), location).AbsoluteUri);
                }
                SemVersion current;
                if (!SemVersion.TryParse(installedVersion ?? CurrentVersion, out current)) throw new InvalidDataException("Invalid installed version.");
                var result = new UpdateCheckResult { IsConfigured = true, Release = latest, UpdateAvailable = latest.Version.CompareTo(current) > 0 };
                if (!result.UpdateAvailable) return result; // HEAD only. No REST request for unchanged versions.
                try
                {
                    var url = "https://api.github.com/repos/" + UpdateConfiguration.RepositoryOwner + "/" + UpdateConfiguration.RepositoryName + "/releases/tags/" + latest.Tag;
                    var bytes = await DownloadBytesAsync(url, token, 2 * 1024 * 1024).ConfigureAwait(false);
                    var detail = ReleaseParser.ParseLatestStable("[" + Encoding.UTF8.GetString(bytes) + "]");
                    if (detail == null || detail.Tag != latest.Tag) throw new InvalidDataException("Release is not the expected stable tag.");
                    latest.Notes = detail.Notes;
                }
                catch (RateLimitException) { result.RateLimited = true; }
                catch (HttpRequestException ex) { result.Error = ex.Message; }
                return result;
            }
            catch (RateLimitException) { return new UpdateCheckResult { IsConfigured = true, RateLimited = true }; }
            catch (Exception ex) { return new UpdateCheckResult { IsConfigured = true, Error = ex.Message }; }
        }

        public static ReleaseInfo FromStableRedirect(string url)
        {
            var prefix = UpdateConfiguration.RepositoryUrl + "/releases/tag/";
            if (url == null || !url.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Unexpected latest release repository.");
            var tag = url.Substring(prefix.Length);
            SemVersion version;
            if (!SemVersion.TryParse(tag, out version) || !string.IsNullOrEmpty(version.PreRelease) || tag != "v" + version)
                throw new InvalidDataException("Invalid stable release tag.");
            var asset = "TargetMarkerOverlay-v" + version + ".zip";
            var download = UpdateConfiguration.RepositoryUrl + "/releases/download/" + tag + "/";
            return new ReleaseInfo { Tag = tag, Version = version, PageUrl = url, Package = new ReleaseAsset { Name = asset, Url = download + asset }, HashManifest = new ReleaseAsset { Name = "SHA256SUMS.txt", Url = download + "SHA256SUMS.txt" } };
        }

        public static UpdateCheckResult EvaluateReleaseJson(string json, string currentVersion)
        {
            try
            {
                var release = ReleaseParser.ParseLatestStable(json);
                SemVersion current;
                if (!SemVersion.TryParse(currentVersion, out current)) throw new FormatException("Current version is not valid SemVer.");
                return new UpdateCheckResult { IsConfigured = true, Release = release, UpdateAvailable = release != null && release.Version.CompareTo(current) > 0 };
            }
            catch (Exception ex) { return new UpdateCheckResult { IsConfigured = true, Error = ex.Message }; }
        }

        public async Task<PreparedUpdate> DownloadAndPrepareAsync(ReleaseInfo release, CancellationToken token)
        {
            if (release?.Package == null || release.HashManifest == null) throw new InvalidDataException("Release assets or SHA256SUMS.txt are missing.");
            if (!UpdateSecurity.IsAllowedGitHubUrl(release.Package.Url) || !UpdateSecurity.IsAllowedGitHubUrl(release.HashManifest.Url)) throw new InvalidDataException("Non-GitHub HTTPS asset URL was rejected.");
            var expectedName = "TargetMarkerOverlay-v" + release.Version + ".zip";
            var prefix = UpdateConfiguration.RepositoryUrl + "/releases/download/" + release.Tag + "/";
            if (release.Tag != "v" + release.Version || release.Package.Name != expectedName || release.HashManifest.Name != "SHA256SUMS.txt" ||
                release.Package.Url != prefix + expectedName || release.HashManifest.Url != prefix + "SHA256SUMS.txt") throw new InvalidDataException("Unexpected release asset identity.");
            var root = Path.Combine(Path.GetTempPath(), "TargetMarkerOverlayUpdate", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
            var zip = Path.Combine(root, Path.GetFileName(release.Package.Name));
            var manifestBytes = await DownloadBytesAsync(release.HashManifest.Url, token, 1024 * 1024).ConfigureAwait(false);
            var manifest = Encoding.UTF8.GetString(manifestBytes);
            var bytes = await DownloadBytesAsync(release.Package.Url, token, 64 * 1024 * 1024).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.WriteAllBytes(zip, bytes);
            var expected = UpdateSecurity.FindExpectedHash(manifest, release.Package.Name);
            if (expected == null || !string.Equals(expected, UpdateSecurity.ComputeSha256(zip), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 verification failed.");
            UpdateSecurity.ValidateZip(zip);
            var stage = Path.Combine(root, "stage"); Directory.CreateDirectory(stage);
            using (var archive = ZipFile.OpenRead(zip))
                foreach (var entry in archive.Entries) entry.ExtractToFile(Path.Combine(stage, entry.Name), true);
            var dll = Path.Combine(stage, UpdateConfiguration.PluginFileName);
            var updater = Path.Combine(stage, UpdateConfiguration.UpdaterFileName);
            UpdateSecurity.ValidateAssembly(dll, "TargetMarkerOverlay", release.Version);
            UpdateSecurity.ValidateAssembly(updater, "TargetMarkerOverlay.Updater", release.Version);
            return new PreparedUpdate { DllPath = dll, UpdaterPath = updater, DllHash = UpdateSecurity.ComputeSha256(dll), UpdaterHash = UpdateSecurity.ComputeSha256(updater), Version = release.Version };
            }
            catch { try { Directory.Delete(root, true); } catch { } throw; }
        }

        private async Task<byte[]> DownloadBytesAsync(string url, CancellationToken token, int limit)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                for (var redirect = 0; redirect < 6; redirect++)
                {
                    if (!UpdateSecurity.IsAllowedGitHubUrl(url)) throw new InvalidDataException("Unsafe download redirect.");
                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                        {
                            if (response.Headers.Location == null) throw new InvalidDataException("Redirect location missing.");
                            url = new Uri(new Uri(url), response.Headers.Location).AbsoluteUri; continue;
                        }
                        if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429) throw new RateLimitException();
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Download exceeds size limit.");
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new MemoryStream())
                        {
                            var buffer = new byte[81920]; int read;
                            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                            {
                                if (output.Length + read > limit) throw new InvalidDataException("Download exceeds size limit.");
                                output.Write(buffer, 0, read);
                            }
                            return output.ToArray();
                        }
                    }
                }
                throw new InvalidDataException("Too many download redirects.");
            }
        }

        public void Dispose() { client.Dispose(); }
    }

    public sealed class RateLimitException : HttpRequestException
    {
        public RateLimitException() : base("GitHub HTTP 403/429") { }
    }

    public sealed class PreparedUpdate
    {
        public string DllPath { get; set; }
        public string UpdaterPath { get; set; }
        public string DllHash { get; set; }
        public string UpdaterHash { get; set; }
        public SemVersion Version { get; set; }

        public ProcessStartInfo CreateStartInfo(string target, int pid, long startTicks, string language)
        {
            target = UpdateSecurity.NormalizeRegisteredPath(target);
            UpdateSecurity.ValidateAssembly(DllPath, "TargetMarkerOverlay", Version);
            UpdateSecurity.ValidateAssembly(UpdaterPath, "TargetMarkerOverlay.Updater", Version);
            if (UpdateSecurity.ComputeSha256(DllPath) != DllHash || UpdateSecurity.ComputeSha256(UpdaterPath) != UpdaterHash) throw new InvalidDataException("Staged files changed.");
            return new ProcessStartInfo
            {
                FileName = UpdaterPath, UseShellExecute = false, CreateNoWindow = true,
                Arguments = "--wait-pid " + pid + " --start-ticks " + startTicks + " --source " + Quote(DllPath) + " --target " + Quote(target) +
                    " --sha256 " + DllHash + " --original " + UpdateSecurity.ComputeSha256(target) + " --version " + Version + " --language " + Localization.Normalize(language)
            };
        }
        private static string Quote(string value)
        {
            if (value.IndexOf('"') >= 0 || value.EndsWith("\\", StringComparison.Ordinal)) throw new InvalidDataException("Invalid file argument.");
            return "\"" + value + "\"";
        }
    }
}
