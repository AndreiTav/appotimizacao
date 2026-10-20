using NexusOptimizer.Core;

namespace NexusOptimizer.Tests;

public sealed class OptimizationTests
{
    private sealed class Operation : IReversibleOperation
    {
        public OperationInfo Info { get; } = new("test", "Test", "", "", "");
        public string State = "original";
        public int Writes;
        public Func<string, Task>? OnWrite;
        public Task<string> ReadAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(State); }
        public async Task WriteAsync(string state, CancellationToken token) { Writes++; token.ThrowIfCancellationRequested(); if (OnWrite is not null) await OnWrite(state); State = state; }
        public void Validate(string state) { if (state == "invalid") throw new ArgumentException("Invalid"); }
    }
    private static OptimizationEngine Engine(TestDirectory d) => new(new(d.File("history.json"), () => []), new(d.File("logs")));

    [Fact] public async Task OriginalIsDurableBeforeMutation()
    {
        using var d = new TestDirectory(); var engine = Engine(d); var operation = new Operation();
        operation.OnWrite = async _ =>
        {
            var record = Assert.Single(await engine.HistoryAsync());
            Assert.Equal("original", record.Before); Assert.Equal("target", record.After); Assert.Equal(OperationStatus.Prepared, record.Status);
        };
        var result = await engine.ApplyAsync(operation, "target");
        Assert.True(result.Changed); Assert.Equal("target", operation.State);
        Assert.Equal(OperationStatus.Applied, Assert.Single(await engine.HistoryAsync()).Status);
    }
    [Fact] public async Task RepeatedApplyIsIdempotentAndKeepsFirstBackup()
    {
        using var d = new TestDirectory(); var engine = Engine(d); var operation = new Operation();
        await engine.ApplyAsync(operation, "target"); var repeated = await engine.ApplyAsync(operation, "target");
        Assert.False(repeated.Changed); Assert.Equal(1, operation.Writes); Assert.Equal("original", Assert.Single(await engine.HistoryAsync()).Before);
    }
    [Fact] public async Task RevertRestoresOriginalAndIsIdempotent()
    {
        using var d = new TestDirectory(); var engine = Engine(d); var operation = new Operation();
        await engine.ApplyAsync(operation, "target"); var id = (await engine.HistoryAsync()).Single().Id;
        await engine.RevertAsync(id, operation); Assert.Equal("original", operation.State);
        Assert.False((await engine.RevertAsync(id, operation)).Changed); Assert.Equal(2, operation.Writes);
    }
    [Fact] public async Task ExternalChangesAreNotOverwrittenByRevert()
    {
        using var d = new TestDirectory(); var engine = Engine(d); var operation = new Operation();
        await engine.ApplyAsync(operation, "target"); operation.State = "external";
        var id = (await engine.HistoryAsync()).Single().Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RevertAsync(id, operation));
        Assert.Equal("external", operation.State); Assert.Equal(1, operation.Writes);
    }
    [Fact] public async Task MostRecentChangeMustBeRevertedFirst()
    {
        using var d = new TestDirectory(); var engine = Engine(d); var operation = new Operation();
        await engine.ApplyAsync(operation, "target"); await engine.ApplyAsync(operation, "other");
        var history = await engine.HistoryAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RevertAsync(history[0].Id, operation));
        await engine.RevertAsync(history[1].Id, operation); await engine.RevertAsync(history[0].Id, operation);
        Assert.Equal("original", operation.State);
    }
    [Fact] public async Task FailureAfterPartialWriteAutomaticallyRollsBack()
    {
        using var d = new TestDirectory(); var engine = Engine(d); var operation = new Operation();
        operation.OnWrite = state => { if (state == "target") { operation.State = "partially-changed"; throw new IOException("Device failure"); } return Task.CompletedTask; };
        await Assert.ThrowsAsync<IOException>(() => engine.ApplyAsync(operation, "target"));
        Assert.Equal("original", operation.State); Assert.Equal(OperationStatus.Failed, Assert.Single(await engine.HistoryAsync()).Status);
    }
    [Fact] public async Task PermissionDenialDoesNotCrashSubsequentOperations()
    {
        using var d = new TestDirectory(); var engine = Engine(d); var operation = new Operation();
        operation.OnWrite = _ => throw new UnauthorizedAccessException("Denied");
        await Assert.ThrowsAsync<AggregateException>(() => engine.ApplyAsync(operation, "target"));
        operation.OnWrite = null;
        var record = Assert.Single(await engine.HistoryAsync()); Assert.Equal(OperationStatus.Prepared, record.Status);
        await engine.RevertAsync(record.Id, operation);
        Assert.True((await engine.ApplyAsync(operation, "target")).Changed);
    }
    [Fact] public async Task InterruptedRecordBlocksNewApplyUntilRecovered()
    {
        using var d = new TestDirectory(); var store = new JsonStore<List<ChangeRecord>>(d.File("history.json"), () => []);
        var record = new ChangeRecord(Guid.NewGuid(), "test", "Test", "original", "target", DateTimeOffset.Now, OperationStatus.Prepared);
        await store.SaveAsync([record]); var engine = Engine(d); var operation = new Operation { State = "partial" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(operation, "target"));
        await engine.RevertAsync(record.Id, operation); Assert.Equal("original", operation.State);
    }
    [Fact] public async Task CancellationDuringWriteRollsBackWithIndependentToken()
    {
        using var d = new TestDirectory(); using var cancel = new CancellationTokenSource(); var engine = Engine(d); var operation = new Operation();
        operation.OnWrite = state => { if (state == "target") { operation.State = "partial"; cancel.Cancel(); throw new OperationCanceledException(cancel.Token); } return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ApplyAsync(operation, "target", cancel.Token));
        Assert.Equal("original", operation.State); Assert.Equal(OperationStatus.Failed, Assert.Single(await engine.HistoryAsync()).Status);
    }
    [Fact] public async Task FailedBackupPreventsSystemWrite()
    {
        using var d = new TestDirectory(); var engine = new OptimizationEngine(new(d.Path, () => []), new(d.File("logs"))); var operation = new Operation();
        var failure = await Record.ExceptionAsync(() => engine.ApplyAsync(operation, "target"));
        Assert.True(failure is IOException or UnauthorizedAccessException,
            $"Expected a filesystem/permission failure, got {failure?.GetType().Name ?? "success"}.");
        Assert.Equal(0, operation.Writes); Assert.Equal("original", operation.State);
    }
    [Fact] public async Task InvalidRequestDoesNotCreateHistory()
    {
        using var d = new TestDirectory(); var engine = Engine(d); var operation = new Operation();
        await Assert.ThrowsAsync<ArgumentException>(() => engine.ApplyAsync(operation, "invalid")); Assert.Empty(await engine.HistoryAsync()); Assert.Equal(0, operation.Writes);
    }
    [Fact] public async Task CorruptJournalBlocksMutationAndPreservesEvidence()
    {
        using var d = new TestDirectory(); await File.WriteAllTextAsync(d.File("history.json"), "corrupt"); var operation = new Operation();
        await Assert.ThrowsAnyAsync<Exception>(() => Engine(d).ApplyAsync(operation, "target"));
        Assert.Equal(0, operation.Writes); Assert.Equal("corrupt", await File.ReadAllTextAsync(d.File("history.json")));
    }
    [Fact] public async Task ConcurrentIdenticalAppliesProduceOnlyOneMutation()
    {
        using var d = new TestDirectory(); var engine = Engine(d); var operation = new Operation();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => engine.ApplyAsync(operation, "target")));
        Assert.Single(results, r => r.Changed); Assert.Equal(1, operation.Writes); Assert.Single(await engine.HistoryAsync());
    }
}
