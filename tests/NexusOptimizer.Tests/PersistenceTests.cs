using System.Text.Json;
using NexusOptimizer.Core;

namespace NexusOptimizer.Tests;

public sealed class PersistenceTests
{
    [Fact] public async Task MissingFileReturnsDefaultsWithoutCreatingAnything()
    {
        using var d = new TestDirectory(); var store = new JsonStore<AppPreferences>(d.File("prefs.json"), () => new());
        Assert.Equal("Escuro", (await store.LoadAsync()).Theme); Assert.False(File.Exists(store.Path));
    }
    [Fact] public async Task PreferencesRoundTripAcrossInstances()
    {
        using var d = new TestDirectory(); var first = new JsonStore<AppPreferences>(d.File("prefs.json"), () => new());
        var expected = new AppPreferences { Theme = "Claro", Accent = "Roxo", MonitorIntervalSeconds = 5, Notifications = false, GamePath = @"C:\Jogos\Jogo.exe" };
        await first.SaveAsync(expected);
        var second = new JsonStore<AppPreferences>(first.Path, () => new()); Assert.Equal(expected, await second.LoadAsync());
    }
    [Theory] [InlineData("{")] [InlineData("null")] [InlineData("")]
    public async Task CorruptFileIsNotSilentlyReplaced(string invalid)
    {
        using var d = new TestDirectory(); string path = d.File("prefs.json"); await File.WriteAllTextAsync(path, invalid);
        var store = new JsonStore<AppPreferences>(path, () => new());
        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAsync()); Assert.Equal(invalid, await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task CanceledSavePreservesPreviousFile()
    {
        using var d = new TestDirectory(); var store = new JsonStore<int>(d.File("data.json"), () => 0);
        await store.SaveAsync(17); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(42, cancellation.Token));
        Assert.Equal(17, await store.LoadAsync()); Assert.Single(Directory.GetFiles(d.Path));
    }
    [Fact] public async Task ConcurrentUpdatesDoNotLoseWrites()
    {
        using var d = new TestDirectory(); var store = new JsonStore<int>(d.File("data.json"), () => 0);
        await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => store.UpdateAsync(n => n + 1)));
        Assert.Equal(40, await store.LoadAsync());
    }
    [Fact] public void InvalidPreferencesAreClampedAndNormalized()
    {
        var value = new AppPreferences { Theme = "invalid", Accent = "invalid", MonitorIntervalSeconds = -100 }.Validated();
        Assert.Equal("Escuro", value.Theme); Assert.Equal("Ciano", value.Accent); Assert.Equal(1, value.MonitorIntervalSeconds);
        Assert.Equal(30, (value with { MonitorIntervalSeconds = 100 }).Validated().MonitorIntervalSeconds);
    }
}
