namespace NexusOptimizer.Core;

public enum RiskLevel { Low, Moderate, High }
public enum OperationStatus { Prepared, Applied, Failed, Reverted }
public sealed record OperationInfo(string Id, string Name, string Description, string Benefit,
    string SideEffects, RiskLevel Risk = RiskLevel.Low, bool RequiresAdmin = false, bool RequiresRestart = false);
public sealed record ChangeRecord(Guid Id, string OperationId, string Name, string Before, string After,
    DateTimeOffset CreatedAt, OperationStatus Status, string? Error = null, DateTimeOffset? RevertedAt = null);
public sealed record OperationResult(bool Changed, string Message);

public interface IReversibleOperation
{
    OperationInfo Info { get; }
    Task<string> ReadAsync(CancellationToken token);
    Task WriteAsync(string state, CancellationToken token);
    void Validate(string state);
}

public sealed record AppPreferences
{
    public string Theme { get; init; } = "Escuro";
    public string Accent { get; init; } = "Ciano";
    public int MonitorIntervalSeconds { get; init; } = 2;
    public bool Notifications { get; init; } = true;
    public string? GamePath { get; init; }
    public AppPreferences Validated() => this with
    {
        Theme = Theme is "Escuro" or "Claro" ? Theme : "Escuro",
        Accent = Accent is "Ciano" or "Roxo" or "Azul" ? Accent : "Ciano",
        MonitorIntervalSeconds = Math.Clamp(MonitorIntervalSeconds, 1, 30)
    };
}

public sealed record DriveSnapshot(string Name, long Total, long Free)
{
    public double UsedPercent => Total > 0 ? 100d * (Total - Free) / Total : 0;
    public string Summary => $"{Format.Bytes(Free)} livres de {Format.Bytes(Total)}";
}
public sealed record ProcessSnapshot(int Id, string Name, long Memory, double? Cpu, DateTime StartTime)
{
    public string Summary => $"PID {Id} · RAM {Format.Bytes(Memory)} · CPU {(Cpu is null ? "Não disponível" : $"{Cpu:F1}%")}";
}
public sealed record HardwareSnapshot(string CpuName, double? CpuUsage, double? CpuMhz,
    long TotalMemory, long AvailableMemory, string GpuName, string OsName, TimeSpan Uptime,
    int ProcessCount, IReadOnlyList<DriveSnapshot> Drives, IReadOnlyList<ProcessSnapshot> Processes,
    IReadOnlyList<string> Errors)
{
    public double MemoryUsage => TotalMemory > 0 ? 100d * (TotalMemory - AvailableMemory) / TotalMemory : 0;
}
public static class Format
{
    public static string Bytes(long n)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, n); int i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value:F1} {units[i]}";
    }
}
