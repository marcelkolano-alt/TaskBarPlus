using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Accessibility;

namespace TaskbarPlus
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
            public override string ToString() { return String.Format("{0},{1} {2}x{3}", Left, Top, Width, Height); }
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Accent { public int State, Flags; public uint Color; public int Animation; }
        [StructLayout(LayoutKind.Sequential)] internal struct Composition { public int Attribute; public IntPtr Data; public int Size; }
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr p);
        [DllImport("user32.dll")] internal static extern bool EnumChildWindows(IntPtr hwnd, EnumProc callback, IntPtr p);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] internal static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref Composition data);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("oleacc.dll")] internal static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId, ref Guid iid, [In, Out, MarshalAs(UnmanagedType.Interface)] ref IAccessible accessible);
        [DllImport("oleacc.dll")] internal static extern int AccessibleChildren(IAccessible container, int start, int count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] object[] children, out int obtained);

        internal static string Class(IntPtr hwnd) { var b = new StringBuilder(256); GetClassName(hwnd, b, b.Capacity); return b.ToString(); }
        internal static List<IntPtr> Taskbars()
        {
            var result = new List<IntPtr>();
            EnumWindows(delegate(IntPtr w, IntPtr p) { string c = Class(w); if (c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") result.Add(w); return true; }, IntPtr.Zero);
            return result;
        }
        internal static IntPtr Descendant(IntPtr parent, string className)
        {
            IntPtr result = IntPtr.Zero;
            EnumChildWindows(parent, delegate(IntPtr w, IntPtr p) { if (Class(w) == className) { result = w; return false; } return true; }, IntPtr.Zero);
            return result;
        }
        internal static List<Rect> Buttons(IntPtr taskList, StringBuilder diagnostic)
        {
            var bounds = new List<Rect>();
            IAccessible root = null;
            Guid iid = new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");
            try
            {
                if (AccessibleObjectFromWindow(taskList, 0xFFFFFFFC, ref iid, ref root) != 0 || root == null) return bounds;
                int count = Math.Min(root.accChildCount, 512), obtained;
                object[] children = new object[count];
                AccessibleChildren(root, 0, count, children, out obtained);
                for (int i = 0; i < obtained; i++)
                {
                    IAccessible item = children[i] as IAccessible;
                    try
                    {
                        var owner = item ?? root;
                        object child = item == null ? children[i] : 0;
                        int x, y, w, h;
                        owner.accLocation(out x, out y, out w, out h, child);
                        int state = Convert.ToInt32(owner.get_accState(child));
                        int role = Convert.ToInt32(owner.get_accRole(child));
                        if (diagnostic != null) diagnostic.AppendLine(String.Format("  child {0}: role={1} state={2:X} bounds={3},{4} {5}x{6}", i, role, state, x, y, w, h));
                        if ((state & 0x8000) == 0 && w > 0 && h > 0 && (role == 43 || role == 37 || role == 57 || role == 59))
                            bounds.Add(new Rect { Left = x, Top = y, Right = x + w, Bottom = y + h });
                    }
                    catch (COMException) { }
                    finally { if (item != null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item); }
                }
            }
            catch (COMException) { }
            finally { if (root != null && Marshal.IsComObject(root)) Marshal.ReleaseComObject(root); }
            return bounds;
        }
        internal static bool Style(IntPtr hwnd, int state, uint color)
        {
            var accent = new Accent { State = state, Color = color, Flags = 2 };
            IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Accent)));
            try
            {
                Marshal.StructureToPtr(accent, memory, false);
                var data = new Composition { Attribute = 19, Data = memory, Size = Marshal.SizeOf(typeof(Accent)) };
                return SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        internal static void RestoreStyle(IntPtr hwnd)
        {
            Style(hwnd, 0, 0);
            PostMessage(hwnd, 0x031E, IntPtr.Zero, IntPtr.Zero);
        }
        internal static string Diagnose()
        {
            var s = new StringBuilder();
            foreach (var bar in Taskbars())
            {
                Rect rect; GetWindowRect(bar, out rect);
                s.AppendLine(Class(bar) + " " + bar + " " + rect);
                EnumChildWindows(bar, delegate(IntPtr w, IntPtr p) { Rect r; GetWindowRect(w, out r); s.AppendLine(" " + Class(w) + " " + w + " " + r); return true; }, IntPtr.Zero);
                IntPtr list = Descendant(bar, "MSTaskListWClass");
                if (list != IntPtr.Zero) Buttons(list, s);
            }
            return s.ToString();
        }
    }
}
