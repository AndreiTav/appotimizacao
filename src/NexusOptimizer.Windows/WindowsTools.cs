using System.Diagnostics;
using NexusOptimizer.Core;

namespace NexusOptimizer.Windows;

public sealed record WindowsTool(string Id, string Name, string Description, string Target, string[] Arguments, bool Admin = false, bool Wait = false);
public static class WindowsTools
{
    public static IReadOnlyList<WindowsTool> All { get; } = [
        new("tasks", "Gerenciador de Tarefas", "Consumo e aplicativos em execução", "Taskmgr.exe", []),
        new("power", "Opções de energia", "Planos disponíveis no Windows", "control.exe", ["/name", "Microsoft.PowerOptions"]),
        new("devices", "Gerenciador de Dispositivos", "Dispositivos e drivers", "mmc.exe", ["devmgmt.msc"]),
        new("info", "Informações do Sistema", "Inventário detalhado do computador", "msinfo32.exe", []),
        new("disk-clean", "Limpeza de Disco", "Limpeza nativa do Windows", "cleanmgr.exe", []),
        new("disks", "Gerenciamento de Disco", "Volumes e partições — use com cuidado", "mmc.exe", ["diskmgmt.msc"]),
        new("events", "Visualizador de Eventos", "Eventos e diagnóstico do sistema", "mmc.exe", ["eventvwr.msc"]),
        new("startup", "Aplicativos de inicialização", "Gerenciamento nativo e impacto de inicialização", "ms-settings:startupapps", []),
        new("game", "Modo de Jogo", "Consulte e altere nas Configurações do Windows", "ms-settings:gaming-gamemode", []),
        new("graphics", "Configurações gráficas", "Preferências gráficas por aplicativo", "ms-settings:display-advancedgraphics", []),
        new("diagnostics", "Diagnóstico DirectX", "Diagnóstico de vídeo e áudio", "dxdiag.exe", []),
        new("restore", "Proteção do Sistema", "Consulte a proteção e crie um ponto de restauração", "SystemPropertiesProtection.exe", []),
        new("sfc", "Verificar arquivos (SFC)", "Verifica e repara arquivos protegidos. UAC; pode demorar. Acompanhe a janela nativa.", "sfc.exe", ["/scannow"], true, true),
        new("dism", "Diagnóstico de imagem (DISM)", "Verifica a integridade da imagem, sem reparo automático. UAC; acompanhe a janela nativa.", "dism.exe", ["/Online", "/Cleanup-Image", "/ScanHealth"], true, true)
    ];
    public static async Task<string> OpenAsync(string id, CancellationToken token = default)
    {
        var tool = All.Single(x => x.Id == id); // Only predefined IDs; never execute text supplied by UI.
        var info = new ProcessStartInfo(tool.Target.StartsWith("ms-settings:", StringComparison.Ordinal) ? tool.Target : Path.Combine(Environment.SystemDirectory, tool.Target))
            { UseShellExecute = true, Verb = tool.Admin ? "runas" : "open", WorkingDirectory = Environment.SystemDirectory };
        foreach (var argument in tool.Arguments)
            info.ArgumentList.Add(argument.EndsWith(".msc", StringComparison.OrdinalIgnoreCase) ? Path.Combine(Environment.SystemDirectory, argument) : argument);
        using var process = Process.Start(info);
        if (!tool.Wait || process is null) return "Ferramenta aberta no Windows.";
        // Elevated tools show their own real progress; never fabricate percentages in the app.
        await process.WaitForExitAsync(token);
        return process.ExitCode switch
        {
            0 => "Comando concluído (código 0). Consulte o resultado detalhado na ferramenta e nos logs CBS/DISM do Windows.",
            3010 => "Comando concluído; o Windows solicita reinicialização (código 3010).",
            _ => throw new IOException($"A ferramenta retornou código {process.ExitCode}. Consulte o resultado na janela e nos logs do Windows.")
        };
    }
    public static void RequestClose(ProcessSnapshot snapshot)
    {
        using var process = Process.GetProcessById(snapshot.Id);
        if (process.Id == Environment.ProcessId || process.StartTime != snapshot.StartTime || process.SessionId != Process.GetCurrentProcess().SessionId)
            throw new InvalidOperationException("Processo diferente, protegido ou de outra sessão.");
        if (!Native.IsProcessCritical(process.Handle, out bool critical) || critical)
            throw new InvalidOperationException("Não é possível confirmar que este processo pode ser fechado com segurança.");
        if (!process.CloseMainWindow()) throw new InvalidOperationException("O aplicativo não tem uma janela que aceite fechamento. Use o Gerenciador de Tarefas.");
    }
}
