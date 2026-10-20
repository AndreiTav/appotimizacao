using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using NexusOptimizer.Core;

namespace NexusOptimizer.Windows.Tests;

public sealed class BindingRegressionTests
{
    [Fact] public void ActualDashboardDiskBindingRendersWithoutWritingToReadOnlySnapshot()
    {
        RunSta(() =>
        {
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var xaml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainWindow.xaml"));
            string expression = xaml.Descendants(ns + "ProgressBar").Select(e => (string?)e.Attribute("Value"))
                .Single(value => value?.Contains("UsedPercent", StringComparison.Ordinal) == true)!;
            // Parse the real binding from the shipped XAML and exercise the WPF binding engine on an STA thread.
            var controlXaml = new XElement(ns + "ProgressBar", new XAttribute("Value", expression));
            var progress = (ProgressBar)XamlReader.Parse(controlXaml.ToString(SaveOptions.DisableFormatting));
            var snapshot = new DriveSnapshot(@"C:\", 100, 25);
            progress.DataContext = snapshot;
            progress.Measure(new Size(200, 20)); progress.Arrange(new Rect(0, 0, 200, 20));
            DrainDispatcher();
            var binding = progress.GetBindingExpression(RangeBase.ValueProperty)!;
            binding.UpdateTarget();
            Assert.Equal(BindingStatus.Active, binding.Status);
            Assert.Equal(BindingMode.OneWay, binding.ParentBinding.Mode);
            Assert.Equal(75d, progress.Value);
            progress.SetCurrentValue(RangeBase.ValueProperty, 10d);
            binding.UpdateSource();
            Assert.Equal(75d, snapshot.UsedPercent);
            binding.UpdateTarget(); DrainDispatcher(); Assert.Equal(75d, progress.Value);
        });
    }

    private static void DrainDispatcher()
    {
        // A detached control still schedules initial data binding on the dispatcher.
        // An STA thread alone does not process that queue as a running WPF application does.
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15))) throw new TimeoutException("WPF binding regression timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
