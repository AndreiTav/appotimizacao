using NexusOptimizer.Core;

namespace NexusOptimizer.Tests;

public sealed class ProcessRunnerTests
{
    private static (string Exe, string[] Args) Shell(string unix, string windows) => OperatingSystem.IsWindows()
        ? (Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", windows]) : ("/bin/sh", ["-c", unix]);
    [Fact] public async Task CapturesBothStreamsAndPreservesFailureCode()
    {
        var command = Shell("printf output; printf failure >&2; exit 7", "echo output & echo failure 1>&2 & exit /b 7");
        var result = await new ProcessRunner().RunAsync(command.Exe, command.Args, TimeSpan.FromSeconds(10));
        Assert.Equal(7, result.ExitCode); Assert.Contains("output", result.Output); Assert.Contains("failure", result.Error); Assert.Throws<IOException>(result.EnsureSuccess);
    }
    [Fact] public async Task MissingCommandFailsClearly()
    { await Assert.ThrowsAnyAsync<Exception>(() => new ProcessRunner().RunAsync("nexus-command-that-does-not-exist", [], TimeSpan.FromSeconds(1))); }
    [Fact] public async Task PreCanceledCommandIsNeverStarted()
    {
        using var directory = new TestDirectory(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        string marker = directory.File("must-not-exist");
        var command = Shell($"touch '{marker}'", $"echo executed >\"{marker}\"");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProcessRunner().RunAsync(command.Exe, command.Args, TimeSpan.FromSeconds(5), cancellation.Token));
        Assert.False(File.Exists(marker));
    }
    [Fact] public async Task TimeoutKillsProcessAndReturnsNoFalseSuccess()
    {
        var command = Shell("sleep 20", "ping -n 21 127.0.0.1 >nul");
        await Assert.ThrowsAsync<TimeoutException>(() => new ProcessRunner().RunAsync(command.Exe, command.Args, TimeSpan.FromMilliseconds(150)));
    }
    [Fact] public async Task UserCancellationIsDistinctFromTimeout()
    {
        var command = Shell("sleep 20", "ping -n 21 127.0.0.1 >nul"); using var cancellation = new CancellationTokenSource(150);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProcessRunner().RunAsync(command.Exe, command.Args, TimeSpan.FromSeconds(10), cancellation.Token));
    }
    [Fact] public async Task LargeOutputIsDrainedAndBounded()
    {
        var command = Shell("yes nexus | head -c 150000; yes error | head -c 150000 >&2", "for /l %i in (1,1,16000) do @(echo nexus-output & echo error-output 1>&2)");
        var result = await new ProcessRunner().RunAsync(command.Exe, command.Args, TimeSpan.FromSeconds(20));
        result.EnsureSuccess(); Assert.Equal(65536, result.Output.Length); Assert.Equal(65536, result.Error.Length);
    }
}
