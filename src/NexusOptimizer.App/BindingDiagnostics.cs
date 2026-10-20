using System.Diagnostics;

namespace NexusOptimizer.App;

internal sealed class BindingDiagnostics : TraceListener
{
    public static List<string> Errors { get; } = [];
    public override void Write(string? message) { }
    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message);
    }
}
