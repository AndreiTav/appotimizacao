using System.Text.Json;

namespace NexusOptimizer.Core;

/// <summary>Single-writer atomic replacement. A corrupt file is preserved, never treated as empty.</summary>
public sealed class JsonStore<T>(string path, Func<T> defaultFactory)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public async Task<T> LoadAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { return await LoadUnlockedAsync(token); }
        finally { gate.Release(); }
    }
    public async Task SaveAsync(T value, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { await SaveUnlockedAsync(value, token); }
        finally { gate.Release(); }
    }
    public async Task UpdateAsync(Func<T, T> update, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { await SaveUnlockedAsync(update(await LoadUnlockedAsync(token)), token); }
        finally { gate.Release(); }
    }
    private async Task<T> LoadUnlockedAsync(CancellationToken token)
    {
        if (!File.Exists(Path)) return defaultFactory();
        await using var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<T>(file, Options, token)
            ?? throw new InvalidDataException($"Arquivo JSON vazio ou inválido: {Path}");
    }
    private async Task SaveUnlockedAsync(T value, CancellationToken token)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temp = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(file, value, Options, token);
                await file.FlushAsync(token);
                file.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temp, Path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class FileLog(string directory)
{
    private readonly object gate = new();
    public void Write(string context, Exception? error = null)
    {
        // Logging must never take down error handling. No credentials or process environments are logged.
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(System.IO.Path.Combine(directory, $"nexus-{DateTime.Today:yyyy-MM-dd}.log"),
                    $"{DateTimeOffset.Now:O} {context}{Environment.NewLine}{error}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
