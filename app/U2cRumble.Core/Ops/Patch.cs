using U2cRumble.Core.Firmware;
using U2cRumble.Core.Hid;
using U2cRumble.Core.Protocol;

namespace U2cRumble.Core.Ops;

/// <summary>Patch: stock v1.09 bank A plus tweaks, written through the bootloader.</summary>
public static class Patch
{
    public const string Pending =
        "A firmware update is pending on this controller, and it could replace the tweaks the next time the controller starts. " +
        "Nothing was written. Start the controller normally once, then check again.";

    /// <summary>The controller must have this backup's bootloader and address.</summary>
    public static void CheckUnit(IChannel channel, Backup backup, string mode = "boot")
    {
        if (backup.Data.Length != Image.FlashSize)
            throw new OpException("This isn't a full backup of the controller.");
        try
        {
            if (mode == "boot" && BootProtocol.Kind(channel) != "controller")
                throw new OpException("The device in update mode isn't a controller. The 2.4 GHz dongle isn't supported.");
            if (!Probe.Read(channel, mode, 0, Image.BootEnd).SequenceEqual(backup.Data[..Image.BootEnd]))
                throw new OpException("This device doesn't match the backup, because its update-mode code is different. Nothing was written.");
            var mac = Image.MacSector;
            if (!Image.SameUnit(Probe.Read(channel, mode, mac, Image.Sector), backup.Data[mac..(mac + Image.Sector)]))
                throw new OpException("This backup is from another controller, because the address is different. Nothing was written.");
        }
        catch (ProtocolException e)
        {
            throw new OpException($"{e.Message}. Nothing was written.");
        }
    }

    /// <summary>A staged update could replace what is written at the next start.</summary>
    public static void CheckNoPendingUpdate(IChannel channel, string message = Pending, string mode = "boot")
    {
        bool pending;
        try
        {
            pending = Probe.PendingUpdate(channel, mode);
        }
        catch (ProtocolException e)
        {
            throw new OpException($"{e.Message}. Nothing was written.");
        }
        if (pending)
            throw new OpException(message);
    }

    public static byte[] CurrentBankA(IChannel channel, string mode = "boot")
    {
        try
        {
            return Probe.Read(channel, mode, Image.Base, Image.AppSize);
        }
        catch (ProtocolException e)
        {
            throw new OpException($"{e.Message}. Nothing was written.");
        }
    }

    public static Plan PlanPatch(IChannel channel, Backup backup, byte[] stock, IReadOnlyList<string> keys, string mode = "boot")
    {
        CheckUnit(channel, backup, mode);
        CheckNoPendingUpdate(channel, mode: mode);
        var current = CurrentBankA(channel, mode);
        var replacing = !Recipes.Identify(current).Known;
        if (replacing)   // the whole bank A is written from stock either way, so 1.09 is all that matters
        {
            var (version, _) = Probe.DetectVersion(channel, mode, current);
            if (version is null)
                throw new OpException("This controller's firmware isn't the original 1.09, with or without this app's tweaks, " +
                                      "and its version can't be identified, so nothing was written. It may be another firmware version, " +
                                      "which you can update with 8BitDo's own updater, or firmware modified outside this app, " +
                                      "which restoring your backup replaces.");
            if (Math.Abs(version.Value - Image.SupportedVersion) > 1e-6)
                throw new OpException($"This controller has firmware {version:F2}, and this app only supports " +
                                      $"{Image.SupportedVersion:F2}, so nothing was written. Update it with 8BitDo's own updater first.");
        }

        byte[] targetBank;
        try
        {
            targetBank = Recipes.Build(stock, keys);
        }
        catch (RecipeException e)
        {
            throw new OpException(e.Message);
        }

        var target = new byte[Image.FlashSize];
        Array.Copy(targetBank, 0, target, Image.Base, targetBank.Length);
        var diff = current.Zip(targetBank).Count(pair => pair.First != pair.Second);
        var note = replacing ? "This replaces firmware 1.09 that this app doesn't recognise. " : "";
        var shown = Tweaks.Shown(keys);
        var tweaks = shown.Count > 0 ? Plan.Listed(shown.Select(k => Recipes.All[k].Title)) : "no tweaks";
        return new Plan(target, diff > 0, Array.Empty<int>(), diff,
            $"{note}Installing changes {Plan.Count(diff, "byte")} of the firmware, with {tweaks}.");
    }
}
