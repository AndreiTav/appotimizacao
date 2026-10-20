using System.ComponentModel;
using NexusOptimizer.App.ViewModels;

namespace NexusOptimizer.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel model = new();
    public MainWindow()
    {
        InitializeComponent(); DataContext = model;
        Loaded += async (_, _) =>
        {
            await model.InitializeAsync();
            if (App.IsSmokeTest) await SmokeTestAsync();
        };
        Activated += (_, _) => model.IsWindowActive = true;
        Deactivated += (_, _) => model.IsWindowActive = false;
        Closing += OnClosing;
        Closed += (_, _) => model.Dispose();
    }
    private async Task SmokeTestAsync()
    {
        var errors = new List<string>();
        try
        {
            if (model.HasError) errors.Add(model.Status);
            if (!IsLoaded || !IsVisible) errors.Add("Janela principal não foi exibida.");
            if (model.PowerPlans.Count == 0) errors.Add("Nenhum plano de energia detectado.");
            if (model.MemoryValue == "Não disponível") errors.Add("Leitura da memória física falhou.");
            for (int page = 0; page < model.Pages.Length; page++)
            {
                model.SelectedPage = page; UpdateLayout();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            if (model.History.Count != 0) errors.Add("O smoke test não deve aplicar alterações.");
            errors.AddRange(BindingDiagnostics.Errors);
        }
        catch (Exception error) { errors.Add(error.ToString()); }
        try
        {
            string? resultPath = Environment.GetEnvironmentVariable("NEXUS_SMOKE_RESULT");
            if (!string.IsNullOrWhiteSpace(resultPath))
                await System.IO.File.WriteAllTextAsync(resultPath, System.Text.Json.JsonSerializer.Serialize(new { Passed = errors.Count == 0, Pages = model.Pages.Length, Errors = errors }));
        }
        catch (Exception error) { errors.Add(error.ToString()); }
        model.Dispose(); System.Windows.Application.Current.Shutdown(errors.Count == 0 ? 0 : 1);
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!model.IsBusy) return;
        e.Cancel = true; model.CancelCommand.Execute(null);
        model.Status = "Cancelamento solicitado. Aguarde a conclusão ou a reversão da operação antes de fechar.";
    }
}
