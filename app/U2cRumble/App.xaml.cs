using System.IO;
using System.Windows;
using System.Windows.Threading;
using U2cRumble.Core;

namespace U2cRumble;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrash(args.ExceptionObject as Exception);
    }

    // a crash gets a readable message instead of a silent exit, and the full trace goes to disk
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrash(e.Exception);
        MessageBox.Show(
            "Something went wrong and the app has to close.\n\n" + e.Exception.Message +
            "\n\nThe full error was saved to crash.log in the app data folder. " +
            "Your controller is not left half-written: if a write was interrupted, reconnect the controller by cable, " +
            "hold L1 + R1 while plugging it in, and use Restore.",
            "U2cRumble", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }

    // appends the exception to the same folder as the app log
    private static void WriteCrash(Exception? error)
    {
        if (error is null)
            return;
        try
        {
            var store = new FlashService().Store;
            Directory.CreateDirectory(store);
            File.AppendAllText(Path.Combine(store, "crash.log"),
                $"{DateTime.Now:u}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // a failure while saving must not hide the message the user needs
        }
    }
}
