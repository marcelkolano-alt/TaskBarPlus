using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TaskbarPlus
{
    internal static class UpdaterTests
    {
        internal static int Run()
        {
            int failures = 0;
            var report = new StringBuilder();
            Action<string, bool> check = delegate(string name, bool ok) { report.AppendLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; };
            Action<string, Action> rejects = delegate(string name, Action action)
            {
                bool rejected = false;
                try { action(); } catch (InvalidDataException) { rejected = true; }
                check(name, rejected);
            };
            check("Version comparison normalizes missing revision", AppUpdater.ParseVersion("v1.1.0") == new Version(1, 1, 0, 0));
            check("Numeric versions handle double-digit releases", AppUpdater.ParseVersion("v1.10.0") > AppUpdater.ParseVersion("v1.9.0"));
            rejects("Rejects malformed release versions", delegate { AppUpdater.ParseVersion("../../other"); });
            string json = "{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v1.2.0\",\"assets\":[{\"name\":\"TaskBarPlus-1.2.0-windows-x64.zip\",\"browser_download_url\":\"https://github.com/marcelkolano-alt/TaskBarPlus/releases/download/v1.2.0/TaskBarPlus-1.2.0-windows-x64.zip\",\"digest\":\"sha256:" + new string('a', 64) + "\"}]}";
            check("Selects the exact repository/version/architecture asset", AppUpdater.ParseRelease(json).Version == new Version(1, 2, 0, 0));
            rejects("Rejects an update from another repository", delegate { AppUpdater.ParseRelease(json.Replace("marcelkolano-alt/TaskBarPlus", "someone/OtherApp")); });
            rejects("Rejects a release without an integrity checksum", delegate { AppUpdater.ParseRelease(json.Replace("sha256:", "missing:")); });
            rejects("Rejects prerelease builds", delegate { AppUpdater.ParseRelease(json.Replace("\"prerelease\":false", "\"prerelease\":true")); });
            using (var source = new MemoryStream(new byte[20]))
            using (var target = new MemoryStream()) rejects("Bounds downloaded and extracted file sizes", delegate { AppUpdater.CopyLimited(source, target, 10); });
            string folder = Path.Combine(Settings.Folder, "UpdateTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string archive = Path.Combine(folder, "test.zip");
                string corrupt = Path.Combine(folder, "corrupt.zip");
                File.WriteAllText(corrupt, "not the published download");
                rejects("Rejects a checksum mismatch before installation", delegate { AppUpdater.VerifyHash(corrupt, new string('0', 64)); });
                using (var stream = File.Create(archive))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                    foreach (string name in new[] { "TaskBarPlus.exe", "assets/TaskBarPlus.ico", "../../outside.txt", "install.ps1" })
                        using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write("test");
                AppUpdater.ExtractPayload(archive, folder);
                check("Only fixed executable and icon paths are extracted", File.ReadAllText(Path.Combine(folder, "candidate.exe")) == "test" && File.Exists(Path.Combine(folder, "candidate.ico")) && !File.Exists(Path.Combine(folder, "install.ps1")));
                string incomplete = Path.Combine(folder, "incomplete.zip");
                using (var stream = File.Create(incomplete))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create)) zip.CreateEntry("TaskBarPlus.exe");
                rejects("Rejects an incomplete update package", delegate { AppUpdater.ExtractPayload(incomplete, folder); });
                string targetFile = Path.Combine(folder, "app.exe"), newFile = Path.Combine(folder, "new.exe"), backup = Path.Combine(folder, "old.exe");
                File.WriteAllText(targetFile, "old"); File.WriteAllText(newFile, "new");
                File.Replace(newFile, targetFile, backup);
                check("Replacement retains the previous version for rollback", File.ReadAllText(targetFile) == "new" && File.ReadAllText(backup) == "old");
                File.Replace(backup, targetFile, null);
                check("Rollback restores the previous version", File.ReadAllText(targetFile) == "old");
            }
            finally { Directory.Delete(folder, true); }
            report.AppendLine("Failures: " + failures);
            Directory.CreateDirectory(Settings.Folder);
            File.WriteAllText(Path.Combine(Settings.Folder, "updater-test-results.txt"), report.ToString());
            return failures == 0 ? 0 : 1;
        }
    }
}
