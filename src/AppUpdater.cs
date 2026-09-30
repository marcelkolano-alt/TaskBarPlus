using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml.Serialization;

namespace TaskbarPlus
{
    internal sealed class ReleaseInfo
    {
        internal Version Version;
        internal string Tag, AssetName, DownloadUrl, Digest;
    }
    public sealed class UpdatePlan
    {
        public string Target, PreviousHash, CandidateHash, Version;
        public int ParentId;
        public long ParentStartTicks;
    }
    internal static class AppUpdater
    {
        internal const string Repository = "https://github.com/marcelkolano-alt/TaskBarPlus";
        const string LatestApi = "https://api.github.com/repos/marcelkolano-alt/TaskBarPlus/releases/latest";
        internal static readonly string UpdatesFolder = Path.Combine(Settings.Folder, "Updates");
        internal static Version CurrentVersion { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
        internal static string CurrentLabel { get { return CurrentVersion.ToString(3); } }
        internal static Version ParseVersion(string text)
        {
            if (text == null || !Regex.IsMatch(text, "^v?[0-9]+\\.[0-9]+\\.[0-9]+(\\.[0-9]+)?$")) throw new InvalidDataException("The release version is invalid.");
            var version = new Version(text.TrimStart('v'));
            return new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
        }
        internal static ReleaseInfo ParseRelease(string json)
        {
            var data = new JavaScriptSerializer { MaxJsonLength = 1048576 }.Deserialize<Dictionary<string, object>>(json);
            if (Convert.ToBoolean(data["draft"]) || Convert.ToBoolean(data["prerelease"])) throw new InvalidDataException("The release is not a stable public build.");
            string tag = Convert.ToString(data["tag_name"]);
            var version = ParseVersion(tag);
            string assetName = "TaskBarPlus-" + tag.TrimStart('v') + "-windows-x64.zip";
            foreach (var item in (System.Collections.IEnumerable)data["assets"])
            {
                var asset = (Dictionary<string, object>)item;
                if (Convert.ToString(asset["name"]) != assetName) continue;
                string url = Convert.ToString(asset["browser_download_url"]);
                string expected = Repository + "/releases/download/" + tag + "/" + assetName;
                if (!String.Equals(url, expected, StringComparison.Ordinal)) throw new InvalidDataException("The update is not hosted in the TaskBar+ repository.");
                object digestObject;
                string digest = asset.TryGetValue("digest", out digestObject) ? Convert.ToString(digestObject) : "";
                if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$")) throw new InvalidDataException("This release has no GitHub SHA-256 checksum. Download it from the release page instead.");
                return new ReleaseInfo { Tag = tag, Version = version, AssetName = assetName, DownloadUrl = url, Digest = digest.Substring(7).ToLowerInvariant() };
            }
            throw new InvalidDataException("This release does not contain a Windows x64 build.");
        }
        internal static Task<ReleaseInfo> CheckAsync()
        {
            return Task.Run(delegate
            {
                using (var bytes = new MemoryStream())
                {
                    Download(LatestApi, bytes, 1048576);
                    return ParseRelease(Encoding.UTF8.GetString(bytes.ToArray()));
                }
            });
        }
        static void Download(string address, Stream destination, long limit)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Uri uri = new Uri(address);
            for (int redirect = 0; redirect < 6; redirect++)
            {
                if (uri.Scheme != "https" || !(uri.Host == "api.github.com" || uri.Host == "github.com" || uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com"))
                    throw new InvalidDataException("Unexpected update download destination.");
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.UserAgent = "TaskBarPlus/" + CurrentLabel;
                request.Accept = "application/vnd.github+json";
                request.Timeout = 20000; request.ReadWriteTimeout = 20000; request.AllowAutoRedirect = false;
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                    {
                        uri = new Uri(uri, response.Headers["Location"]); continue;
                    }
                    if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > limit) throw new InvalidDataException("The update response is invalid or too large.");
                    using (var source = response.GetResponseStream()) CopyLimited(source, destination, limit);
                    return;
                }
            }
            throw new InvalidDataException("Too many update redirects.");
        }
        internal static void CopyLimited(Stream source, Stream destination, long limit)
        {
            byte[] buffer = new byte[65536]; long total = 0; int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > limit) throw new InvalidDataException("The update exceeds the allowed size.");
                destination.Write(buffer, 0, read);
            }
        }
        internal static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        internal static void VerifyHash(string path, string expected)
        {
            if (!String.Equals(Hash(path), expected, StringComparison.Ordinal)) throw new InvalidDataException("The download checksum did not match GitHub. Nothing was installed.");
        }
        internal static void ExtractPayload(string archivePath, string folder)
        {
            using (var file = File.OpenRead(archivePath))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
            {
                foreach (var name in new[] { "TaskBarPlus.exe", "assets/TaskBarPlus.ico" })
                {
                    var matches = zip.Entries.Where(e => e.FullName.Replace('\\', '/') == name).ToArray();
                    if (matches.Length != 1) throw new InvalidDataException("The update package is incomplete or ambiguous.");
                    long limit = name.EndsWith(".exe") ? 32 * 1024 * 1024 : 1024 * 1024;
                    if (matches[0].Length > limit) throw new InvalidDataException("An update file is too large.");
                    // Fixed destinations: never extract arbitrary archive paths or scripts.
                    string output = Path.Combine(folder, name.EndsWith(".exe") ? "candidate.exe" : "candidate.ico");
                    using (var input = matches[0].Open())
                    using (var target = File.Create(output)) CopyLimited(input, target, limit);
                }
            }
        }
        internal static Task<string> PrepareAsync(ReleaseInfo release)
        {
            string target = Assembly.GetExecutingAssembly().Location;
            return Task.Run(delegate
            {
                CleanupStaging();
                string folder = Path.Combine(UpdatesFolder, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                try
                {
                    string archive = Path.Combine(folder, "package.zip");
                    using (var file = File.Create(archive)) Download(release.DownloadUrl, file, 64 * 1024 * 1024);
                    VerifyHash(archive, release.Digest);
                    ExtractPayload(archive, folder);
                    string candidate = Path.Combine(folder, "candidate.exe");
                    var name = AssemblyName.GetAssemblyName(candidate);
                    if (name.Name != "TaskBarPlus" || name.Version != release.Version) throw new InvalidDataException("The downloaded application version does not match the release.");
                    using (var current = Process.GetCurrentProcess())
                    {
                        var plan = new UpdatePlan { Target = target, ParentId = current.Id, ParentStartTicks = current.StartTime.ToUniversalTime().Ticks,
                            PreviousHash = Hash(target), CandidateHash = Hash(candidate), Version = release.Version.ToString() };
                        using (var file = File.Create(Path.Combine(folder, "plan.xml"))) new XmlSerializer(typeof(UpdatePlan)).Serialize(file, plan);
                    }
                    File.Copy(target, Path.Combine(folder, "UpdateHelper.exe"));
                    return folder;
                }
                catch { DeleteStaging(folder); throw; }
            });
        }
        internal static void LaunchInstaller(string folder)
        {
            if (!ValidStaging(folder)) throw new InvalidDataException("Invalid update staging folder.");
            Process.Start(new ProcessStartInfo(Path.Combine(folder, "UpdateHelper.exe"), "--apply-update") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder });
        }
        internal static bool ValidStaging(string folder)
        {
            string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            Guid id;
            return String.Equals(Path.GetDirectoryName(full), Path.GetFullPath(UpdatesFolder), StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParseExact(Path.GetFileName(full), "N", out id) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0;
        }
        static void DeleteStaging(string folder)
        {
            try { if (Directory.Exists(folder) && ValidStaging(folder)) Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        internal static void CleanupStaging()
        {
            try
            {
                if (!Directory.Exists(UpdatesFolder)) return;
                foreach (var folder in Directory.GetDirectories(UpdatesFolder))
                    if (ValidStaging(folder) && (File.Exists(Path.Combine(folder, "complete")) || Directory.GetLastWriteTimeUtc(folder) < DateTime.UtcNow.AddDays(-7))) DeleteStaging(folder);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        internal static string ReadyEvent(string token) { return @"Local\TaskBarPlus.Update." + token; }
        internal static void SignalReady(string token)
        {
            Guid id;
            if (!Guid.TryParseExact(token, "N", out id)) return;
            try { using (var ready = EventWaitHandle.OpenExisting(ReadyEvent(token))) ready.Set(); } catch (WaitHandleCannotBeOpenedException) { }
        }
        internal static int Install()
        {
            string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string target = null, backup = null, iconTarget = null, iconBackup = null, newFile = null, newIcon = null;
            bool replaced = false, iconReplaced = false, iconCreated = false, started = false, parentStopped = false;
            Process launched = null;
            try
            {
                if (!ValidStaging(folder)) throw new InvalidDataException("The updater must run from its staging folder.");
                UpdatePlan plan;
                using (var file = File.OpenRead(Path.Combine(folder, "plan.xml"))) plan = (UpdatePlan)new XmlSerializer(typeof(UpdatePlan)).Deserialize(file);
                target = Path.GetFullPath(plan.Target);
                if (!String.Equals(Path.GetFileName(target), "TaskBarPlus.exe", StringComparison.OrdinalIgnoreCase) || Hash(target) != plan.PreviousHash)
                    throw new InvalidDataException("The installed application changed during the update.");
                string candidate = Path.Combine(folder, "candidate.exe");
                if (Hash(candidate) != plan.CandidateHash || AssemblyName.GetAssemblyName(candidate).Version != ParseVersion(plan.Version)) throw new InvalidDataException("The staged update is invalid.");
                if (ParseVersion(plan.Version) < AssemblyName.GetAssemblyName(target).Version) throw new InvalidDataException("The updater will not install an older version.");
                try
                {
                    using (var parent = Process.GetProcessById(plan.ParentId))
                    {
                        if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStartTicks) throw new InvalidDataException("The update process identity changed.");
                        if (!parent.WaitForExit(20000)) throw new IOException("TaskBar+ is still running. Close it and try again.");
                    }
                }
                catch (ArgumentException) { } // Parent already exited normally.
                parentStopped = true;
                string suffix = "." + Path.GetFileName(folder);
                newFile = target + suffix + ".new"; backup = target + suffix + ".previous";
                File.Copy(candidate, newFile);
                File.Replace(newFile, target, backup); replaced = true;
                iconTarget = Path.Combine(Path.GetDirectoryName(target), "TaskBarPlus.ico");
                iconBackup = iconTarget + suffix + ".previous";
                if (File.Exists(iconTarget))
                {
                    newIcon = iconTarget + suffix + ".new";
                    File.Copy(Path.Combine(folder, "candidate.ico"), newIcon);
                    File.Replace(newIcon, iconTarget, iconBackup); iconReplaced = true;
                }
                else { File.Copy(Path.Combine(folder, "candidate.ico"), iconTarget); iconCreated = true; }
                string token = Path.GetFileName(folder);
                using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEvent(token)))
                {
                    launched = Process.Start(new ProcessStartInfo(target, "--updated " + token) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) });
                    if (!ready.WaitOne(25000)) throw new IOException("The new version did not start successfully.");
                }
                started = true;
                File.Delete(backup);
                if (iconReplaced) File.Delete(iconBackup);
                File.WriteAllText(Path.Combine(folder, "complete"), "Updated to " + plan.Version);
                return 0;
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
                if (!started)
                {
                    try
                    {
                        if (launched != null && !launched.HasExited) { launched.Kill(); launched.WaitForExit(5000); }
                        if (replaced && File.Exists(backup)) File.Replace(backup, target, null);
                        if (iconReplaced && File.Exists(iconBackup)) File.Replace(iconBackup, iconTarget, null);
                        else if (iconCreated) File.Delete(iconTarget);
                        if (parentStopped) Process.Start(target);
                    }
                    catch (Exception rollback) { Settings.Log(rollback); }
                }
                System.Windows.MessageBox.Show("The update could not finish. " + ex.Message + "\nYour settings have been kept.", "TaskBar+ update");
                return 1;
            }
            finally
            {
                if (launched != null) launched.Dispose();
                try { if (newFile != null && File.Exists(newFile)) File.Delete(newFile); } catch (IOException) { }
                try { if (newIcon != null && File.Exists(newIcon)) File.Delete(newIcon); } catch (IOException) { }
            }
        }
    }
}
