using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskbarPlus
{
    internal sealed class TaskbarEngine : IDisposable
    {
        sealed class OriginalPosition { internal IntPtr Parent; internal int X, Y; }
        readonly Dictionary<IntPtr, OriginalPosition> positions = new Dictionary<IntPtr, OriginalPosition>();
        readonly HashSet<IntPtr> styled = new HashSet<IntPtr>();
        internal string Status = "Waiting for the Windows taskbar...";
        internal int DisplayCount;
        internal int CenteredCount;
        internal bool HadError;

        internal void Apply(Settings settings)
        {
            HadError = false;
            if (settings.Paused) { Restore(); Status = "Paused · Windows appearance restored"; return; }
            var bars = Native.Taskbars();
            DisplayCount = bars.Count; CenteredCount = 0;
            foreach (var stale in positions.Keys.Where(w => !Native.IsWindow(w)).ToArray()) positions.Remove(stale);
            styled.RemoveWhere(w => !Native.IsWindow(w));
            foreach (var bar in bars)
            {
                bool selected = settings.AllDisplays || Native.Class(bar) == "Shell_TrayWnd";
                IntPtr list = Native.Descendant(bar, "MSTaskListWClass");
                if (!selected) { RestoreBar(bar, list); continue; }
                if (settings.Effect == "Default")
                {
                    if (styled.Remove(bar)) Native.RestoreStyle(bar);
                }
                else
                {
                    uint rgb = Convert.ToUInt32(settings.Color.Substring(1), 16);
                    uint alpha = (uint)Math.Round(settings.Opacity * 255.0 / 100);
                    int state = settings.Effect == "Clear" ? 2 : settings.Effect == "Blur" ? 3 : settings.Effect == "Acrylic" ? 4 : 1;
                    if (state == 1) alpha = 255;
                    // Acrylic needs nonzero alpha on Windows 10 to retain its material.
                    if (state == 4) alpha = Math.Max(1, alpha);
                    uint abgr = (alpha << 24) | ((rgb & 255) << 16) | (rgb & 0xFF00) | (rgb >> 16);
                    if (!Native.Style(bar, state, abgr)) HadError = true;
                    else styled.Add(bar);
                }
                if (list == IntPtr.Zero) continue;
                if (!settings.Center) { RestorePosition(list); continue; }
                if (Center(bar, list, settings.Offset)) CenteredCount++;
            }
            if (bars.Count == 0) Status = "Waiting for the Windows taskbar...";
            else if (HadError) Status = "Windows could not apply one of the effects";
            else if (settings.Center && CenteredCount == 0) Status = "Appearance applied · waiting for app buttons";
            else Status = "Active on " + (settings.AllDisplays ? bars.Count : 1) + " display" + ((settings.AllDisplays ? bars.Count : 1) == 1 ? "" : "s") + " · " + (settings.Center ? "Apps centered" : "Windows alignment");
        }

        // Center the real button bounds, not the oversized task-list window. Clamp
        // inside its parent so Search, custom toolbars and the tray remain usable.
        bool Center(IntPtr bar, IntPtr list, int offset)
        {
            if (!Native.IsWindowVisible(list)) return false;
            Native.Rect barRect, listRect, parentRect;
            IntPtr parent = Native.GetParent(list);
            if (!Native.GetWindowRect(bar, out barRect) || !Native.GetWindowRect(list, out listRect) || !Native.GetClientRect(parent, out parentRect)) return false;
            var origin = new Native.Point { X = listRect.Left, Y = listRect.Top };
            Native.ScreenToClient(parent, ref origin);
            if (!positions.ContainsKey(list)) positions.Add(list, new OriginalPosition { Parent = parent, X = origin.X, Y = origin.Y });
            var buttons = Native.Buttons(list, null);
            if (buttons.Count == 0) { RestorePosition(list); return false; }
            bool horizontal = barRect.Width >= barRect.Height;
            int first = horizontal ? buttons.Min(r => r.Left) : buttons.Min(r => r.Top);
            int last = horizontal ? buttons.Max(r => r.Right) : buttons.Max(r => r.Bottom);
            int localInset = first - (horizontal ? listRect.Left : listRect.Top);
            int groupSize = last - first;
            var parentOrigin = new Native.Point { X = barRect.Left, Y = barRect.Top };
            Native.ScreenToClient(parent, ref parentOrigin);
            int target = ComputeTarget(horizontal ? parentOrigin.X : parentOrigin.Y,
                horizontal ? barRect.Width : barRect.Height, horizontal ? parentRect.Width : parentRect.Height,
                localInset, groupSize, offset);
            int x = horizontal ? target : origin.X, y = horizontal ? origin.Y : target;
            if (Math.Abs(origin.X - x) > 1 || Math.Abs(origin.Y - y) > 1)
                if (!Native.SetWindowPos(list, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010 | 0x4000)) HadError = true;
            return true;
        }

        internal static int ComputeTarget(int barOrigin, int barLength, int available, int inset, int size, int offset)
        {
            int desired = barOrigin + (barLength - size) / 2 - inset + offset;
            int maximum = Math.Max(0, available - size - inset);
            return Math.Max(0, Math.Min(maximum, desired));
        }
        void RestorePosition(IntPtr list)
        {
            OriginalPosition old;
            if (positions.TryGetValue(list, out old))
            {
                if (Native.IsWindow(list) && Native.GetParent(list) == old.Parent)
                    Native.SetWindowPos(list, IntPtr.Zero, old.X, old.Y, 0, 0, 0x0001 | 0x0004 | 0x0010 | 0x4000);
                positions.Remove(list);
            }
        }
        void RestoreBar(IntPtr bar, IntPtr list)
        {
            if (styled.Remove(bar) && Native.IsWindow(bar)) Native.RestoreStyle(bar);
            RestorePosition(list);
        }
        internal void Restore()
        {
            foreach (var bar in styled.ToArray()) if (Native.IsWindow(bar)) Native.RestoreStyle(bar);
            styled.Clear();
            foreach (var list in positions.Keys.ToArray()) RestorePosition(list);
        }
        public void Dispose() { Restore(); }
    }
}
