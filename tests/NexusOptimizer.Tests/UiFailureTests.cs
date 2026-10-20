using System.Xml.Linq;
using NexusOptimizer.Core;

namespace NexusOptimizer.Tests;

public sealed class UiFailureTests
{
    [Fact] public void UnexpectedFailureNotifiesOnceAndStopsOnSubsequentFailures()
    {
        int logged = 0, recovered = 0, stopped = 0;
        var handler = new UiFailureHandler(_ => logged++, _ => recovered++, () => stopped++);
        for (int i = 0; i < 100; i++) handler.Report(new InvalidOperationException("Read-only binding"));
        Assert.Equal(1, logged); Assert.Equal(1, recovered); Assert.Equal(1, stopped);
    }

    [Fact] public void ReentrantNotificationDoesNotRecursivelyNotify()
    {
        int recovered = 0, stopped = 0; UiFailureHandler? handler = null;
        handler = new(_ => { }, error => { recovered++; handler!.Report(error); }, () => stopped++);
        handler.Report(new InvalidOperationException("UI error"));
        Assert.Equal(1, recovered); Assert.Equal(0, stopped);
    }

    [Fact] public void RecoveryFailureStopsOnceWithoutTryingToRenderAgain()
    {
        int stopped = 0;
        var handler = new UiFailureHandler(_ => { }, _ => throw new InvalidOperationException("Recovery view failed"), () => stopped++);
        handler.Report(new IOException("Original failure"));
        handler.Report(new IOException("Subsequent failure"));
        Assert.Equal(1, stopped);
    }

    [Fact] public void LoggingFailureDoesNotSuppressRecovery()
    {
        var original = new InvalidOperationException("Original failure"); Exception? received = null;
        var handler = new UiFailureHandler(_ => throw new IOException("Log denied"), error => received = error, () => { });
        handler.Report(original); Assert.Same(original, received);
    }

    [Fact] public async Task ConcurrentFailuresCannotCreateMultipleRecoveryViews()
    {
        int recovered = 0, stopped = 0;
        var handler = new UiFailureHandler(_ => { }, _ => Interlocked.Increment(ref recovered), () => Interlocked.Increment(ref stopped));
        await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => handler.Report(new InvalidOperationException("UI error")))));
        Assert.Equal(1, recovered); Assert.InRange(stopped, 0, 1);
    }

    [Fact] public void DashboardAndTaskProgressBindingsExplicitlyPreventSourceWrites()
    {
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var xaml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainWindow.xaml"));
        var bars = xaml.Descendants(ns + "ProgressBar").Select(e => (string?)e.Attribute("Value")).ToArray();
        Assert.Equal(2, bars.Length);
        Assert.All(bars, expression => Assert.Contains("Mode=OneWay", expression));
        Assert.Contains(bars, expression => expression!.Contains("UsedPercent", StringComparison.Ordinal));
    }
}
