using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexusOptimizer.App.ViewModels;

namespace NexusOptimizer.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel model = new();
    private bool recoveryMode;
    private TextBlock? recoveryMessage;
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
    internal void EnterRecoveryMode(Exception error)
    {
        if (recoveryMode) return;
        recoveryMode = true;
        model.Dispose();
        // Remove the failing bindings, rather than continuing to render a broken visual tree.
        DataContext = null;
        recoveryMessage = new TextBlock
        {
            Text = "O NEXUS encontrou uma falha na interface e interrompeu novas ações. " +
                "Tarefas em andamento recebem cancelamento e preservam suas rotinas de reversão. " +
                "Consulte o histórico após reiniciar para conferir alterações pendentes.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Margin = new Thickness(0, 20, 0, 20)
        };
        var close = new Button { Content = "Fechar aplicativo", HorizontalAlignment = HorizontalAlignment.Left };
        close.Click += (_, _) => Close();
        var panel = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "O aplicativo foi pausado", FontSize = 28, Foreground = Brushes.White },
                recoveryMessage,
                new TextBlock { Text = "Detalhe: " + error.Message, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray },
                new TextBlock { Text = "Logs: " + System.IO.Path.Combine(App.DataDirectory, "logs"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 16, 0, 16) },
                close
            }
        };
        Content = new Border { Background = new SolidColorBrush(Color.FromRgb(17, 22, 32)), Padding = new Thickness(32), Child = panel };
    }
    private async Task SmokeTestAsync()
    {
        var errors = new List<string>();
        try
        {
            if (recoveryMode) errors.Add("A interface entrou no modo de recuperação durante o smoke test.");
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
        if (recoveryMode && recoveryMessage is not null) recoveryMessage.Text = model.Status;
    }
}
