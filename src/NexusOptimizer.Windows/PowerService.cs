using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using NexusOptimizer.Core;

namespace NexusOptimizer.Windows;

public sealed record PowerPlan(Guid Id, string Name) { public override string ToString() => Name; }
public sealed class PowerService : IReversibleOperation
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid Performance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public OperationInfo Info { get; } = new("power-plan", "Plano de energia", "Ativa um plano já existente no Windows.",
        "Permite escolher entre consumo e disponibilidade de desempenho; o ganho depende da carga.",
        "Planos de desempenho podem aumentar consumo, ruído e temperatura.");
    public IReadOnlyList<PowerPlan> Plans()
    {
        List<PowerPlan> plans = [];
        for (uint i = 0; i < 128; i++)
        {
            byte[] bytes = new byte[16]; uint size = 16;
            uint result = Native.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, i, bytes, ref size);
            if (result == 259) break;
            Check(result); Guid id = new(bytes); uint nameSize = 0;
            uint nameResult = Native.PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref nameSize);
            string name = id.ToString();
            if (nameSize is > 0 and < 65536 && nameResult is 0 or 234)
            {
                var buffer = new byte[nameSize];
                if (Native.PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref nameSize) == 0)
                    name = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
            }
            plans.Add(new(id, name));
        }
        return plans;
    }
    public Guid Active()
    {
        Check(Native.PowerGetActiveScheme(IntPtr.Zero, out var pointer));
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { Native.LocalFree(pointer); }
    }
    public Task<string> ReadAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(Active().ToString()); }
    public Task WriteAsync(string state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Validate(state); Guid id = Guid.Parse(state);
        Check(Native.PowerSetActiveScheme(IntPtr.Zero, ref id)); return Task.CompletedTask;
    }
    public void Validate(string state)
    {
        if (!Guid.TryParse(state, out var id) || !Plans().Any(x => x.Id == id))
            throw new InvalidOperationException("Este plano não existe ou não é suportado neste computador.");
    }
    private static void Check(uint result) { if (result != 0) throw new Win32Exception((int)result); }
}
