using System.Diagnostics;
using System.Text;

namespace NexusOptimizer.Core;

public sealed record CommandResult(int ExitCode, string Output, string Error)
{
    public void EnsureSuccess()
    {
        if (ExitCode != 0) throw new IOException($"Comando falhou (código {ExitCode}). {Error} {Output}");
    }
}
public sealed class ProcessRunner
{
    public async Task<CommandResult> RunAsync(string executable, IEnumerable<string> arguments,
        TimeSpan timeout, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in arguments) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        process.Start();
        // Drain both pipes concurrently, with bounded retained output.
        var stdout = ReadBoundedAsync(process.StandardOutput);
        var stderr = ReadBoundedAsync(process.StandardError);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            throw new TimeoutException("O comando excedeu o tempo limite e foi encerrado.");
        }
        return new(process.ExitCode, await stdout, await stderr);
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var result = new StringBuilder(); char[] buffer = new char[2048]; int n;
        while ((n = await reader.ReadAsync(buffer)) > 0)
            if (result.Length < 65_536) result.Append(buffer, 0, Math.Min(n, 65_536 - result.Length));
        return result.ToString();
    }
}
