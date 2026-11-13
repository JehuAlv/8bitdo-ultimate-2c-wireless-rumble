using System.Reflection;
using System.Windows;
using System.Windows.Media;
using U2cRumble.Core;
using U2cRumble.Core.Ops;

namespace U2cRumble;

/// <summary>Main window: device status, the flashing steps and a live log of the .NET core.</summary>
public partial class MainWindow : Window
{
    private readonly FlashService _service = new();
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

        Log($"Store: {_service.Store}");
        _ = RefreshDevicesAsync();
    }

    // lists the devices and turns their modes into a one-line status
    private async Task RefreshDevicesAsync()
    {
        SetBusy(true);
        SetStatus("Looking for the controller...", false);
        try
        {
            var (lines, modes) = await Task.Run(() =>
            {
                var found = _service.Find();
                return (_service.DescribeDevices(), found.Select(f => f.Mode).ToList());
            });

            foreach (var line in lines)
                Log(line);
            SetStatus(Summarize(modes), false);
        }
        catch (Exception ex)
        {
            SetStatus("Could not look for the controller.", true);
            Log(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // turns the device modes into a short, plain status line
    private static string Summarize(IReadOnlyCollection<string> modes)
    {
        if (modes.Count == 0)
            return "No controller found. Connect it with the USB cable.";
        if (modes.Contains("boot"))
            return "Controller in update mode. It is ready to back up, install or restore.";
        if (modes.Contains("cable") || modes.Contains("cable_360"))
            return "Controller connected by cable. Ready to back up or install.";
        if (modes.Any(m => m.StartsWith("bluetooth", StringComparison.Ordinal)))
            return "Controller connected over Bluetooth. Flashing still needs the USB cable.";
        if (modes.Any(m => m.StartsWith("dongle", StringComparison.Ordinal)))
            return "Only the 2.4 GHz dongle is connected. This tool needs the controller itself.";
        return "Controller found.";
    }

    // runs one core operation on a background thread and streams its messages into the log
    private async Task RunAsync(string what, Action action)
    {
        SetBusy(true);
        Log(string.Empty);
        Log($"> {what}");
        try
        {
            await Task.Run(action);
        }
        catch (Exception ex)
        {
            Log(ex.Message);
            Log("The task did not finish successfully. Read the messages above.");
        }
        finally
        {
            SetBusy(false);
            await RefreshDevicesAsync();
        }
    }

    // shows the core's progress without flooding the log
    private void ShowProgress(string label, int done, int total)
    {
        var percent = Math.Min(100, done * 100 / Math.Max(total, 1));
        Dispatcher.BeginInvoke(() => SetStatus($"{label}: {percent} %", false));
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Back up the controller's firmware now?\n\nConnect the controller with the USB cable first.",
            "Back up", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (answer == MessageBoxResult.OK)
            await RunAsync("backup", () => _service.Backup(Log, ShowProgress));
    }

    private async void Check_Click(object sender, RoutedEventArgs e) =>
        await RunAsync("check", () => _service.Install(
            new[] { "bt_xbox", "bt_rumble" }, write: false, disclaimerAccepted: false, Log, ShowProgress));

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Install 'Xbox over Bluetooth' and 'Rumble over Bluetooth'?\n\n" +
            "This writes to the controller's firmware. Make sure you have a backup and that the controller is connected by cable.",
            "Install", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.OK)
            await RunAsync("install", () => _service.Install(
                new[] { "bt_xbox", "bt_rumble" },
                write: true,
                disclaimerAccepted: DisclaimerCheck.IsChecked == true,
                Log, ShowProgress));
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Restore the newest recognised backup to the controller?\n\n" +
            "This writes to the controller's firmware and undoes the tweaks.",
            "Restore", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.OK)
            await RunAsync("restore", () => _service.Restore(
                "backup",
                write: true,
                forceUnknown: false,
                disclaimerAccepted: DisclaimerCheck.IsChecked == true,
                Log, ShowProgress));
    }

    private void Disclaimer_Changed(object sender, RoutedEventArgs e) => SetBusy(_busy);

    // enables or disables the buttons; writing stays disabled until the disclaimer is ticked
    private void SetBusy(bool busy)
    {
        _busy = busy;
        var ready = !busy;
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

    // the core runs on a background thread, so marshal every line onto the UI thread
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
