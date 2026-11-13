using U2cRumble.Core.Firmware;
using U2cRumble.Core.Hid;
using U2cRumble.Core.Ops;
using U2cRumble.Core.Protocol;

namespace U2cRumble.Core;

/// <summary>The flashing operations the app offers, without any command line or Python.</summary>
public sealed class FlashService
{
    public const string BootHelp =
        "Turn the controller fully off, then hold LB + RB while plugging in the cable. The small light below the Home ring will blink white.";

    private const string Restarted = "The controller restarted with its new firmware.";
    private const string Replug =
        "The controller hasn't come back yet: unplug it, hold Home for about 12 seconds until it turns off, then connect it again.";
    private const string Installed =
        "Installed. {} If you changed a Bluetooth tweak, remove the controller in Windows Settings \u2192 Bluetooth & devices and pair it again.";
    private const string Restored =
        "Restored. {} If its Bluetooth tweaks changed, remove the controller in Windows Settings \u2192 Bluetooth & devices and pair it again.";
    private const string NotEntered = "The controller didn't restart into update mode, so nothing was written. ";
    private const string InUpdateMode =
        "The controller is in update mode and nothing was written. Try again: the app installs and restores from there too.";
    private const string Unchanged = "The controller already has this firmware, so nothing was written.";
    private const string Checked = "The check passed and nothing was written.";
    private const string DisclaimerHelp =
        "Read DISCLAIMER.md first. There's no warranty. Installing modified firmware probably voids the controller's warranty. " +
        "It only works with the Ultimate 2C on firmware 1.09.";

    private const int BootWait = 8;
    private const int BackWait = 10;
    private const double Step = 0.3;

    private static readonly string[] ControllerModes = { "cable", "cable_360", "boot", "dongle", "dongle_paired" };

    public string Store { get; }

    public FlashService(string? store = null) => Store = store ?? AppState.DefaultStore();

    public IReadOnlyList<Found> Find() => Devices.Find();

    /// <summary>Full read-only backup over the cable or in update mode.</summary>
    public void Backup(Action<string> log, Action<string, int, int> progress) => WithLock(() =>
    {
        var device = Pick("cable", "cable_360", "boot");
        using var channel = Open(device.Info, device.Mode == "boot" ? 0 : 0x81);
        var backup = BackupFile.Take(channel, device.Mode, Store, progress);
        var name = Path.GetFileName(backup.FilePath);
        LogFile.Write(Store, $"backup {name} sha256 {backup.Sha256}");
        log($"Backup saved as \u201c{name}\u201d. The controller has {BackupFile.Describe(backup.State)}.");
        log(backup.FilePath);
        log($"sha256 {backup.Sha256}");
    });

    /// <summary>Check (write: false) or install the chosen tweaks.</summary>
    public void Install(IReadOnlyList<string> tweaks, bool write, bool disclaimerAccepted,
        Action<string> log, Action<string, int, int> progress, Action<Plan>? planned = null) => WithLock(() =>
    {
        NeedAccept(write, disclaimerAccepted);
        var device = Pick("boot", "cable", "cable_360");
        LogFile.Write(Store, write ? "command patch" : "command check");

        var keys = tweaks.Where(k => k.Length > 0).ToList();
        if (keys.Count > 0 && !keys.Contains("ble_interval"))   // every install asks for 12.5 ms
            keys.Add("ble_interval");

        byte[]? stock = null;
        Plan MakePlan(IChannel channel, string mode)
        {
            var backup = BackupFor(channel, mode, recognised: false);
            stock ??= StockFor(backup);
            return Patch.PlanPatch(channel, backup, stock, keys, mode);
        }

        PlanAndWrite(device, write, MakePlan, Installed, log, progress, planned);
    });

    /// <summary>Restore this unit's newest recognised backup.</summary>
    public void Restore(string source, bool write, bool forceUnknown, bool disclaimerAccepted,
        Action<string> log, Action<string, int, int> progress, Action<Plan>? planned = null) => WithLock(() =>
    {
        NeedAccept(write, disclaimerAccepted);
        if (source != "backup")
            throw new OpException("This app restores the controller's own backup.");
        var device = Pick("boot", "cable", "cable_360");
        LogFile.Write(Store, "command restore");

        Plan MakePlan(IChannel channel, string mode)
        {
            var backup = BackupFor(channel, mode, recognised: true);
            return Ops.Restore.PlanRestore(channel, backup, source, null, forceUnknown, mode);
        }

        PlanAndWrite(device, write, MakePlan, Restored, log, progress, planned);
    });

    // prints each device with the mode the toolkit sees
    public IReadOnlyList<string> DescribeDevices()
    {
        var lines = new List<string>();
        foreach (var found in Find())
        {
            var info = found.Info;
            lines.Add($"{found.Mode,-15} {info.Vendor:x4}:{info.Product:x4} \"{info.Name}\"");
            if (found.Mode == "boot")
            {
                using var channel = Open(info, 0);
                var who = BootProtocol.Identify(channel);
                lines.Add(who is not null
                    ? $"{new string(' ', 15)} identifies as 0x{who:x4}"
                    : $"{new string(' ', 15)} doesn't answer the identity query");
            }
        }
        return lines;
    }

    // runs the plan once over the cable, and only then enters update mode to write; the bootloader starts the firmware again
    private void PlanAndWrite(Found device, bool write, Func<IChannel, string, Plan> makePlan, string doneTemplate,
        Action<string> log, Action<string, int, int> progress, Action<Plan>? planned = null)
    {
        if (device.Mode != "boot")
        {
            Plan first;
            using (var cable = Open(device.Info, 0x81))
            {
                Probe.CheckDevice(cable, device.Mode, null);
                first = makePlan(cable, device.Mode);
            }
            planned?.Invoke(first);

            if (!write || (!first.BankA && first.OtherSectors.Count == 0))
            {
                WritePlan(false, null, first, log, progress);
                return;
            }
        }

        var entered = device.Mode != "boot";
        var info = entered ? IntoUpdateMode(device) : device.Info;
        IChannel bootChannel;
        try
        {
            bootChannel = Open(info, 0);
        }
        catch (Exception e) when (e is IOException or PermissionDeniedException)
        {
            throw new OpException(entered ? $"{e.Message}. {InUpdateMode}" : e.Message);
        }

        var erasing = false;
        var wrote = false;
        var restart = false;
        try
        {
            var plan = makePlan(bootChannel, "boot");
            planned?.Invoke(plan);
            wrote = WritePlan(write, bootChannel, plan, log, progress, () => erasing = true);
            restart = entered || wrote;
        }
        catch
        {
            restart = entered && !erasing;
            throw;
        }
        finally
        {
            try
            {
                if (restart)
                    bootChannel.Xfer(BootProtocol.Request(7), 0.5);
            }
            catch (Exception)
            {
                // not coming back is said below; it mustn't hide what happened
            }
            finally
            {
                bootChannel.Dispose();
            }
        }

        var cameBack = restart && Back();
        if (wrote)
            log(doneTemplate.Replace("{}", cameBack ? Restarted : Replug));
    }

    // writes the plan unless it is a check or changes nothing; returns whether it wrote
    private static bool WritePlan(bool write, IChannel? channel, Plan plan, Action<string> log,
        Action<string, int, int> progress, Action? markErasing = null)
    {
        if (!plan.BankA && plan.OtherSectors.Count == 0)
        {
            log(Unchanged);
            return false;
        }
        if (!write)
        {
            log($"{Checked} {plan.Summary}");
            return false;
        }

        log(plan.Summary);
        markErasing?.Invoke();
        Plan.Apply(channel!, plan, progress);
        return true;
    }

    // by cable: restart the controller into update mode, once the update channel shows it isn't the dongle
    private DeviceInfo IntoUpdateMode(Found device)
    {
        using (var channel = Open(device.Info, 0x81))
        {
            Probe.CheckDevice(channel, device.Mode, null);
            AppProtocol.EnterUpdateMode(channel);
        }

        var boot = WaitForMode("boot", BootWait);
        return boot?.Info ?? throw new OpException(NotEntered + BootHelp);
    }

    private Found? WaitForMode(string mode, int seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            var found = Find().FirstOrDefault(f => f.Mode == mode);
            if (found is not null)
                return found;
            Thread.Sleep(TimeSpan.FromSeconds(Step));
        }
        return null;
    }

    // whether the controller came back by cable after the bootloader restarted it
    private bool Back()
    {
        var end = DateTime.UtcNow.AddSeconds(BackWait);
        while (DateTime.UtcNow < end)
        {
            if (Find().Any(f => f.Mode is "cable" or "cable_360"))
                return true;
            Thread.Sleep(TimeSpan.FromSeconds(Step));
        }
        return false;
    }

    private Found Pick(params string[] modes)
    {
        var present = Find().Where(f => ControllerModes.Contains(f.Mode)).ToList();
        if (present.Count > 1)
            throw new OpException("More than one device is connected. Unplug all but one controller.");
        if (present.Count == 0 || !modes.Contains(present[0].Mode))
        {
            if (present.Count > 0 && present[0].Mode.StartsWith("dongle", StringComparison.Ordinal))
                throw new OpException("That is the 2.4 GHz dongle, and this only works with the controller.");
            var want = modes.Contains("cable") ? "by cable" : "in update mode. " + BootHelp;
            throw new OpException($"No controller found {want}");
        }
        return present[0];
    }

    // the backup named or the newest of this unit; recognised: the newest whose firmware the app recognises
    private Backup BackupFor(IChannel channel, string mode, bool recognised)
    {
        var bootloader = Probe.Read(channel, mode, 0, Image.BootEnd);
        var mac = Probe.Read(channel, mode, Image.MacSector, Image.Sector);
        var found = BackupFile.FindBackups(Store, bootloader, mac);
        if (found.Count == 0)
            throw new OpException("There's no backup of this controller. Back it up first, connected by cable.");
        if (!recognised)
            return found[0];
        return BackupFile.Restorable(found)
               ?? throw new OpException("None of this controller's backups has firmware this app recognises, so nothing was written. " +
                                        "You can restore the original firmware 1.09 instead.");
    }

    private byte[] StockFor(Backup backup) =>
        BackupFile.FindOriginal(backup, Store)
        ?? throw new OpException("None of your backups contains the original firmware 1.09. Download it from 8BitDo's server instead.");

    private void NeedAccept(bool write, bool accepted)
    {
        if (!write)
            return;
        if (!AppState.Accepted(Store))
        {
            if (!accepted)
                throw new OpException("You haven't accepted the disclaimer yet. " + DisclaimerHelp);
            AppState.Accept(Store);
        }
    }

    private void WithLock(Action action)
    {
        var lockFile = AppState.LockDevice(Store);
        if (lockFile is null)
            throw new OpException("Another copy of the app, or the command line, is using the controller. Wait until it finishes, then try again.");
        try
        {
            action();
        }
        finally
        {
            AppState.UnlockDevice(lockFile);
        }
    }

    private static IChannel Open(DeviceInfo info, int reportId) => HidTransport.OpenChannel(info, reportId);
}
