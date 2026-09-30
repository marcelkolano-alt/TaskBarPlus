using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

[assembly: AssemblyTitle("TaskBar+")]
[assembly: AssemblyDescription("Local taskbar transparency and centering for Windows 10")]
[assembly: AssemblyVersion("1.1.0.0")]

namespace TaskbarPlus
{
    internal sealed class Program
    {
        Settings settings;
        readonly TaskbarEngine engine = new TaskbarEngine();
        Application app;
        Window window;
        Forms.NotifyIcon tray;
        ResourceUsage usage;
        Forms.ToolStripMenuItem cpuItem, memoryItem;
        DispatcherTimer timer, saveTimer;
        EventWaitHandle showEvent, stopEvent;
        RegisteredWaitHandle showWait, stopWait;
        bool updating, exiting;
        string lastError;
        string updateStatus = "Updates come from the public TaskBar+ GitHub releases.";
        bool updateBusy;
        ReleaseInfo availableUpdate;
        const string InstanceName = @"Local\TaskBarPlus.Desktop.v1";

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Contains("--apply-update")) return AppUpdater.Install();
            if (args.Contains("--test-updater"))
            {
                try { return UpdaterTests.Run(); }
                catch (Exception ex) { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(Path.Combine(Settings.Folder, "updater-test-results.txt"), ex.ToString()); return 1; }
            }
            if (args.Contains("--diagnose")) { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(Path.Combine(Settings.Folder, "diagnostic.txt"), Native.Diagnose()); return 0; }
            if (args.Contains("--self-test")) return SelfTest.Run();
            if (args.Contains("--integration-test"))
            {
                bool testOwnsMutex;
                using (var testMutex = new Mutex(true, InstanceName, out testOwnsMutex))
                {
                    if (!testOwnsMutex) return 2; // Never race the running tray app.
                    try { return SelfTest.Integration(); }
                    finally { testMutex.ReleaseMutex(); }
                }
            }
            if (args.Contains("--render-preview")) return SelfTest.RenderPreview();
            if (args.Contains("--quit")) { Signal(InstanceName + ".Stop"); return 0; }
            bool created;
            using (var mutex = new Mutex(true, InstanceName, out created))
            {
                if (!created) { if (!args.Contains("--background")) Signal(InstanceName + ".Show"); return 0; }
                var program = new Program();
                try { program.Run(args.Contains("--background"), args.Length == 2 && args[0] == "--updated" ? args[1] : null); return 0; }
                catch (Exception ex) { Settings.Log(ex); if (!args.Contains("--background")) MessageBox.Show("TaskBar+ could not start. " + ex.Message, "TaskBar+"); return 1; }
                finally { program.Cleanup(); mutex.ReleaseMutex(); }
            }
        }
        static void Signal(string name) { try { using (var signal = EventWaitHandle.OpenExisting(name)) signal.Set(); } catch (WaitHandleCannotBeOpenedException) { } }

        void Run(bool background, string updatedToken)
        {
            int build;
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) int.TryParse(Convert.ToString(key.GetValue("CurrentBuildNumber")), out build);
            if (build < 17763 || build >= 22000) throw new NotSupportedException("This version is made for the classic Windows 10 taskbar (1809 or later).");
            settings = Settings.Load();
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { Settings.Log(e.Exception); lastError = e.Exception.Message; engine.Restore(); settings.Paused = true; e.Handled = true; Refresh(); };
            app.SessionEnding += delegate { Save(); Cleanup(); };
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Show");
            stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Stop");
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, delegate { app.Dispatcher.BeginInvoke(new Action(Show)); }, null, -1, false);
            stopWait = ThreadPool.RegisterWaitForSingleObject(stopEvent, delegate { app.Dispatcher.BeginInvoke(new Action(Quit)); }, null, -1, true);
            CreateTray();
            usage = new ResourceUsage();
            UpdateUsage();
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += delegate { Apply(); if (usage.Sample()) UpdateUsage(); };
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            saveTimer.Tick += delegate { saveTimer.Stop(); Save(); };
            Apply(); timer.Start();
            if (!background) Show();
            if (updatedToken != null) AppUpdater.SignalReady(updatedToken);
            var cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            cleanupTimer.Tick += delegate { cleanupTimer.Stop(); System.Threading.Tasks.Task.Run(new Action(AppUpdater.CleanupStaging)); };
            cleanupTimer.Start();
            app.Run();
        }
        void CreateTray()
        {
            tray = new Forms.NotifyIcon { Icon = MakeIcon(), Text = "TaskBar+", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open TaskBar+", null, delegate { Show(); });
            menu.Items.Add("Pause / resume", null, delegate { TogglePause(); });
            var resourceMenu = new Forms.ToolStripMenuItem("Resource usage");
            cpuItem = new Forms.ToolStripMenuItem("CPU: measuring...") { Enabled = false };
            memoryItem = new Forms.ToolStripMenuItem("RAM: measuring...") { Enabled = false };
            resourceMenu.DropDownItems.Add(cpuItem);
            resourceMenu.DropDownItems.Add(memoryItem);
            resourceMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Updates every 5 seconds") { Enabled = false });
            resourceMenu.DropDownItems.Add("Open Task Manager", null, delegate { System.Diagnostics.Process.Start("taskmgr.exe"); });
            menu.Items.Add(resourceMenu);
            menu.Items.Add("Updates...", null, delegate { Show(); });
            menu.Opening += delegate { if (usage != null && usage.Sample()) UpdateUsage(); };
            menu.Items.Add(new Forms.ToolStripSeparator());
            foreach (string effect in new[] { "Clear", "Blur", "Acrylic", "Opaque", "Default" })
            {
                string chosen = effect;
                menu.Items.Add(effect == "Opaque" ? "Solid" : effect == "Default" ? "Windows default" : effect, null, delegate { settings.Effect = chosen; settings.Paused = false; Changed(); Refresh(); });
            }
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit and restore taskbar", null, delegate { Quit(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Show(); };
        }
        static Drawing.Icon MakeIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TaskbarPlus.Icon"))
            using (var icon = new Drawing.Icon(stream, new Drawing.Size(32, 32)))
                return (Drawing.Icon)icon.Clone();
        }
        T Control<T>(string name) where T : class { return window.FindName(name) as T; }
        void Show()
        {
            if (exiting) return;
            if (window == null)
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TaskbarPlus.MainWindow.xaml")) window = (Window)XamlReader.Load(stream);
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TaskbarPlus.Icon"))
                {
                    window.Icon = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    window.Icon.Freeze();
                }
                // ShutdownMode is explicit: closing releases the window and controls,
                // while the tray/engine continue. Recreate the UI only when requested.
                window.Closed += delegate { window = null; app.MainWindow = null; };
                foreach (string mode in new[] { "Clear", "Blur", "Acrylic", "Opaque", "Default" })
                {
                    string selected = mode;
                    Control<Button>(mode).Click += delegate { settings.Effect = selected; settings.Paused = false; Changed(); Refresh(); };
                }
                Control<Slider>("OpacitySlider").ValueChanged += delegate { if (!updating) { settings.Opacity = (int)Control<Slider>("OpacitySlider").Value; Changed(); Preview(); } };
                Control<Slider>("OffsetSlider").ValueChanged += delegate { if (!updating) { settings.Offset = (int)Control<Slider>("OffsetSlider").Value; Changed(); Preview(); } };
                BindToggle("CenterToggle", delegate(bool value) { settings.Center = value; });
                BindToggle("DisplaysToggle", delegate(bool value) { settings.AllDisplays = value; });
                BindToggle("StartupToggle", delegate(bool value) { WindowsPreferences.Startup = value; });
                BindToggle("SecondsToggle", delegate(bool value) { WindowsPreferences.Seconds = value; });
                Control<Button>("PauseButton").Click += delegate { TogglePause(); };
                Control<Button>("ResetButton").Click += delegate { settings = new Settings { Effect = "Default", Center = false }; Changed(); Refresh(); };
                Control<Button>("HideButton").Click += delegate { window.Close(); };
                Control<Button>("UpdateButton").Click += delegate { CheckForUpdate(); };
                Control<Button>("ColorButton").Click += delegate
                {
                    using (var picker = new Forms.ColorDialog { FullOpen = true, Color = Drawing.ColorTranslator.FromHtml(settings.Color) })
                    {
                        if (picker.ShowDialog(new WindowOwner(window)) == Forms.DialogResult.OK) { settings.Color = "#" + picker.Color.R.ToString("X2") + picker.Color.G.ToString("X2") + picker.Color.B.ToString("X2"); Changed(); Refresh(); }
                    }
                };
                Theme("ThemeGlass", "Clear", "#17202D", 0);
                Theme("ThemeFrost", "Blur", "#DAE7EF", 20);
                Theme("ThemeMidnight", "Acrylic", "#131C2F", 65);
                Theme("ThemeSage", "Blur", "#397D6C", 40);
            }
            Refresh(); UpdateUsage(); RefreshUpdateUi(); window.Show(); window.WindowState = WindowState.Normal; window.Activate();
        }
        async void CheckForUpdate()
        {
            if (updateBusy) return;
            updateBusy = true;
            bool installing = availableUpdate != null;
            updateStatus = installing ? "Downloading and verifying the update..." : "Checking GitHub for the latest stable version...";
            RefreshUpdateUi();
            try
            {
                if (!installing)
                {
                    var release = await AppUpdater.CheckAsync();
                    if (release.Version > AppUpdater.CurrentVersion)
                    {
                        availableUpdate = release;
                        updateStatus = release.Tag + " is available. Installing keeps your settings and restarts the app.";
                    }
                    else updateStatus = "You're up to date · v" + AppUpdater.CurrentLabel;
                }
                else
                {
                    string folder = await AppUpdater.PrepareAsync(availableUpdate);
                    settings.Save();
                    AppUpdater.LaunchInstaller(folder);
                    Quit();
                }
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
                updateStatus = "Update " + (installing ? "installation" : "check") + " failed: " + ex.Message;
            }
            finally { updateBusy = false; if (!exiting) RefreshUpdateUi(); }
        }
        void RefreshUpdateUi()
        {
            if (window == null) return;
            Control<TextBlock>("UpdateStatus").Text = updateStatus;
            Control<TextBlock>("AppVersion").Text = "TaskBar+ v" + AppUpdater.CurrentLabel;
            Control<Button>("UpdateButton").IsEnabled = !updateBusy;
            Control<Button>("UpdateButton").Content = updateBusy ? "Please wait..." : availableUpdate != null ? "Install " + availableUpdate.Tag : "Check for updates";
        }
        sealed class WindowOwner : Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public WindowOwner(Window w) { Handle = new System.Windows.Interop.WindowInteropHelper(w).Handle; }
        }
        void Theme(string button, string effect, string color, int opacity)
        {
            Control<Button>(button).Click += delegate { settings.Effect = effect; settings.Color = color; settings.Opacity = opacity; settings.Paused = false; Changed(); Refresh(); };
        }
        void BindToggle(string name, Action<bool> action)
        {
            RoutedEventHandler handler = delegate
            {
                if (updating) return;
                try { action(Control<CheckBox>(name).IsChecked == true); Changed(); Refresh(); }
                catch (Exception ex) { Settings.Log(ex); lastError = "Could not save this setting: " + ex.Message; Refresh(); }
            };
            Control<CheckBox>(name).Checked += handler; Control<CheckBox>(name).Unchecked += handler;
        }
        void TogglePause() { settings.Paused = !settings.Paused; Changed(); Refresh(); }
        void Changed() { lastError = null; Apply(); saveTimer.Stop(); saveTimer.Start(); }
        void Save() { try { settings.Save(); } catch (Exception ex) { Settings.Log(ex); lastError = "Could not save preferences: " + ex.Message; } }
        void Apply()
        {
            if (timer != null) timer.Interval = TimeSpan.FromSeconds(settings.Paused ? 5 : 1);
            try { engine.Apply(settings); }
            catch (Exception ex) { if (lastError != ex.Message) Settings.Log(ex); lastError = ex.Message; }
            if (window != null)
            {
                var status = Control<TextBlock>("Status");
                string text = lastError ?? engine.Status;
                if (status.Text != text) status.Text = text;
            }
        }
        void UpdateUsage()
        {
            if (usage == null) return;
            tray.Text = usage.TrayText;
            cpuItem.Text = "CPU: " + usage.CpuText + " of system capacity";
            memoryItem.Text = "RAM: " + usage.MemoryText + " (working set)";
            if (window != null)
            {
                Control<TextBlock>("ResourceUsage").Text = usage.Summary + "   ·   PID " + usage.ProcessId;
                Control<TextBlock>("ResourceUsage").ToolTip = "TaskBar+ process usage, sampled every 5 seconds. RAM is the working set. Windows rendering costs in Explorer/DWM are separate.";
            }
        }
        void Refresh()
        {
            if (window == null) return;
            updating = true;
            try
            {
                Control<Slider>("OpacitySlider").Value = settings.Opacity;
                Control<Slider>("OpacitySlider").IsEnabled = settings.Effect != "Default" && settings.Effect != "Opaque";
                Control<Slider>("OffsetSlider").Value = settings.Offset;
                Control<Slider>("OffsetSlider").IsEnabled = settings.Center;
                Control<CheckBox>("CenterToggle").IsChecked = settings.Center;
                Control<CheckBox>("DisplaysToggle").IsChecked = settings.AllDisplays;
                Control<CheckBox>("StartupToggle").IsChecked = WindowsPreferences.Startup;
                Control<CheckBox>("SecondsToggle").IsChecked = WindowsPreferences.Seconds;
                Control<TextBlock>("Badge").Text = settings.Paused ? "PAUSED" : "LIVE";
                Control<Button>("PauseButton").Content = settings.Paused ? "Resume" : "Pause";
                foreach (string effect in new[] { "Clear", "Blur", "Acrylic", "Opaque", "Default" })
                {
                    Control<Button>(effect).Background = Brush(effect == settings.Effect ? "#294B3F" : "#252C38");
                    Control<Button>(effect).BorderBrush = Brush(effect == settings.Effect ? "#6DDCB0" : "#364052");
                }
                Control<TextBlock>("Status").Text = lastError ?? engine.Status;
                Preview();
            }
            finally { updating = false; }
        }
        static SolidColorBrush Brush(string value) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        void Preview()
        {
            Control<TextBlock>("OpacityLabel").Text = settings.Effect == "Opaque" ? "100% (solid)" : settings.Effect == "Default" ? "Windows managed" : settings.Opacity + "%";
            Control<TextBlock>("OffsetLabel").Text = (settings.Offset > 0 ? "+" : "") + settings.Offset + " px";
            Control<TextBlock>("ColorLabel").Text = settings.Color;
            Control<Border>("ColorSwatch").Background = Brush(settings.Color);
            var color = (Color)ColorConverter.ConvertFromString(settings.Color);
            color.A = (byte)(settings.Effect == "Opaque" || settings.Effect == "Default" ? 255 : settings.Opacity * 255 / 100);
            Control<Border>("PreviewBar").Background = new SolidColorBrush(color);
            Control<StackPanel>("PreviewIcons").HorizontalAlignment = settings.Center ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            Control<StackPanel>("PreviewIcons").Margin = settings.Center ? new Thickness(settings.Offset / 3.0, 0, 0, 0) : new Thickness(45, 0, 0, 0);
        }
        void Quit() { if (exiting) return; exiting = true; Save(); Cleanup(); app.Shutdown(); }
        void Cleanup()
        {
            exiting = true;
            if (timer != null) timer.Stop();
            if (saveTimer != null) saveTimer.Stop();
            if (showWait != null) { showWait.Unregister(null); showWait = null; }
            if (stopWait != null) { stopWait.Unregister(null); stopWait = null; }
            if (showEvent != null) { showEvent.Dispose(); showEvent = null; }
            if (stopEvent != null) { stopEvent.Dispose(); stopEvent = null; }
            engine.Dispose();
            if (usage != null) { usage.Dispose(); usage = null; }
            if (tray != null) { tray.Visible = false; var icon = tray.Icon; tray.Dispose(); icon.Dispose(); tray = null; }
        }
    }
}
