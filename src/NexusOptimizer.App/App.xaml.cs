using System.Windows;
using System.Security.Principal;
using System.Runtime.InteropServices;
using NexusOptimizer.Core;

namespace NexusOptimizer.App;

public partial class App : Application
{
    private Mutex? instance;
    public static bool IsSmokeTest { get; } = Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.Ordinal);
    public static string DataDirectory { get; } = IsSmokeTest
        ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NexusOptimizer-smoke-" + Environment.ProcessId)
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusOptimizer");
    public static FileLog Log { get; } = new(System.IO.Path.Combine(DataDirectory, "logs"));
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (IsSmokeTest)
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Error;
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingDiagnostics());
        }
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        DispatcherUnhandledException += (_, args) => { Log.Write("Falha da interface", args.Exception); args.Handled = true; MessageBox.Show("Não foi possível concluir a ação. " + args.Exception.Message, "NEXUS • erro", MessageBoxButton.OK, MessageBoxImage.Error); };
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Write("Tarefa não observada", args.Exception); args.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Write("Falha fatal", args.ExceptionObject as Exception);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        { MessageBox.Show("Esta versão requer Windows 11 x64.", "NEXUS OPTIMIZER"); Shutdown(1); return; }
        instance = new Mutex(true, @"Local\NexusOptimizer-" + WindowsIdentity.GetCurrent().User?.Value + (IsSmokeTest ? "-smoke-" + Environment.ProcessId : ""), out bool created);
        if (!created) { MessageBox.Show("O NEXUS OPTIMIZER já está aberto nesta sessão."); Shutdown(); return; }
        new MainWindow().Show();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
