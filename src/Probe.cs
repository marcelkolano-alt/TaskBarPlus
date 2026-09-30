using System;
using System.IO;
namespace TaskbarPlus
{
    internal static class Probe
    {
        [STAThread] static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            File.WriteAllText(args[0], Native.Diagnose());
        }
    }
}
