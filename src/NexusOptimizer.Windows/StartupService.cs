using Microsoft.Win32;
using System.Text;
using System.Text.Json;
using NexusOptimizer.Core;

namespace NexusOptimizer.Windows;

public sealed record StartupEntry(string Name, string Command, string Source, bool Enabled, bool CanManage, string Publisher, string? OperationId);
public sealed record StartupValue(string Text, RegistryValueKind Kind);
public sealed record StartupState(StartupValue? Active, StartupValue? Disabled);

public sealed class StartupOperation(string name) : IReversibleOperation
{
    internal const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string DisabledPath = @"Software\NexusOptimizer\DisabledStartup";
    public string EntryName { get; } = name;
    public OperationInfo Info => new("startup:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(EntryName)),
        "Inicialização: " + EntryName, "Move a entrada do usuário entre Run e o arquivo de entradas desativadas do NEXUS.",
        "Pode reduzir aplicativos abertos no logon; não mede tempo de inicialização.", "O aplicativo deixará de abrir no próximo logon.");
    public Task<string> ReadAsync(CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(JsonSerializer.Serialize(new StartupState(Read(RunPath), Read(DisabledPath)))); }
    public string WithEnabled(string current, bool enabled)
    {
        var state = JsonSerializer.Deserialize<StartupState>(current)!;
        if (state.Active is not null && state.Disabled is not null) throw new InvalidOperationException("Há entradas conflitantes. Gerencie-as nas Configurações do Windows.");
        var value = state.Active ?? state.Disabled ?? throw new InvalidOperationException("Entrada não encontrada.");
        return JsonSerializer.Serialize(enabled ? new StartupState(value, null) : new StartupState(null, value));
    }
    public static string EnabledCommand(string command) => JsonSerializer.Serialize(new StartupState(new(command, RegistryValueKind.String), null));
    public Task WriteAsync(string state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Validate(state); var value = JsonSerializer.Deserialize<StartupState>(state)!;
        // Write the destination FIRST: a crash cannot discard both copies. The journal also retains the original.
        if (value.Active is not null) Set(RunPath, value.Active);
        if (value.Disabled is not null) Set(DisabledPath, value.Disabled);
        if (value.Active is null) Remove(RunPath);
        if (value.Disabled is null) Remove(DisabledPath);
        return Task.CompletedTask;
    }
    public void Validate(string state)
    {
        if (EntryName.Length is 0 or > 16383 || EntryName.Contains('\0')) throw new InvalidDataException("Nome de entrada inválido.");
        var value = JsonSerializer.Deserialize<StartupState>(state) ?? throw new InvalidDataException("Backup inválido.");
        foreach (var item in new[] { value.Active, value.Disabled }.Where(x => x is not null))
            if (item!.Kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString) || item.Text.Contains('\0'))
                throw new InvalidDataException("Tipo de entrada não suportado.");
    }
    private StartupValue? Read(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        if (key?.GetValue(EntryName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string value) return null;
        var kind = key.GetValueKind(EntryName);
        if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) throw new InvalidDataException("Tipo de registro não suportado.");
        return new(value, kind);
    }
    private void Set(string path, StartupValue value) { using var key = Registry.CurrentUser.CreateSubKey(path, true); key.SetValue(EntryName, value.Text, value.Kind); }
    private void Remove(string path) { using var key = Registry.CurrentUser.OpenSubKey(path, true); key?.DeleteValue(EntryName, false); }
}

public sealed class StartupService
{
    public IReadOnlyList<StartupEntry> Read()
    {
        List<StartupEntry> result = [];
        ReadKey(Registry.CurrentUser, StartupOperation.RunPath, "Usuário · Run", true, true, result);
        ReadKey(Registry.CurrentUser, StartupOperation.DisabledPath, "Usuário · arquivo NEXUS", false, true, result);
        ReadKey(Registry.LocalMachine, StartupOperation.RunPath, "Computador · Run (somente leitura)", true, false, result);
        ReadKey(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Computador · Run 32 bits (somente leitura)", true, false, result);
        foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
        {
            string path = Environment.GetFolderPath(folder);
            if (!Directory.Exists(path)) continue;
            foreach (string file in Directory.EnumerateFiles(path))
                if (!Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    result.Add(new(Path.GetFileNameWithoutExtension(file), file, "Pasta Inicializar (somente leitura)", true, false, "Não identificado", null));
        }
        return result.OrderBy(x => x.Name).ToArray();
    }
    private static void ReadKey(RegistryKey hive, string path, string source, bool enabled, bool manage, List<StartupEntry> entries)
    {
        using var key = hive.OpenSubKey(path);
        if (key is null) return;
        foreach (string name in key.GetValueNames())
        {
            if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string command) continue;
            entries.Add(new(name, command, source, enabled, manage, "Não identificado", manage ? new StartupOperation(name).Info.Id : null));
        }
    }
}
