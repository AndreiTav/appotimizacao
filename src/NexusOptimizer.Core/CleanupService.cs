namespace NexusOptimizer.Core;

public sealed record CleanupRule(string Id, string Name, string Root, string Pattern, string Warning, bool RequiresAdmin = false);
public sealed record CleanupFile(string RuleId, string Path, long Length, DateTime LastWriteUtc);
public sealed record CleanupScan(IReadOnlyList<CleanupFile> Files, IReadOnlyList<string> Warnings)
{
    public long TotalBytes => Files.Sum(x => x.Length);
}
public sealed record CleanupResult(int Deleted, int Skipped, long FreedBytes, IReadOnlyList<string> Errors);
public interface IFileDeletion { void Delete(CleanupFile file, string allowedRoot); }

public static class PathSafety
{
    private static readonly StringComparison Comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static bool IsWithin(string path, string root)
    {
        string prefix = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root)) + System.IO.Path.DirectorySeparatorChar;
        return System.IO.Path.GetFullPath(path).StartsWith(prefix, Comparison);
    }
    public static void AssertNoReparsePoints(string path)
    {
        string? current = System.IO.Path.GetFullPath(path);
        while (current is not null)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Links simbólicos e junções não são permitidos na limpeza.");
            current = System.IO.Path.GetDirectoryName(current);
        }
    }
}

public sealed class CleanupService(IReadOnlyList<CleanupRule> rules, IFileDeletion deletion)
{
    public IReadOnlyList<CleanupRule> Rules => rules;
    public Task<CleanupScan> ScanAsync(IReadOnlySet<string> selected, CancellationToken token = default) => Task.Run(() =>
    {
        List<CleanupFile> files = []; List<string> warnings = [];
        DateTime cutoff = DateTime.UtcNow.AddHours(-24);
        foreach (var rule in rules.Where(r => selected.Contains(r.Id)))
        {
            if (!Directory.Exists(rule.Root)) continue;
            var stack = new Stack<string>(); stack.Push(rule.Root);
            while (stack.TryPop(out var directory))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    PathSafety.AssertNoReparsePoints(directory);
                    foreach (string path in Directory.EnumerateFiles(directory, rule.Pattern, SearchOption.TopDirectoryOnly))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            PathSafety.AssertNoReparsePoints(path);
                            var info = new FileInfo(path);
                            if (info.LastWriteTimeUtc >= cutoff || (info.Attributes & (FileAttributes.System | FileAttributes.ReadOnly)) != 0) continue;
                            files.Add(new(rule.Id, info.FullName, info.Length, info.LastWriteTimeUtc));
                            if (files.Count >= 100_000) return new CleanupScan(files, [.. warnings, "Limite de 100.000 arquivos atingido; analise novamente após a limpeza."]);
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AddWarning(warnings, e.Message); }
                    }
                    foreach (string child in Directory.EnumerateDirectories(directory))
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) stack.Push(child);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AddWarning(warnings, e.Message); }
            }
        }
        return new CleanupScan(files, warnings);
    }, token);

    public Task<CleanupResult> DeleteAsync(CleanupScan scan, IReadOnlySet<string> selected, IProgress<int>? progress = null,
        CancellationToken token = default) => Task.Run(() =>
    {
        int deleted = 0, skipped = 0, done = 0; long freed = 0; List<string> errors = [];
        var candidates = scan.Files.Where(f => selected.Contains(f.RuleId)).ToArray();
        foreach (var file in candidates)
        {
            // Return the partial outcome on cancellation; deletions cannot be rolled back.
            if (token.IsCancellationRequested) { skipped += candidates.Length - done; break; }
            try
            {
                var rule = rules.Single(r => r.Id == file.RuleId);
                if (!PathSafety.IsWithin(file.Path, rule.Root) || !System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(rule.Pattern, System.IO.Path.GetFileName(file.Path), OperatingSystem.IsWindows()))
                    throw new IOException("Arquivo fora das regras permitidas.");
                PathSafety.AssertNoReparsePoints(file.Path);
                var info = new FileInfo(file.Path);
                if (info.Length != file.Length || info.LastWriteTimeUtc != file.LastWriteUtc || info.LastWriteTimeUtc >= DateTime.UtcNow.AddHours(-24))
                    throw new IOException("Arquivo alterado após a análise; ignorado.");
                if ((info.Attributes & (FileAttributes.System | FileAttributes.ReadOnly)) != 0) throw new IOException("Arquivo protegido; ignorado.");
                deletion.Delete(file, rule.Root);
                deleted++; freed += file.Length;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            { skipped++; AddWarning(errors, $"{System.IO.Path.GetFileName(file.Path)}: {e.Message}"); }
            done++; progress?.Report(candidates.Length == 0 ? 100 : 100 * done / candidates.Length);
        }
        return new CleanupResult(deleted, skipped, freed, errors);
    }, CancellationToken.None);
    private static void AddWarning(List<string> warnings, string warning) { if (warnings.Count < 100) warnings.Add(warning); }
}
