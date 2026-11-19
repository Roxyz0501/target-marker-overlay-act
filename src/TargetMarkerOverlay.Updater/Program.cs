using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;

namespace TargetMarkerOverlay.Updater
{
    public static class Installer
    {
        private static int Main(string[] args)
        {
            var language = "en";
            try
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                if (args.Length != 16) throw new ArgumentException("Invalid arguments.");
                for (var i = 0; i < args.Length; i += 2) values.Add(args[i], args[i + 1]);
                language = values["--language"];
                var source = Path.GetFullPath(values["--source"]);
                var target = Path.GetFullPath(values["--target"]);
                ValidatePaths(source, target);
                using (var gate = new MutexLease(target))
                {
                    if (!gate.Acquired) throw new IOException("An update is already pending.");
                    VerifyHash(source, values["--sha256"]);
                    ValidateSourceAssembly(source, values["--version"]);
                    WaitForExit(int.Parse(values["--wait-pid"], CultureInfo.InvariantCulture), long.Parse(values["--start-ticks"], CultureInfo.InvariantCulture));
                    ValidateSourceAssembly(source, values["--version"]);
                    VerifyHash(target, values["--original"]);
                    ReplaceWithBackup(source, target, values["--sha256"], values["--original"]);
                }
                WriteResult(language, true);
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update-error.log"), ex.ToString()); } catch { }
                WriteResult(language, false);
                return 1;
            }
        }

        private static void ValidateSourceAssembly(string path, string version)
        {
            var identity = System.Reflection.AssemblyName.GetAssemblyName(path);
            if (identity.Name != "TargetMarkerOverlay" || identity.Version.ToString(3) != version ||
                FileVersionInfo.GetVersionInfo(path).CompanyName != "Roxyz0501") throw new InvalidDataException("Unexpected staged assembly.");
        }

        public static void ValidatePaths(string source, string target)
        {
            if (!Path.IsPathRooted(source) || !Path.IsPathRooted(target) ||
                !string.Equals(Path.GetFileName(source), "TargetMarkerOverlay.dll", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(target), "TargetMarkerOverlay.dll", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid plugin paths.");
            if (!File.Exists(source) || !File.Exists(target)) throw new FileNotFoundException("Plugin DLL is missing.");
            RejectReparsePoints(source); RejectReparsePoints(target);
        }

        private static void RejectReparsePoints(string path)
        {
            for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse paths are not supported.");
        }

        public static void WaitForExit(int pid, long startTicks)
        {
            Process process;
            try { process = Process.GetProcessById(pid); } catch (ArgumentException) { return; }
            using (process)
            {
                if (process.StartTime.ToUniversalTime().Ticks != startTicks) return; // PID was reused.
                if (!process.WaitForExit((int)TimeSpan.FromHours(24).TotalMilliseconds))
                    throw new TimeoutException("ACT did not exit within 24 hours; update cancelled.");
            }
        }

        public static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static void VerifyHash(string path, string hash)
        {
            if (hash == null || hash.Length != 64 || !string.Equals(Hash(path), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("File hash mismatch.");
        }

        public static string ReplaceWithBackup(string source, string target, string expectedHash, string originalHash, Action<string, string, string> replace = null)
        {
            ValidatePaths(source, target);
            VerifyHash(source, expectedHash); VerifyHash(target, originalHash);
            var suffix = Guid.NewGuid().ToString("N");
            var incoming = target + ".incoming-" + suffix;
            var backup = target + ".backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + suffix;
            try
            {
                File.Copy(source, incoming, false);
                VerifyHash(incoming, expectedHash);
                File.Copy(target, backup, false);
                VerifyHash(backup, originalHash);
                VerifyHash(target, originalHash);
                try
                {
                    if (replace == null) File.Replace(incoming, target, null);
                    else replace(incoming, target, backup);
                    VerifyHash(target, expectedHash);
                }
                catch
                {
                    // Backup is retained even if restoration fails (e.g. disk/permission failure).
                    File.Copy(backup, target, true);
                    VerifyHash(target, originalHash);
                    throw;
                }
                return backup;
            }
            finally { if (File.Exists(incoming)) File.Delete(incoming); }
        }

        private static void WriteResult(string language, bool success)
        {
            var text = language == "ja" ? "更新が完了しました。ACTを再起動してください。" : language == "zh-CN" ? "更新完成。请重新启动ACT。" : language == "ko" ? "업데이트가 완료되었습니다. ACT를 다시 시작하세요." : "Update complete. Restart ACT.";
            if (!success) text = language == "ja" ? "更新に失敗しました。update-error.logと登録先のバックアップを確認してください。" : language == "zh-CN" ? "更新失败。请检查update-error.log和安装目录中的备份。" : language == "ko" ? "업데이트에 실패했습니다. update-error.log와 설치 폴더의 백업을 확인하세요." : "Update failed. Check update-error.log and the backup in the installation folder.";
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update-result.txt"), text); } catch { }
        }

        private sealed class MutexLease : IDisposable
        {
            private readonly System.Threading.Mutex mutex;
            public bool Acquired { get; private set; }
            public MutexLease(string target)
            {
                string name;
                using (var sha = SHA256.Create()) name = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(target.ToUpperInvariant()))).Replace("-", "");
                mutex = new System.Threading.Mutex(false, "Local\\TargetMarkerOverlayUpdate-" + name);
                try { Acquired = mutex.WaitOne(0); } catch (System.Threading.AbandonedMutexException) { Acquired = true; }
            }
            public void Dispose() { if (Acquired) mutex.ReleaseMutex(); mutex.Dispose(); }
        }
    }
}
