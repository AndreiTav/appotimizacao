using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using NexusOptimizer.Core;

namespace NexusOptimizer.Windows;

public sealed class MouseOperation : IReversibleOperation
{
    public OperationInfo Info { get; } = new("mouse-acceleration", "Aceleração do ponteiro", "Desativa a aceleração do ponteiro pela API SystemParametersInfo.",
        "Movimento mais previsível no desktop. Jogos com entrada bruta geralmente ignoram este ajuste.",
        "Altera a sensação do mouse em todos os aplicativos que usam o ponteiro do Windows.", RiskLevel.Moderate);
    public const string Disabled = "[0,0,0]";
    public static bool IsEnabled(string state) => JsonSerializer.Deserialize<int[]>(state) is { Length: 3 } values && values[2] != 0;
    public Task<string> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); int[] values = new int[3];
        if (!Native.SystemParametersInfo(0x0003, 0, values, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return Task.FromResult(JsonSerializer.Serialize(values));
    }
    public Task WriteAsync(string state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Validate(state);
        if (!Native.SystemParametersInfo(0x0004, 0, JsonSerializer.Deserialize<int[]>(state)!, 3))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return Task.CompletedTask;
    }
    public void Validate(string state)
    {
        var values = JsonSerializer.Deserialize<int[]>(state);
        if (values is not { Length: 3 } || values.Any(x => x < 0) || values[2] > 2) throw new InvalidDataException("Configuração de mouse inválida.");
    }
}
