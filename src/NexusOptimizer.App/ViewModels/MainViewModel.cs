using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NexusOptimizer.Core;
using NexusOptimizer.Windows;

namespace NexusOptimizer.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly JsonStore<AppPreferences> preferences = new(Path.Combine(App.DataDirectory, "preferences.json"), () => new());
    private readonly OptimizationEngine engine = new(new(Path.Combine(App.DataDirectory, "history.json"), () => []), App.Log);
    private readonly MonitoringService monitoring = new();
    private readonly PowerService power = new();
    private readonly MouseOperation mouse = new();
    private readonly StartupService startup = new();
    private readonly CleanupService cleanup = WindowsCleanup.Create();
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? currentOperation;
    private CleanupScan? scan;
    private HardwareSnapshot? latest;
    private Task? monitorTask;
    private readonly SemaphoreSlim samplingGate = new(1, 1);
    private bool preferencesLoaded;
    public bool IsWindowActive { get; set; } = true;
    public string MachineName => Environment.MachineName;
    public string DataPath => App.DataDirectory;
    public string[] Pages { get; } = ["Dashboard", "Otimização Inteligente", "Otimização Gamer", "Limpeza do Sistema", "Inicialização", "Monitoramento", "Ferramentas do Windows", "Histórico e Restauração", "Configurações"];
    public string[] Themes { get; } = ["Escuro", "Claro"];
    public string[] Accents { get; } = ["Ciano", "Roxo", "Azul"];
    public int[] Intervals { get; } = Enumerable.Range(1, 30).ToArray();
    public IReadOnlyList<WindowsTool> Tools => WindowsTools.All;
    public ObservableCollection<double> CpuHistory { get; } = [];
    public ObservableCollection<double> MemoryHistory { get; } = [];
    public ObservableCollection<DriveSnapshot> Drives { get; } = [];
    public ObservableCollection<ProcessSnapshot> Processes { get; } = [];
    public ObservableCollection<Recommendation> Recommendations { get; } = [];
    public ObservableCollection<PowerPlan> PowerPlans { get; } = [];
    public ObservableCollection<StartupEntry> StartupEntries { get; } = [];
    public ObservableCollection<HistoryRow> History { get; } = [];
    public ObservableCollection<CleanupCategory> CleanupCategories { get; } = [];
    [ObservableProperty] private int selectedPage;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string status = "Preparando os módulos…";
    [ObservableProperty] private bool hasError;
    [ObservableProperty] private double progress;
    [ObservableProperty] private bool indeterminate;
    [ObservableProperty] private string cpuName = "Não disponível";
    [ObservableProperty] private string cpuValue = "Não disponível";
    [ObservableProperty] private string cpuFrequency = "Frequência: Não disponível";
    [ObservableProperty] private double cpuPercent;
    [ObservableProperty] private string memoryValue = "Não disponível";
    [ObservableProperty] private string memoryDetail = "Aguardando leitura";
    [ObservableProperty] private double memoryPercent;
    [ObservableProperty] private string gpuName = "Não disponível";
    [ObservableProperty] private string osName = "Windows 11";
    [ObservableProperty] private string uptime = "Não disponível";
    [ObservableProperty] private string processCount = "Não disponível";
    [ObservableProperty] private string activePowerPlan = "Não disponível";
    [ObservableProperty] private string optimizationSummary = "Nenhuma leitura ainda";
    [ObservableProperty] private string monitoringStatus = "Aguardando leitura dos sensores";
    [ObservableProperty] private string cleanupSummary = "Selecione as categorias e analise antes de excluir.";
    [ObservableProperty] private string recycleSummary = "Tamanho não consultado";
    [ObservableProperty] private string gamePath = "Nenhum executável selecionado";
    [ObservableProperty] private PowerPlan? selectedPowerPlan;
    [ObservableProperty] private ProcessSnapshot? selectedProcess;
    [ObservableProperty] private StartupEntry? selectedStartup;
    [ObservableProperty] private HistoryRow? selectedHistory;
    [ObservableProperty] private string selectedTheme = "Escuro";
    [ObservableProperty] private string selectedAccent = "Ciano";
    [ObservableProperty] private int monitorInterval = 2;
    [ObservableProperty] private bool notifications = true;
    [ObservableProperty] private bool startWithWindows;

    public MainViewModel()
    {
        foreach (var rule in cleanup.Rules) CleanupCategories.Add(new(rule));
    }
    public async Task InitializeAsync()
    {
        await RunAsync(async token =>
        {
            AppPreferences p;
            try { p = (await preferences.LoadAsync(token)).Validated(); }
            catch (JsonException e)
            {
                // Preserve the invalid original before allowing fresh preferences to be saved.
                string backup = preferences.Path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Copy(preferences.Path, backup, false); App.Log.Write("Preferências inválidas; cópia preservada", e);
                MessageBox.Show("O arquivo de preferências é inválido. Uma cópia foi preservada; os padrões serão usados.", "NEXUS"); p = new();
            }
            preferencesLoaded = true;
            SelectedTheme = p.Theme; SelectedAccent = p.Accent; MonitorInterval = p.MonitorIntervalSeconds;
            Notifications = p.Notifications; GamePath = p.GamePath ?? "Nenhum executável selecionado";
            ApplyTheme();
            List<string> errors = [];
            foreach (var step in new Func<CancellationToken, Task>[] { RefreshPowerAsync, RefreshStartupAsync, RefreshHistoryAsync, SampleAsync })
            {
                try { await step(token); }
                catch (Exception error) when (error is not OperationCanceledException)
                { errors.Add(error.Message); App.Log.Write("Carregar módulo", error); }
            }
            HasError = errors.Count > 0;
            Status = errors.Count == 0 ? "Pronto para analisar. Nenhuma configuração do Windows foi alterada." : "Alguns módulos não puderam ser carregados: " + string.Join(" · ", errors);
        });
        monitorTask = MonitorLoopAsync(lifetime.Token);
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(MonitorInterval, 1, 30)), token);
                if (IsWindowActive && SelectedPage is 0 or 5) await SampleAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) { App.Log.Write("Monitoramento", error); MonitoringStatus = "Uma leitura falhou; nova tentativa no próximo intervalo. " + error.Message; }
        }
    }
    private async Task SampleAsync(CancellationToken token)
    {
        await samplingGate.WaitAsync(token);
        try
        {
        latest = await monitoring.SampleAsync(token);
        CpuName = latest.CpuName; CpuPercent = latest.CpuUsage ?? 0;
        CpuValue = latest.CpuUsage is double cpu ? $"{cpu:F0}%" : "Não disponível";
        CpuFrequency = latest.CpuMhz is double mhz ? $"{mhz / 1000:F2} GHz · leitura WMI, até 30 s" : "Frequência: Não disponível";
        MemoryPercent = latest.MemoryUsage;
        MemoryValue = latest.TotalMemory > 0 ? $"{MemoryPercent:F0}%" : "Não disponível";
        MemoryDetail = latest.TotalMemory > 0 ? $"{Format.Bytes(latest.TotalMemory - latest.AvailableMemory)} em uso · {Format.Bytes(latest.AvailableMemory)} disponíveis · {Format.Bytes(latest.TotalMemory)} total" : "Não disponível";
        GpuName = latest.GpuName; OsName = latest.OsName;
        Uptime = $"{(int)latest.Uptime.TotalDays}d {latest.Uptime.Hours}h {latest.Uptime.Minutes}min";
        ProcessCount = latest.ProcessCount.ToString();
        if (latest.CpuUsage is double value) Append(CpuHistory, value);
        if (latest.TotalMemory > 0) Append(MemoryHistory, latest.MemoryUsage);
        Replace(Drives, latest.Drives);
        int? selected = SelectedProcess?.Id;
        Replace(Processes, latest.Processes); SelectedProcess = Processes.FirstOrDefault(p => p.Id == selected);
        MonitoringStatus = latest.Errors.Count == 0 ? $"Leitura real · {DateTime.Now:HH:mm:ss} · intervalo {MonitorInterval} s" : string.Join(" · ", latest.Errors);
        }
        finally { samplingGate.Release(); }
    }

    [RelayCommand] private Task AnalyzeAsync() => RunAsync(async token =>
    {
        await SampleAsync(token); await RefreshPowerAsync(token); await RefreshStartupAsync(token);
        Recommendations.Clear();
        Guid active = await Task.Run(power.Active, token);
        if (PowerPlans.Any(x => x.Id == PowerService.Balanced) && active != PowerService.Balanced)
            Recommendations.Add(new() { Name = "Usar o plano Equilibrado", Detail = "O plano atual é " + ActivePowerPlan + ". O Equilibrado ajusta consumo e desempenho conforme a carga.",
                Benefit = "Impacto estimado: menor consumo em cargas leves; sem promessa de aumento de FPS.", SideEffects = power.Info.SideEffects,
                Metadata = "Risco baixo · reversível · sem UAC · sem reinicialização · estado: outro plano ativo", Operation = power, Desired = PowerService.Balanced.ToString(), Selected = true });
        string mouseState = await Task.Run(() => mouse.ReadAsync(token), token);
        if (MouseOperation.IsEnabled(mouseState))
            Recommendations.Add(new() { Name = mouse.Info.Name, Detail = mouse.Info.Description, Benefit = "Impacto estimado: controle do ponteiro. " + mouse.Info.Benefit,
                SideEffects = mouse.Info.SideEffects, Metadata = "Risco moderado · reversível · sem UAC · sem reinicialização · estado: ativado", Operation = mouse, Desired = MouseOperation.Disabled });
        int count = StartupEntries.Count(x => x.CanManage && x.Enabled);
        if (count > 0) Recommendations.Add(new() { Name = $"Revisar {count} aplicativo(s) de inicialização", Detail = "Escolha individualmente os aplicativos que não precisam abrir no logon.",
            Benefit = "Impacto estimado depende dos aplicativos desativados.", SideEffects = "Os aplicativos desativados não abrirão automaticamente.", Metadata = "Revisão manual · nenhuma alteração automática", ToolId = "startup" });
        foreach (var drive in Drives.Where(d => d.UsedPercent > 90))
            Recommendations.Add(new() { Name = $"Pouco espaço em {drive.Name}", Detail = drive.Summary, Benefit = "Liberar espaço pode evitar falhas de atualizações e instalações.",
                SideEffects = "A limpeza exclui arquivos; analise as categorias antes de confirmar.", Metadata = "Diagnóstico · ocupação superior a 90%", ToolId = "disk-clean" });
        Recommendations.Add(new() { Name = "Consultar o Modo de Jogo", Detail = "A configuração é gerenciada na página oficial do Windows; o NEXUS não usa chaves não documentadas.",
            Benefit = "Revise a preferência nativa para a sua utilização.", SideEffects = "Não altera configurações automaticamente.", Metadata = "Estado: consultar no Windows · impacto não medido", ToolId = "game" });
        SelectedPage = 1; Status = "Análise concluída com dados reais. Apenas ajustes de baixo risco são pré-selecionados.";
    });

    [RelayCommand] private Task OptimizeAsync() => RunAsync(async token =>
    {
        var selected = Recommendations.Where(x => x.Selected && x.CanApply).ToArray();
        if (selected.Length == 0) { Status = "Nenhum ajuste selecionado para aplicar."; return; }
        string description = string.Join("\n\n", selected.Select(x => $"• {x.Name}\n{x.Detail}\n{x.SideEffects}\n{x.Metadata}"));
        if (!Confirm("Aplicar exatamente estes ajustes?\n\n" + description + "\n\nOs estados anteriores serão registrados para restauração.")) return;
        List<(Guid Id, IReversibleOperation Operation)> completed = [];
        try
        {
            foreach (var item in selected)
            {
                token.ThrowIfCancellationRequested();
                var result = await Task.Run(() => engine.ApplyAsync(item.Operation!, item.Desired!, token), token);
                if (result.Changed)
                {
                    var history = await engine.HistoryAsync(CancellationToken.None);
                    completed.Add((history.Last(x => x.OperationId == item.Operation!.Info.Id).Id, item.Operation!));
                }
            }
            Status = "Ajustes aplicados e verificados. A reversão está disponível no histórico.";
        }
        catch
        {
            foreach (var item in completed.AsEnumerable().Reverse())
                try { await Task.Run(() => engine.RevertAsync(item.Id, item.Operation)); }
                catch (Exception error) { App.Log.Write("Reversão parcial; consulte o histórico", error); }
            throw;
        }
        finally { await RefreshHistoryAsync(CancellationToken.None); await RefreshPowerAsync(CancellationToken.None); }
    });

    [RelayCommand] private Task ApplyRecommendationAsync(Recommendation? item) => RunAsync(async token =>
    {
        if (item?.CanApply != true || !Confirm($"{item.Name}\n\n{item.Detail}\n{item.Benefit}\n{item.SideEffects}\n{item.Metadata}\n\nAplicar e salvar o backup?")) return;
        Status = (await Task.Run(() => engine.ApplyAsync(item.Operation!, item.Desired!, token), token)).Message;
        await RefreshHistoryAsync(token); await RefreshPowerAsync(token);
    });
    [RelayCommand] private Task ApplyProfileAsync(string? profile) => RunAsync(async token =>
    {
        Guid id = profile switch { "balanced" => PowerService.Balanced, "performance" => PowerService.Performance,
            "gamer" when SelectedPowerPlan is not null => SelectedPowerPlan.Id, _ => throw new InvalidOperationException("Selecione um plano suportado.") };
        var plan = PowerPlans.SingleOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("Este computador não oferece o plano solicitado. Escolha um plano existente ou use as opções de energia nativas.");
        if (!Confirm($"Ativar o plano “{plan.Name}”?\n\nA única alteração deste perfil será o plano de energia. Planos de desempenho podem aumentar consumo e temperatura. Não há ganho de FPS garantido.\n\nO plano anterior será registrado para restauração. Sem reinicialização; restrições do Windows serão respeitadas.")) return;
        Status = (await Task.Run(() => engine.ApplyAsync(power, id.ToString(), token), token)).Message;
        await RefreshPowerAsync(token); await RefreshHistoryAsync(token);
    });
    [RelayCommand] private Task DisableMouseAccelerationAsync() => RunAsync(async token =>
    {
        if (!MouseOperation.IsEnabled(await Task.Run(() => mouse.ReadAsync(token), token)))
        { Status = "A aceleração do ponteiro já está desativada. Nenhuma alteração foi necessária."; return; }
        if (!Confirm($"{mouse.Info.Description}\n\n{mouse.Info.Benefit}\n{mouse.Info.SideEffects}\n\nRisco moderado. Aplicar com backup do estado atual?")) return;
        Status = (await Task.Run(() => engine.ApplyAsync(mouse, MouseOperation.Disabled, token), token)).Message;
        await RefreshHistoryAsync(token);
    });
    [RelayCommand] private Task SelectGameAsync() => RunAsync(async token =>
    {
        var dialog = new OpenFileDialog { Filter = "Executável do jogo (*.exe)|*.exe", CheckFileExists = true, Title = "Selecione um jogo instalado (o executável não será iniciado)" };
        if (dialog.ShowDialog() != true) return;
        await using var file = File.OpenRead(dialog.FileName);
        byte[] header = new byte[2];
        if (await file.ReadAsync(header, token) != 2 || header[0] != 'M' || header[1] != 'Z') throw new InvalidDataException("O arquivo não tem um cabeçalho de executável Windows.");
        GamePath = dialog.FileName; await SavePreferencesCoreAsync(token);
        Status = "Jogo selecionado. Ajustes disponíveis: plano de energia e preferências gráficas nativas. Nenhum arquivo do jogo será modificado.";
    });
    [RelayCommand] private Task RequestCloseAsync() => RunAsync(token =>
    {
        var process = SelectedProcess ?? throw new InvalidOperationException("Selecione um processo na lista.");
        if (Confirm($"Solicitar o fechamento de {process.Name} (PID {process.Id})?\n\nSalve seu trabalho. O NEXUS enviará um pedido de fechamento à janela; o processo não será encerrado à força."))
        { WindowsTools.RequestClose(process); Status = "Solicitação enviada. O aplicativo pode pedir confirmação ou continuar aberto."; }
        return Task.CompletedTask;
    });

    [RelayCommand] private Task ScanCleanupAsync() => RunAsync(async token =>
    {
        scan = null; var selected = SelectedCategories();
        if (selected.Count == 0) { Status = "Selecione pelo menos uma categoria."; return; }
        scan = await cleanup.ScanAsync(selected, token);
        foreach (var category in CleanupCategories)
        {
            var files = scan.Files.Where(f => f.RuleId == category.Rule.Id).ToArray();
            category.Summary = selected.Contains(category.Rule.Id) ? $"{files.Length:N0} arquivo(s) · {Format.Bytes(files.Sum(f => f.Length))}" : "Não analisado";
        }
        CleanupSummary = $"{scan.Files.Count:N0} arquivos · até {Format.Bytes(scan.TotalBytes)} recuperáveis. {scan.Warnings.Count} aviso(s).";
        Status = scan.Warnings.Count == 0 ? "Análise concluída. Nada foi excluído." : "Análise parcial: " + string.Join(" · ", scan.Warnings.Take(3));
    });
    [RelayCommand] private Task CleanAsync() => RunAsync(async token =>
    {
        var currentScan = scan ?? throw new InvalidOperationException("Analise as categorias antes da limpeza.");
        var selected = SelectedCategories(); var files = currentScan.Files.Where(f => selected.Contains(f.RuleId)).ToArray();
        if (files.Length == 0) { Status = "Nenhum arquivo analisado nas categorias selecionadas."; return; }
        if (!Confirm($"Excluir definitivamente {files.Length:N0} arquivo(s), até {Format.Bytes(files.Sum(f => f.Length))}?\n\nCategorias: {string.Join(", ", CleanupCategories.Where(c => c.Selected).Select(c => c.Name))}\n\nEsta limpeza não pode ser desfeita pelo histórico. Arquivos em uso ou alterados serão ignorados. A lixeira não está incluída.")) return;
        Indeterminate = false;
        var result = await cleanup.DeleteAsync(currentScan, selected, new Progress<int>(p => Progress = p), token);
        scan = null;
        CleanupSummary = $"{result.Deleted} excluídos · {result.Skipped} ignorados · {Format.Bytes(result.FreedBytes)} removidos.";
        Status = CleanupSummary + (token.IsCancellationRequested ? " Cancelado; exclusões já realizadas são permanentes." : "") + (result.Errors.Count > 0 ? " " + string.Join(" · ", result.Errors.Take(3)) : "");
        App.Log.Write(Status);
    });
    [RelayCommand] private Task QueryRecycleAsync() => RunAsync(async token =>
    {
        var result = await Task.Run(WindowsCleanup.RecycleSize, token);
        RecycleSummary = $"{result.Items:N0} itens · {Format.Bytes(result.Bytes)}"; Status = "Lixeira consultada. Nada foi excluído.";
    });
    [RelayCommand] private Task EmptyRecycleAsync() => RunAsync(async token =>
    {
        var result = await Task.Run(WindowsCleanup.RecycleSize, token);
        if (!Confirm($"Esvaziar permanentemente a lixeira de todas as unidades?\n\n{result.Items:N0} itens · {Format.Bytes(result.Bytes)}\n\nEsta ação não pode ser desfeita. O Windows também exibirá sua confirmação.")) return;
        await Task.Run(WindowsCleanup.EmptyRecycleBin, token); RecycleSummary = "Consulte novamente para conferir o estado atual.";
        Status = "Pedido de esvaziamento concluído pelo Windows. Se você cancelou a confirmação nativa, os itens foram preservados.";
        App.Log.Write(Status);
    });

    [RelayCommand] private Task RefreshStartupAsync() => RunAsync(async token => { await RefreshStartupAsync(token); Status = "Entradas atualizadas. Pastas e entradas de máquina são somente leitura; tarefas agendadas devem ser gerenciadas no Windows."; });
    private async Task RefreshStartupAsync(CancellationToken token)
    {
        Replace(StartupEntries, await Task.Run(startup.Read, token));
        StartWithWindows = StartupEntries.Any(x => x.Name == "NexusOptimizer" && x.CanManage && x.Enabled);
    }
    [RelayCommand] private Task ToggleStartupAsync() => RunAsync(async token =>
    {
        var entry = SelectedStartup ?? throw new InvalidOperationException("Selecione uma entrada.");
        if (!entry.CanManage) { Status = await WindowsTools.OpenAsync("startup", token); return; }
        if (!Confirm($"{(entry.Enabled ? "Desativar" : "Ativar")} “{entry.Name}” no próximo logon?\n\nOrigem: {entry.Source}\nComando: {entry.Command}\n\nA entrada será preservada e poderá ser restaurada pelo histórico. Sem UAC; sem reinicialização imediata.")) return;
        var operation = new StartupOperation(entry.Name);
        string before = await Task.Run(() => operation.ReadAsync(token), token);
        Status = (await Task.Run(() => engine.ApplyAsync(operation, operation.WithEnabled(before, !entry.Enabled), token), token)).Message;
        await RefreshStartupAsync(token); await RefreshHistoryAsync(token);
    });

    [RelayCommand] private Task RefreshHistoryAsync() => RunAsync(async token => { await RefreshHistoryAsync(token); Status = "Histórico atualizado."; });
    private async Task RefreshHistoryAsync(CancellationToken token)
    {
        var records = await engine.HistoryAsync(token);
        Replace(History, records.AsEnumerable().Reverse().Select(r => new HistoryRow(r)));
        OptimizationSummary = $"{records.Count(x => x.Status == OperationStatus.Applied)} alteração(ões) ativa(s) · {records.Count(x => x.Status == OperationStatus.Prepared)} pendente(s)";
    }
    private IReversibleOperation Resolve(string id) => id switch
    {
        "power-plan" => power,
        "mouse-acceleration" => mouse,
        _ when id.StartsWith("startup:", StringComparison.Ordinal) => new StartupOperation(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(id[8..]))),
        _ => throw new InvalidOperationException("Tipo de operação desconhecido; o backup foi preservado.")
    };
    [RelayCommand] private Task RevertAsync() => RunAsync(async token =>
    {
        var row = SelectedHistory ?? throw new InvalidOperationException("Selecione uma alteração.");
        if (!row.CanRevert) { Status = "Esta alteração não tem reversão pendente."; return; }
        if (!Confirm($"Restaurar o estado anterior de “{row.Name}”?\n\n{row.Details}\n\nAlterações posteriores e mudanças externas serão verificadas antes de restaurar. Operações interrompidas usarão o backup original.")) return;
        Status = (await Task.Run(() => engine.RevertAsync(row.Record.Id, Resolve(row.Record.OperationId), token), token)).Message;
        await RefreshHistoryAsync(token); await RefreshPowerAsync(token); await RefreshStartupAsync(token);
    });
    [RelayCommand] private Task RestoreAllAsync() => RunAsync(async token =>
    {
        var rows = History.Where(x => x.CanRevert).ToArray();
        if (rows.Length == 0) { Status = "Nenhuma alteração para restaurar."; return; }
        if (!Confirm("Restaurar estas alterações, da mais recente para a mais antiga?\n\n" + string.Join("\n", rows.Select(x => "• " + x.Name)) + "\n\nConfigurações alteradas externamente podem impedir a reversão. Limpeza de arquivos não pode ser desfeita.")) return;
        int restored = 0; List<string> errors = [];
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            try { await Task.Run(() => engine.RevertAsync(row.Record.Id, Resolve(row.Record.OperationId), token), token); restored++; }
            catch (Exception error) when (error is not OperationCanceledException) { errors.Add(row.Name + ": " + error.Message); App.Log.Write("Restaurar", error); }
        }
        await RefreshHistoryAsync(token); await RefreshPowerAsync(token); await RefreshStartupAsync(token);
        HasError = errors.Count > 0; Status = $"{restored} alteração(ões) restaurada(s)." + (errors.Count == 0 ? "" : " Falhas: " + string.Join(" · ", errors));
    });

    [RelayCommand] private Task OpenToolAsync(string? id) => RunAsync(async token =>
    {
        var tool = Tools.Single(t => t.Id == id);
        if (tool.Admin && !Confirm($"Executar {tool.Name}?\n\n{tool.Description}\n\nO Windows solicitará UAC. O andamento real será mostrado na janela da ferramenta. Ao cancelar a espera no NEXUS, a ferramenta continuará sua execução.")) return;
        Status = tool.Wait ? "Ferramenta em execução. Acompanhe a janela nativa; aguarde a conclusão." : "Abrindo ferramenta…";
        try { Status = await WindowsTools.OpenAsync(tool.Id, token); }
        catch (OperationCanceledException) { Status = "Espera cancelada. A ferramenta nativa continua executando; acompanhe sua janela."; }
    });
    [RelayCommand] private Task OpenRecommendationToolAsync(Recommendation? item) => item?.ToolId is string id ? OpenToolAsync(id) : Task.CompletedTask;

    [RelayCommand] private Task SavePreferencesAsync() => RunAsync(async token =>
    {
        await SavePreferencesCoreAsync(token);
        var operation = new StartupOperation("NexusOptimizer");
        string state = await Task.Run(() => operation.ReadAsync(token), token);
        var parsed = JsonSerializer.Deserialize<StartupState>(state)!;
        bool enabled = parsed.Active is not null;
        if (enabled != StartWithWindows)
        {
            string desired = StartWithWindows ? StartupOperation.EnabledCommand('"' + (Environment.ProcessPath ?? throw new InvalidOperationException("Executável não identificado.")) + '"') : operation.WithEnabled(state, false);
            if (Confirm($"{(StartWithWindows ? "Ativar" : "Desativar")} a inicialização do NEXUS com o Windows?\n\nSerá alterada somente a entrada NexusOptimizer do usuário atual, com backup no histórico. Mantenha o aplicativo em uma pasta portátil fixa."))
                await Task.Run(() => engine.ApplyAsync(operation, desired, token), token);
            await RefreshStartupAsync(token); await RefreshHistoryAsync(token);
        }
        Status = "Preferências salvas neste computador.";
    });
    private async Task SavePreferencesCoreAsync(CancellationToken token)
    {
        if (!preferencesLoaded) throw new InvalidOperationException("As preferências ainda não foram carregadas. Consulte os logs antes de substituir o arquivo existente.");
        await preferences.SaveAsync(new AppPreferences { Theme = SelectedTheme, Accent = SelectedAccent, MonitorIntervalSeconds = MonitorInterval,
            Notifications = Notifications, GamePath = GamePath == "Nenhum executável selecionado" ? null : GamePath }.Validated(), token);
    }
    [RelayCommand] private Task ResetPreferencesAsync() => RunAsync(async token =>
    {
        if (!Confirm("Restaurar tema, destaque, notificações e intervalo padrão?\n\nA inicialização do Windows e as otimizações serão preservadas. Use o histórico para desfazê-las.")) return;
        SelectedTheme = "Escuro"; SelectedAccent = "Ciano"; MonitorInterval = 2; Notifications = true; GamePath = "Nenhum executável selecionado";
        await SavePreferencesCoreAsync(token); Status = "Preferências restauradas.";
    });
    [RelayCommand] private Task OpenLogsAsync() => RunAsync(_ =>
    {
        string path = Path.Combine(App.DataDirectory, "logs"); Directory.CreateDirectory(path);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "explorer.exe")) { UseShellExecute = true };
        start.ArgumentList.Add(path); Process.Start(start)?.Dispose(); return Task.CompletedTask;
    });
    [RelayCommand] private Task ExportReportAsync() => RunAsync(async token =>
    {
        await SampleAsync(token);
        var dialog = new SaveFileDialog { Filter = "Relatório JSON (*.json)|*.json", FileName = $"Nexus-diagnostico-{DateTime.Now:yyyyMMdd-HHmm}.json" };
        if (dialog.ShowDialog() != true) return;
        var report = new { GeneratedAt = DateTimeOffset.Now, MachineName, Hardware = latest, ActivePowerPlan,
            Recommendations = Recommendations.Select(r => new { r.Name, r.Detail, r.Benefit, r.Metadata }),
            Changes = History.Select(h => new { h.Name, h.Record.CreatedAt, h.Status }) };
        await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), token);
        Status = "Relatório exportado localmente. Contém identificação do computador e nomes de processos; revise antes de compartilhar.";
    });
    [RelayCommand] private void Cancel() => currentOperation?.Cancel();
    [RelayCommand] private void GoSettings() => SelectedPage = 8;

    private async Task RefreshPowerAsync(CancellationToken token)
    {
        var plans = await Task.Run(power.Plans, token); Guid active = await Task.Run(power.Active, token);
        Replace(PowerPlans, plans); SelectedPowerPlan = PowerPlans.FirstOrDefault(p => p.Id == active);
        ActivePowerPlan = SelectedPowerPlan?.Name ?? active.ToString();
    }
    private HashSet<string> SelectedCategories() => CleanupCategories.Where(c => c.Selected).Select(c => c.Rule.Id).ToHashSet();
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        IsBusy = true; HasError = false; Indeterminate = true; Progress = 0;
        currentOperation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try { await action(currentOperation.Token); }
        catch (OperationCanceledException) { Status = "Operação cancelada. Consulte o histórico para conferir as etapas concluídas."; }
        catch (Exception error)
        {
            HasError = true; Status = error is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 } ? "Elevação cancelada pelo usuário. Nenhuma ferramenta administrativa foi iniciada." : "Não foi possível concluir: " + error.Message;
            App.Log.Write("Operação da interface", error);
            MessageBox.Show(Status, "NEXUS • operação não concluída", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { currentOperation.Dispose(); currentOperation = null; IsBusy = false; Indeterminate = false; }
    }
    private static bool Confirm(string text) => MessageBox.Show(text, "NEXUS • confirmar alteração", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    private static void Append(ObservableCollection<double> values, double value) { values.Add(value); if (values.Count > 60) values.RemoveAt(0); }
    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values) { collection.Clear(); foreach (var item in values) collection.Add(item); }
    partial void OnSelectedThemeChanged(string value) => ApplyTheme();
    partial void OnSelectedAccentChanged(string value) => ApplyTheme();
    private void ApplyTheme()
    {
        bool light = SelectedTheme == "Claro";
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(light ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark);
        var colors = light ? new[] { "#F2F5FA", "#E7EDF5", "#FFFFFF", "#CFD8E5", "#172337", "#52637A" } : new[] { "#0B0E14", "#111620", "#171D29", "#283244", "#F1F5FB", "#97A5BB" };
        string[] keys = ["CanvasBrush", "PanelBrush", "CardBrush", "LineBrush", "TextBrush", "MutedBrush"];
        for (int i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(SelectedAccent switch { "Roxo" => light ? "#8660DA" : "#B497FF", "Azul" => light ? "#4D8CE0" : "#79AEFF", _ => light ? "#259FAF" : "#55D9E8" }));
    }
    public void Dispose() { lifetime.Cancel(); currentOperation?.Cancel(); /* CTS lives until awaited operations observe cancellation. */ }
}
