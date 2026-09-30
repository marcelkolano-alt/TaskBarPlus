using System;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;

namespace TaskbarPlus
{
    internal static class SelfTest
    {
        internal static int RenderPreview()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TaskbarPlus.MainWindow.xaml"))
            {
                var window = (Window)XamlReader.Load(stream);
                // Render the actual WPF content without taking a desktop screenshot.
                ((CheckBox)window.FindName("CenterToggle")).IsChecked = true;
                ((CheckBox)window.FindName("DisplaysToggle")).IsChecked = true;
                ((CheckBox)window.FindName("StartupToggle")).IsChecked = WindowsPreferences.Startup;
                ((TextBlock)window.FindName("Status")).Text = "Layout preview · live state is checked separately";
                var root = (FrameworkElement)window.Content;
                ((Grid)root).Background = new SolidColorBrush(Color.FromRgb(16, 19, 24));
                root.Measure(new Size(840, 805)); root.Arrange(new Rect(0, 0, 840, 805)); root.UpdateLayout();
                var image = new RenderTargetBitmap(840, 805, 96, 96, PixelFormats.Pbgra32);
                image.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                Directory.CreateDirectory(Settings.Folder);
                using (var file = File.Create(Path.Combine(Settings.Folder, "layout-preview.png"))) encoder.Save(file);
                window.Close();
                return 0;
            }
        }

        internal static int Integration()
        {
            var result = new StringBuilder();
            int failures = 0;
            Action<string, bool> check = delegate(string name, bool ok) { result.AppendLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; };
            var before = new Dictionary<IntPtr, Native.Point>();
            foreach (var bar in Native.Taskbars())
            {
                IntPtr list = Native.Descendant(bar, "MSTaskListWClass");
                if (list == IntPtr.Zero) continue;
                Native.Rect rect; Native.GetWindowRect(list, out rect);
                var point = new Native.Point { X = rect.Left, Y = rect.Top };
                Native.ScreenToClient(Native.GetParent(list), ref point);
                before.Add(list, point);
            }
            check("Discovered classic taskbars", before.Count > 0);
            using (var engine = new TaskbarEngine())
            {
                var settings = new Settings();
                engine.Apply(settings); Thread.Sleep(250);
                check("Clear effect accepted on all displays", !engine.HadError);
                check("Found button bounds on all displays", engine.CenteredCount == before.Count);
                foreach (var bar in Native.Taskbars())
                {
                    IntPtr list = Native.Descendant(bar, "MSTaskListWClass");
                    if (list == IntPtr.Zero) continue;
                    Native.Rect bounds; Native.GetWindowRect(bar, out bounds);
                    var buttons = Native.Buttons(list, null);
                    if (buttons.Count == 0) continue;
                    double actualCenter = (buttons.Min(b => b.Left) + buttons.Max(b => b.Right)) / 2.0;
                    double screenCenter = (bounds.Left + bounds.Right) / 2.0;
                    check("Live group centered within 1px on " + Native.Class(bar), Math.Abs(actualCenter - screenCenter) <= 1);
                }
                foreach (var effect in new[] { "Blur", "Acrylic", "Opaque", "Default" })
                {
                    settings.Effect = effect; settings.Opacity = 35;
                    engine.Apply(settings); Thread.Sleep(150);
                    check(effect + " native effect applied", !engine.HadError);
                }
                settings.Paused = true; engine.Apply(settings); Thread.Sleep(250);
                foreach (var pair in before)
                {
                    Native.Rect rect; Native.GetWindowRect(pair.Key, out rect);
                    var point = new Native.Point { X = rect.Left, Y = rect.Top };
                    Native.ScreenToClient(Native.GetParent(pair.Key), ref point);
                    check("Pause restores original button position " + pair.Key, point.X == pair.Value.X && point.Y == pair.Value.Y);
                }
                settings.Paused = false; settings.Effect = "Clear"; engine.Apply(settings); Thread.Sleep(200);
                check("Resume recenters all displays", engine.CenteredCount == before.Count && !engine.HadError);
            }
            Thread.Sleep(250);
            foreach (var pair in before)
            {
                Native.Rect rect; Native.GetWindowRect(pair.Key, out rect);
                var point = new Native.Point { X = rect.Left, Y = rect.Top };
                Native.ScreenToClient(Native.GetParent(pair.Key), ref point);
                check("Exit restores original button position " + pair.Key, point.X == pair.Value.X && point.Y == pair.Value.Y);
            }
            Directory.CreateDirectory(Settings.Folder);
            result.AppendLine("Failures: " + failures);
            File.WriteAllText(Path.Combine(Settings.Folder, "integration-results.txt"), result.ToString());
            return failures == 0 ? 0 : 1;
        }

        internal static int Run()
        {
            var results = new StringBuilder();
            int failures = 0;
            Action<string, bool> check = delegate(string name, bool ok) { results.AppendLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; };
            check("One fully busy core on four processors reports 25% CPU", Math.Abs(ResourceUsage.CalculatePercent(5000, 5000, 4) - 25) < 0.001);
            check("An empty CPU sampling interval does not divide by zero", ResourceUsage.CalculatePercent(0, 0, 4) == 0);
            using (var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TaskbarPlus.Icon"))
            {
                var decoder = BitmapDecoder.Create(iconStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                check("Icon includes all nine Windows display sizes", decoder.Frames.Select(f => f.PixelWidth).OrderBy(n => n).SequenceEqual(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }));
            }
            // Primary monitor has a 384px Start/Search area and 34 app buttons.
            check("Primary monitor centers the button group", TaskbarEngine.ComputeTarget(-384, 3440, 2659, 3, 1666, 0) == 500);
            check("Secondary monitor uses its own screen center", TaskbarEngine.ComputeTarget(-144, 2560, 2352, 3, 1666, 0) == 300);
            check("Crowded taskbar never shifts buttons outside the parent", TaskbarEngine.ComputeTarget(-384, 1920, 1100, 3, 1300, 0) == 0);
            check("Offset cannot overlap the system tray", TaskbarEngine.ComputeTarget(-48, 1920, 1200, 3, 1000, 200) == 197);
            check("Negative offset clamps safely", TaskbarEngine.ComputeTarget(-384, 1024, 500, 3, 450, -200) == 0);
            check("Vertical taskbar uses the same bounded geometry", TaskbarEngine.ComputeTarget(-40, 1080, 950, 0, 400, 0) == 300);
            var settings = new Settings { Effect = "invalid", Color = "bad", Opacity = 1000, Offset = -9999 };
            settings.Validate();
            check("Invalid stored settings recover safely", settings.Effect == "Clear" && settings.Color == "#17202D" && settings.Opacity == 100 && settings.Offset == -200);
            var saved = new Settings { Effect = "Acrylic", Color = "#123456", Opacity = 43, Center = false, Offset = 75, AllDisplays = false, Paused = true };
            var serializer = new XmlSerializer(typeof(Settings));
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, saved); stream.Position = 0;
                var loaded = (Settings)serializer.Deserialize(stream);
                check("All preferences survive a save/load round trip", loaded.Effect == saved.Effect && loaded.Color == saved.Color && loaded.Opacity == saved.Opacity && loaded.Center == saved.Center && loaded.Offset == saved.Offset && loaded.AllDisplays == saved.AllDisplays && loaded.Paused == saved.Paused);
            }
            Directory.CreateDirectory(Settings.Folder);
            results.AppendLine("Failures: " + failures);
            File.WriteAllText(Path.Combine(Settings.Folder, "test-results.txt"), results.ToString());
            return failures == 0 ? 0 : 1;
        }
    }
}
