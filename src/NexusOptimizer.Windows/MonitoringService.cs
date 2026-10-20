using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using NexusOptimizer.Core;

namespace NexusOptimizer.Windows;

public sealed class MonitoringService
{
    private ulong lastIdle, lastTotal;
    private readonly Dictionary<int, (DateTime Start, TimeSpan Cpu, long Tick)> previous = [];
    private string cpu = "Não disponível", gpu = "Não disponível";
    private double? mhz;
    private DateTime lastIdentity = DateTime.MinValue;

    // Called serially by the UI's awaited sampling loop; WMI and process enumeration stay off the UI thread.
    public Task<HardwareSnapshot> SampleAsync(CancellationToken token = default) => Task.Run(() =>
    {
        List<string> errors = [];
        if (DateTime.UtcNow - lastIdentity > TimeSpan.FromSeconds(30))
        {
            ReadWmi("SELECT Name, CurrentClockSpeed FROM Win32_Processor", obj =>
            { cpu = obj["Name"]?.ToString()?.Trim() ?? "Não disponível"; mhz = obj["CurrentClockSpeed"] is null ? null : Convert.ToDouble(obj["CurrentClockSpeed"]); }, errors);
            ReadWmi("SELECT Name FROM Win32_VideoController", obj => { gpu = obj["Name"]?.ToString() ?? "Não disponível"; }, errors);
            lastIdentity = DateTime.UtcNow;
        }
        token.ThrowIfCancellationRequested();
        double? usage = null;
        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong total = kernel.Value + user.Value;
            if (lastTotal != 0 && total > lastTotal)
                usage = Math.Clamp(100d * (1 - (double)(idle.Value - lastIdle) / (total - lastTotal)), 0, 100);
            lastTotal = total; lastIdle = idle.Value;
        }
        else errors.Add("Utilização da CPU não disponível.");
        var memory = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        if (!Native.GlobalMemoryStatusEx(ref memory)) errors.Add("Memória física não disponível.");
        List<DriveSnapshot> drives = [];
        foreach (var drive in DriveInfo.GetDrives())
        {
            try { if (drive.IsReady && drive.DriveType == DriveType.Fixed) drives.Add(new(drive.Name, drive.TotalSize, drive.AvailableFreeSpace)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { errors.Add($"Unidade {drive.Name}: {e.Message}"); }
        }
        List<ProcessSnapshot> processSnapshots = []; HashSet<int> seen = [];
        var processes = Process.GetProcesses();
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    int id = process.Id; var start = process.StartTime; var time = process.TotalProcessorTime;
                    long tick = Stopwatch.GetTimestamp(); double? cpuUse = null;
                    if (previous.TryGetValue(id, out var old) && old.Start == start)
                    {
                        double elapsed = Stopwatch.GetElapsedTime(old.Tick, tick).TotalMilliseconds;
                        if (elapsed > 0) cpuUse = Math.Clamp(100 * (time - old.Cpu).TotalMilliseconds / elapsed / Environment.ProcessorCount, 0, 100);
                    }
                    previous[id] = (start, time, tick); seen.Add(id);
                    processSnapshots.Add(new(id, process.ProcessName, process.WorkingSet64, cpuUse, start));
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        foreach (int id in previous.Keys.Where(k => !seen.Contains(k)).ToArray()) previous.Remove(id);
        return new HardwareSnapshot(cpu, usage, mhz, (long)memory.TotalPhysical, (long)memory.AvailablePhysical,
            gpu, $"Windows {(Environment.OSVersion.Version.Build >= 22000 ? "11" : "(não suportado)")} · build {Environment.OSVersion.Version.Build}",
            TimeSpan.FromMilliseconds(Environment.TickCount64), processes.Length, drives,
            processSnapshots.OrderByDescending(p => p.Cpu ?? 0).ThenByDescending(p => p.Memory).Take(12).ToArray(), errors);
    }, token);

    private static void ReadWmi(string query, Action<ManagementObject> read, List<string> errors)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\cimv2"), new ObjectQuery(query),
                new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(3), ReturnImmediately = false });
            using var results = searcher.Get();
            foreach (ManagementObject item in results) using (item) { read(item); break; }
        }
        catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException)
        { errors.Add($"Sensor WMI indisponível: {e.Message}"); }
    }
}
