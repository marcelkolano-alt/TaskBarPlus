using System;
using System.Diagnostics;
using System.Globalization;

namespace TaskbarPlus
{
    // One cached process handle, sampled every five seconds. No history, counters,
    // files, network requests, or forced working-set trimming.
    internal sealed class ResourceUsage : IDisposable
    {
        readonly Process process = Process.GetCurrentProcess();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly int processors = Math.Max(1, Environment.ProcessorCount);
        double previousWall, previousCpu;
        internal bool HasCpuSample { get; private set; }
        internal double CpuPercent { get; private set; }
        internal double MemoryMiB { get; private set; }
        internal int ProcessId { get { return process.Id; } }

        internal ResourceUsage()
        {
            previousCpu = process.TotalProcessorTime.TotalMilliseconds;
            MemoryMiB = process.WorkingSet64 / 1048576.0;
        }
        internal bool Sample()
        {
            double wall = clock.Elapsed.TotalMilliseconds;
            if (wall - previousWall < 5000) return false;
            process.Refresh();
            double cpu = process.TotalProcessorTime.TotalMilliseconds;
            CpuPercent = CalculatePercent(cpu - previousCpu, wall - previousWall, processors);
            MemoryMiB = process.WorkingSet64 / 1048576.0;
            previousCpu = cpu; previousWall = wall; HasCpuSample = true;
            return true;
        }
        internal static double CalculatePercent(double cpuMilliseconds, double elapsedMilliseconds, int logicalProcessors)
        {
            if (elapsedMilliseconds <= 0 || logicalProcessors < 1) return 0;
            return Math.Max(0, Math.Min(100, cpuMilliseconds / elapsedMilliseconds / logicalProcessors * 100));
        }
        internal string CpuText { get { return HasCpuSample ? CpuPercent.ToString("0.00", CultureInfo.CurrentCulture) + "%" : "measuring"; } }
        internal string MemoryText { get { return MemoryMiB.ToString("0.0", CultureInfo.CurrentCulture) + " MB"; } }
        internal string Summary { get { return "CPU " + CpuText + "  |  RAM " + MemoryText; } }
        internal string TrayText
        {
            get
            {
                string text = "TaskBar+\n" + Summary;
                return text.Length <= 63 ? text : text.Substring(0, 63);
            }
        }
        public void Dispose() { process.Dispose(); }
    }
}
