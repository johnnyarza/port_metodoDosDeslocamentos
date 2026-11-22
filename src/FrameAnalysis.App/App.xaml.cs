using System.Windows;
using System.Windows.Threading;

namespace FrameAnalysis.App;

/// <summary>
/// Minimal test UI for the frame solver. In WBeamS16App this window would open from a task, with the section and
/// material taken from the task; here they are typed in.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        var window = new MainWindow();
        window.Show();
        if (e.Args.Length > 0) window.OpenFile(e.Args[0]); // a .frame.json given on the command line or double-clicked
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
