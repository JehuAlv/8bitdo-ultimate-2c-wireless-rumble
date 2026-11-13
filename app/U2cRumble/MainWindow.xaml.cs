using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace U2cRumble;

/// <summary>Main window: device status, the flashing steps and a live log of the core's output.</summary>
public partial class MainWindow : Window
{
    private readonly CoreRunner? _core;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

        _core = CoreRunner.Locate();
        if (_core is null)
        {
            SetStatus("The flashing core (core/) was not found.", true);
            Log("core/ was not found next to the app. Run the app from the repository so it can find core/u2c_customfw.");
            SetBusy(true);
            return;
        }

        Log($"Core: {_core.CoreDir}");
        _ = RefreshDevicesAsync();
    }

    // runs the devices command and turns its output into a one-line status
    private async Task RefreshDevicesAsync()
    {
        if (_core is null)
            return;

        SetBusy(true);
        SetStatus("Looking for the controller...", false);
        var lines = new List<string>();
        void Collect(string line)
        {
            lines.Add(line);
            Log(line);
        }

        try
        {
            await _core.RunAsync(new[] { "devices" }, Collect);
            SetStatus(Summarize(lines), false);
        }
        catch (Exception ex)
        {
            SetStatus("Could not run the flashing core.", true);
            Log($"Could not start the core: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    // turns the devices output into a short, plain status line
    private static string Summarize(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
            return "No controller found. Connect it with the USB cable.";
        if (lines.Any(l => l.StartsWith("boot")))
            return "Controller in update mode. It is ready to back up, install or restore.";
        if (lines.Any(l => l.StartsWith("cable")))
            return "Controller connected by cable. Ready to back up or install.";
        if (lines.Any(l => l.StartsWith("bluetooth")))
            return "Controller connected over Bluetooth. Flashing still needs the USB cable.";
        if (lines.Any(l => l.Contains("dongle")))
            return "Only the 2.4 GHz dongle is connected. This tool needs the controller itself.";
        return lines[0];
    }

    // runs one core command and streams every line into the log
    private async Task RunAsync(IReadOnlyList<string> args)
    {
        if (_core is null)
            return;

        SetBusy(true);
        Log(string.Empty);
        Log($"> u2c-customfw {string.Join(' ', args)}");
        try
        {
            var code = await _core.RunAsync(args, Log);
            Log($"(exit code {code})");
            if (code != 0)
                Log("The command did not finish successfully. Read the messages above.");
        }
        catch (Exception ex)
        {
            Log($"Could not start the core: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            await RefreshDevicesAsync();
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Back up the controller's firmware now?\n\nConnect the controller with the USB cable first.",
            "Back up", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (answer == MessageBoxResult.OK)
            await RunAsync(new[] { "backup" });
    }

    private async void Check_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(new[] { "patch", "--tweaks", "bt_xbox,bt_rumble" });

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Install 'Xbox over Bluetooth' and 'Rumble over Bluetooth'?\n\n" +
            "This writes to the controller's firmware. Make sure you have a backup and that the controller is connected by cable.",
            "Install", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.OK)
            await RunAsync(new[] { "patch", "--tweaks", "bt_xbox,bt_rumble", "--write", "--accept-disclaimer" });
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Restore the newest recognised backup to the controller?\n\n" +
            "This writes to the controller's firmware and undoes the tweaks.",
            "Restore", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.OK)
            await RunAsync(new[] { "restore", "backup", "--write", "--accept-disclaimer" });
    }

    private void Disclaimer_Changed(object sender, RoutedEventArgs e) => SetBusy(_busy);

    // enables or disables the buttons; writing stays disabled until the disclaimer is ticked
    private void SetBusy(bool busy)
    {
        _busy = busy;
        var ready = !busy && _core is not null;
        RefreshButton.IsEnabled = ready;
        BackupButton.IsEnabled = ready;
        CheckButton.IsEnabled = ready;
        var canWrite = ready && DisclaimerCheck.IsChecked == true;
        InstallButton.IsEnabled = canWrite;
        RestoreButton.IsEnabled = canWrite;
    }

    private void SetStatus(string text, bool warning)
    {
        StatusText.Text = text;
        StatusText.Foreground = warning ? Brushes.Firebrick : Brushes.DarkGreen;
    }

    // the core streams its output from a background thread, so marshal every line onto the UI thread
    private void Log(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Log(line));
            return;
        }
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }
}
