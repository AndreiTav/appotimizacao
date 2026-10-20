using NexusOptimizer.Core;

namespace NexusOptimizer.Tests;

public sealed class CleanupTests
{
    private sealed class DeleteFile : IFileDeletion
    {
        public bool Locked;
        public void Delete(CleanupFile file, string allowedRoot) { if (Locked) throw new IOException("Arquivo em uso"); File.Delete(file.Path); }
    }
    private static CleanupService Service(TestDirectory d, IFileDeletion? deletion = null) => new([new("temp", "Temp", d.File("temp"), "*.tmp", "")], deletion ?? new DeleteFile());
    private static HashSet<string> Selected => ["temp"];
    private static string AddFile(TestDirectory d, string name, bool old = true)
    {
        Directory.CreateDirectory(d.File("temp")); string path = d.File("temp/" + name);
        File.WriteAllText(path, "test data"); if (old) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2)); return path;
    }
    [Fact] public async Task ScanOnlyIncludesAllowedOldFiles()
    {
        using var d = new TestDirectory(); AddFile(d, "old.tmp"); AddFile(d, "recent.tmp", false); AddFile(d, "personal.txt");
        var scan = await Service(d).ScanAsync(Selected);
        Assert.Equal("old.tmp", Path.GetFileName(Assert.Single(scan.Files).Path)); Assert.Equal(9, scan.TotalBytes);
    }
    [Fact] public async Task DeleteRemovesOnlyAnalyzedSelectedFiles()
    {
        using var d = new TestDirectory(); string path = AddFile(d, "old.tmp"); var service = Service(d); var scan = await service.ScanAsync(Selected);
        string addedLater = AddFile(d, "later.tmp"); var result = await service.DeleteAsync(scan, Selected);
        Assert.Equal(1, result.Deleted); Assert.False(File.Exists(path)); Assert.True(File.Exists(addedLater));
    }
    [Fact] public async Task FileChangedSinceScanIsSkipped()
    {
        using var d = new TestDirectory(); string path = AddFile(d, "old.tmp"); var service = Service(d); var scan = await service.ScanAsync(Selected);
        await File.WriteAllTextAsync(path, "new and important"); var result = await service.DeleteAsync(scan, Selected);
        Assert.Equal(0, result.Deleted); Assert.Equal(1, result.Skipped); Assert.True(File.Exists(path));
    }
    [Fact] public async Task LockedFilesYieldPartialResultAndStayOnDisk()
    {
        using var d = new TestDirectory(); string path = AddFile(d, "old.tmp"); var service = Service(d, new DeleteFile { Locked = true });
        var result = await service.DeleteAsync(await service.ScanAsync(Selected), Selected);
        Assert.Equal(1, result.Skipped); Assert.NotEmpty(result.Errors); Assert.True(File.Exists(path));
    }
    [Fact] public async Task InjectedPathOutsideRootIsRejected()
    {
        using var d = new TestDirectory(); string path = d.File("personal.tmp"); await File.WriteAllTextAsync(path, "private"); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        var info = new FileInfo(path); var service = Service(d);
        var result = await service.DeleteAsync(new([new("temp", path, info.Length, info.LastWriteTimeUtc)], []), Selected);
        Assert.Equal(1, result.Skipped); Assert.True(File.Exists(path));
    }
    [Fact] public async Task UnselectedCategoryCannotDeleteFiles()
    {
        using var d = new TestDirectory(); string path = AddFile(d, "old.tmp"); var service = Service(d);
        Assert.Equal(0, (await service.DeleteAsync(await service.ScanAsync(Selected), new HashSet<string>())).Deleted); Assert.True(File.Exists(path));
    }
    [Fact] public async Task CancellationReturnsAccuratePartialOutcome()
    {
        using var d = new TestDirectory(); AddFile(d, "a.tmp"); AddFile(d, "b.tmp"); var service = Service(d); var scan = await service.ScanAsync(Selected);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var result = await service.DeleteAsync(scan, Selected, token: cancel.Token); Assert.Equal(0, result.Deleted); Assert.Equal(2, result.Skipped);
    }
    [Fact] public async Task CanceledScanDoesNotPretendToComplete()
    {
        using var d = new TestDirectory(); AddFile(d, "a.tmp"); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(d).ScanAsync(Selected, cancel.Token));
    }
    [Fact] public async Task SymlinkDirectoriesAreNotTraversed()
    {
        using var d = new TestDirectory(); AddFile(d, "old.tmp"); Directory.CreateDirectory(d.File("outside"));
        string personal = d.File("outside/personal.tmp"); await File.WriteAllTextAsync(personal, "personal"); File.SetLastWriteTimeUtc(personal, DateTime.UtcNow.AddDays(-2));
        await CreateDirectoryLinkAsync(d.File("temp/link"), d.File("outside"));
        var scan = await Service(d).ScanAsync(Selected); Assert.Single(scan.Files); Assert.True(File.Exists(personal));
    }
    [Fact] public async Task SymlinkRootIsRejected()
    {
        using var d = new TestDirectory(); Directory.CreateDirectory(d.File("outside"));
        await CreateDirectoryLinkAsync(d.File("temp"), d.File("outside"));
        var scan = await Service(d).ScanAsync(Selected); Assert.Empty(scan.Files); Assert.NotEmpty(scan.Warnings);
    }
    [Fact] public void PrefixSiblingIsNotWithinAllowedRoot()
    {
        using var d = new TestDirectory(); Assert.False(PathSafety.IsWithin(d.File("temp-other/file.tmp"), d.File("temp")));
        Assert.False(PathSafety.IsWithin(d.File("temp/../document.tmp"), d.File("temp"))); Assert.False(PathSafety.IsWithin(d.File("temp"), d.File("temp")));
    }
    private static async Task CreateDirectoryLinkAsync(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        // Junctions do not require Developer Mode / symbolic-link privileges on Windows.
        var result = await new ProcessRunner().RunAsync(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/c", $"mklink /J \"{link}\" \"{target}\""], TimeSpan.FromSeconds(10));
        result.EnsureSuccess();
    }
}
