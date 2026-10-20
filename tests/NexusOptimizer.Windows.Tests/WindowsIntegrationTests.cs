using System.IO;
using System.Text.Json;
using NexusOptimizer.Core;
using NexusOptimizer.Windows;

namespace NexusOptimizer.Windows.Tests;

/// <summary>Real native APIs. Run on Windows; they are not claimed as executed on Linux.</summary>
public sealed class WindowsIntegrationTests
{
    [Fact] public async Task MemoryAndCpuUseRealNativeMeasurements()
    {
        var monitor = new MonitoringService(); var first = await monitor.SampleAsync();
        await Task.Delay(200); var second = await monitor.SampleAsync();
        Assert.True(first.TotalMemory > 0); Assert.InRange(first.AvailableMemory, 0, first.TotalMemory);
        Assert.NotNull(second.CpuUsage); Assert.InRange(second.CpuUsage!.Value, 0, 100); Assert.True(second.ProcessCount > 0);
    }
    [Fact] public void ActivePlanIsAnExistingPlanAndUnknownPlansAreRejected()
    {
        var power = new PowerService(); Assert.Contains(power.Plans(), p => p.Id == power.Active());
        Assert.Throws<InvalidOperationException>(() => power.Validate(Guid.Empty.ToString()));
    }
    [Fact] public async Task MouseStateHasThreeRealSpiValues()
    {
        var mouse = new MouseOperation(); string state = await mouse.ReadAsync(default); mouse.Validate(state);
        Assert.Equal(3, JsonSerializer.Deserialize<int[]>(state)!.Length);
    }
    [Fact] public void StartupModuleLoadsWithoutChangingEntries()
    {
        var entries = new StartupService().Read(); Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Source)));
    }
    [Fact] public async Task NativeDeletionDeletesOldFileAndSkipsLockedFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "nexus-native-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string free = Path.Combine(root, "free.tmp"), locked = Path.Combine(root, "locked.tmp");
            foreach (string p in new[] { free, locked }) { await File.WriteAllTextAsync(p, "safe temporary test"); File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddDays(-2)); }
            var service = new CleanupService([new("test", "Test", root, "*.tmp", "")], new WindowsFileDeletion());
            var scan = await service.ScanAsync(new HashSet<string> { "test" });
            using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var result = await service.DeleteAsync(scan, new HashSet<string> { "test" });
            Assert.Equal(1, result.Deleted); Assert.Equal(1, result.Skipped); Assert.False(File.Exists(free)); Assert.True(File.Exists(locked));
        }
        finally { Directory.Delete(root, true); }
    }
}
