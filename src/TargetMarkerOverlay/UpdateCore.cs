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
                        if (item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && item.Name.IndexOf("TargetMarkerOverlay", StringComparison.OrdinalIgnoreCase) >= 0) info.Package = item;
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
        private static readonly string[] AllowedHosts = { "github.com", "api.github.com", "objects.githubusercontent.com", "githubusercontent.com" };

        public static bool IsAllowedGitHubUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
            return AllowedHosts.Any(host => string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
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
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { UpdateConfiguration.PluginFileName, "README.md", "THIRD_PARTY_LICENSES.md" };
            var result = new List<string>();
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    var normalized = entry.FullName.Replace('\\', '/');
                    if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains("../") || normalized.Contains(":") || normalized.Contains("/")) throw new InvalidDataException("Unsafe archive entry: " + entry.FullName);
                    if (!allowed.Contains(normalized)) throw new InvalidDataException("Unexpected archive entry: " + entry.FullName);
                    result.Add(normalized);
                }
            }
            if (!result.Contains(UpdateConfiguration.PluginFileName, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Plugin DLL is missing.");
            return result;
        }
    }

    public sealed class GitHubUpdateService : IDisposable
    {
        private readonly HttpClient client;
        public GitHubUpdateService()
        {
            client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TargetMarkerOverlay/" + CurrentVersion);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        public static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        public async Task<UpdateCheckResult> CheckAsync(CancellationToken token)
        {
            if (!UpdateConfiguration.IsConfigured) return new UpdateCheckResult { IsConfigured = false };
            try
            {
                var url = "https://api.github.com/repos/" + UpdateConfiguration.RepositoryOwner + "/" + UpdateConfiguration.RepositoryName + "/releases?per_page=20";
                using (var response = await client.GetAsync(url, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return EvaluateReleaseJson(json, CurrentVersion);
                }
            }
            catch (Exception ex) { return new UpdateCheckResult { IsConfigured = true, Error = ex.Message }; }
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

        public async Task<string> DownloadAndPrepareAsync(ReleaseInfo release, CancellationToken token)
        {
            if (release?.Package == null || release.HashManifest == null) throw new InvalidDataException("Release assets or SHA256SUMS.txt are missing.");
            if (!UpdateSecurity.IsAllowedGitHubUrl(release.Package.Url) || !UpdateSecurity.IsAllowedGitHubUrl(release.HashManifest.Url)) throw new InvalidDataException("Non-GitHub HTTPS asset URL was rejected.");
            var root = Path.Combine(Path.GetTempPath(), "TargetMarkerOverlayUpdate", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var zip = Path.Combine(root, Path.GetFileName(release.Package.Name));
            var manifestBytes = await DownloadBytesAsync(release.HashManifest.Url, token).ConfigureAwait(false);
            var manifest = Encoding.UTF8.GetString(manifestBytes);
            var bytes = await DownloadBytesAsync(release.Package.Url, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.WriteAllBytes(zip, bytes);
            var expected = UpdateSecurity.FindExpectedHash(manifest, release.Package.Name);
            if (expected == null || !string.Equals(expected, UpdateSecurity.ComputeSha256(zip), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 verification failed.");
            UpdateSecurity.ValidateZip(zip);
            var stage = Path.Combine(root, "stage"); Directory.CreateDirectory(stage);
            using (var archive = ZipFile.OpenRead(zip))
                foreach (var entry in archive.Entries) entry.ExtractToFile(Path.Combine(stage, entry.Name), true);
            var dll = Path.Combine(stage, UpdateConfiguration.PluginFileName);
            var info = FileVersionInfo.GetVersionInfo(dll);
            if (!string.Equals(info.ProductName, "Target Marker Overlay", StringComparison.Ordinal) || !info.FileVersion.StartsWith(release.Version.ToString() + ".", StringComparison.Ordinal)) throw new InvalidDataException("Plugin name or version does not match the release.");
            return dll;
        }

        private async Task<byte[]> DownloadBytesAsync(string url, CancellationToken token)
        {
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return bytes;
            }
        }

        public void Dispose() { client.Dispose(); }
    }
}
