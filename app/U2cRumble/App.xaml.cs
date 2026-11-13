using System.Windows;
using System.Windows.Threading;

namespace U2cRumble;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
    }

    // a crash gets a readable message instead of a silent exit
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "Something went wrong and the app has to close.\n\n" + e.Exception.Message +
            "\n\nYour controller is not left half-written: if a write was interrupted, reconnect the controller by cable, " +
            "hold L1 + R1 while plugging it in, and use Restore.",
            "U2cRumble", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }
}
