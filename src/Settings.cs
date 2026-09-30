using System;
using System.IO;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace TaskbarPlus
{
    public class Settings
    {
        public string Effect = "Clear";
        public string Color = "#17202D";
        public int Opacity = 0;
        public bool Center = true;
        public int Offset = 0;
        public bool Paused = false;
        public bool AllDisplays = true;
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskBarPlus");
        public static readonly string FilePath = Path.Combine(Folder, "settings.xml");
        public static Settings Load()
        {
            try { using (var stream = File.OpenRead(FilePath)) { var s = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(stream); s.Validate(); return s; } }
            catch (Exception) { return new Settings(); }
        }
        public void Validate()
        {
            if (Array.IndexOf(new[] { "Clear", "Blur", "Acrylic", "Opaque", "Default" }, Effect) < 0) Effect = "Clear";
            Opacity = Math.Max(0, Math.Min(100, Opacity));
            Offset = Math.Max(-200, Math.Min(200, Offset));
            if (Color == null || !System.Text.RegularExpressions.Regex.IsMatch(Color, "^#[0-9A-Fa-f]{6}$")) Color = "#17202D";
        }
        public void Save()
        {
            Validate(); Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            using (var stream = File.Create(temp)) new XmlSerializer(typeof(Settings)).Serialize(stream, this);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null); else File.Move(temp, FilePath);
        }
        public static void Log(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, "app.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256000) File.WriteAllText(path, "");
                File.AppendAllText(path, DateTime.Now.ToString("s") + " " + ex.ToString() + Environment.NewLine);
            }
            catch { }
        }
    }

    internal static class WindowsPreferences
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ExplorerKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        internal static bool Startup
        {
            get { using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) return key != null && key.GetValue("TaskBarPlus") != null; }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) key.SetValue("TaskBarPlus", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" --background");
                    else key.DeleteValue("TaskBarPlus", false);
                }
            }
        }
        internal static bool Seconds
        {
            get { using (var key = Registry.CurrentUser.OpenSubKey(ExplorerKey)) return key != null && Convert.ToInt32(key.GetValue("ShowSecondsInSystemClock", 0)) == 1; }
            set { using (var key = Registry.CurrentUser.CreateSubKey(ExplorerKey)) key.SetValue("ShowSecondsInSystemClock", value ? 1 : 0, RegistryValueKind.DWord); }
        }
    }
}
