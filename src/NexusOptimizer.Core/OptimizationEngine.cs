namespace NexusOptimizer.Core;

/// <summary>Write-ahead journal: persist the original value BEFORE touching the system.</summary>
public sealed class OptimizationEngine(JsonStore<List<ChangeRecord>> journal, FileLog log)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public Task<List<ChangeRecord>> HistoryAsync(CancellationToken token = default) => journal.LoadAsync(token);

    public async Task<OperationResult> ApplyAsync(IReversibleOperation operation, string desired, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        ChangeRecord? record = null;
        bool persisted = false;
        try
        {
            operation.Validate(desired);
            var history = await journal.LoadAsync(token);
            // An unresolved write can be recovered in History; do not overwrite its evidence.
            if (history.Any(x => x.OperationId == operation.Info.Id && x.Status == OperationStatus.Prepared))
                throw new InvalidOperationException("Há uma alteração interrompida. Restaure-a no histórico antes de aplicar novamente.");
            var before = await operation.ReadAsync(token);
            if (before == desired) return new(false, "A configuração já está no estado solicitado.");
            record = new(Guid.NewGuid(), operation.Info.Id, operation.Info.Name, before, desired,
                DateTimeOffset.Now, OperationStatus.Prepared);
            history.Add(record);
            await journal.SaveAsync(history, token);
            persisted = true;
            await operation.WriteAsync(desired, token);
            if (await operation.ReadAsync(token) != desired) throw new IOException("A leitura após a alteração não confirmou o estado solicitado.");
            await UpdateAsync(record with { Status = OperationStatus.Applied }, CancellationToken.None);
            return new(true, "Alteração aplicada e verificada. Backup salvo no histórico.");
        }
        catch (Exception error)
        {
            if (record is not null && persisted)
            {
                // Even cancellation requires rollback. Never lose the original state after a partial write.
                try
                {
                    await operation.WriteAsync(record.Before, CancellationToken.None);
                    if (await operation.ReadAsync(CancellationToken.None) != record.Before)
                        throw new IOException("Não foi possível verificar a reversão automática.");
                    await UpdateAsync(record with { Status = OperationStatus.Failed, Error = error.Message }, CancellationToken.None);
                }
                catch (Exception recoveryError)
                {
                    log.Write("Reversão automática pendente", recoveryError);
                    // Prepared stays recoverable; never label a partial mutation as rolled back.
                    try { await UpdateAsync(record with { Error = $"{error.Message} Reversão pendente: {recoveryError.Message}" }, CancellationToken.None); }
                    catch (Exception journalError) { log.Write("Falha no diário", journalError); }
                    throw new AggregateException("Alteração interrompida; restaure pelo histórico.", error, recoveryError);
                }
            }
            log.Write($"Aplicar {operation.Info.Id}", error);
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task<OperationResult> RevertAsync(Guid id, IReversibleOperation operation, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var history = await journal.LoadAsync(token);
            var record = history.Single(x => x.Id == id);
            if (record.OperationId != operation.Info.Id) throw new InvalidOperationException("Operação incompatível com o backup.");
            if (record.Status is OperationStatus.Reverted or OperationStatus.Failed) return new(false, "Esta alteração já foi desfeita ou falhou sem alterações pendentes.");
            if (history.Skip(history.FindIndex(x => x.Id == record.Id) + 1).Any(x => x.OperationId == record.OperationId &&
                x.Status is OperationStatus.Applied or OperationStatus.Prepared))
                throw new InvalidOperationException("Desfaça primeiro a alteração mais recente desta configuração.");
            var current = await operation.ReadAsync(token);
            if (current != record.After && current != record.Before && record.Status != OperationStatus.Prepared)
                throw new InvalidOperationException("A configuração foi modificada fora do NEXUS. A restauração foi interrompida para preservar essa mudança.");
            operation.Validate(record.Before);
            if (current != record.Before)
            {
                await operation.WriteAsync(record.Before, token);
                if (await operation.ReadAsync(token) != record.Before) throw new IOException("A restauração não foi confirmada.");
            }
            await UpdateAsync(record with { Status = OperationStatus.Reverted, RevertedAt = DateTimeOffset.Now }, CancellationToken.None);
            return new(true, "Configuração anterior restaurada e verificada.");
        }
        finally { gate.Release(); }
    }
    private Task UpdateAsync(ChangeRecord record, CancellationToken token) => journal.UpdateAsync(list =>
    {
        int index = list.FindIndex(x => x.Id == record.Id);
        if (index < 0) throw new InvalidDataException("Backup não encontrado no diário.");
        list[index] = record;
        return list;
    }, token);
}
