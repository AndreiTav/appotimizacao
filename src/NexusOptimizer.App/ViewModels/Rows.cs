using CommunityToolkit.Mvvm.ComponentModel;
using NexusOptimizer.Core;

namespace NexusOptimizer.App.ViewModels;

public sealed partial class Recommendation : ObservableObject
{
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required string Benefit { get; init; }
    public required string SideEffects { get; init; }
    public required string Metadata { get; init; }
    public IReversibleOperation? Operation { get; init; }
    public string? Desired { get; init; }
    public string? ToolId { get; init; }
    public bool CanApply => Operation is not null && Desired is not null;
    [ObservableProperty] private bool selected;
    public bool HasTool => ToolId is not null;
}
public sealed partial class CleanupCategory(CleanupRule rule) : ObservableObject
{
    public CleanupRule Rule { get; } = rule;
    public string Name => Rule.Name;
    public string Warning => Rule.Warning;
    [ObservableProperty] private bool selected;
    [ObservableProperty] private string summary = "Não analisado";
}
public sealed record HistoryRow(ChangeRecord Record)
{
    public string Name => Record.Name;
    public string Date => Record.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
    public bool CanRevert => Record.Status is OperationStatus.Applied or OperationStatus.Prepared;
    public string Status => Record.Status switch { OperationStatus.Prepared => "Interrompido · recuperação disponível", OperationStatus.Applied => "Aplicado", OperationStatus.Failed => "Falhou · estado anterior preservado", _ => "Restaurado" };
    public string Details => $"{Status} · {Date}" + (Record.Error is null ? "" : $" · {Record.Error}");
}
