using U2cRumble.Core.Firmware;
using U2cRumble.Core.Hid;
using U2cRumble.Core.Protocol;

namespace U2cRumble.Core.Ops;

/// <summary>Restore: put back this unit's backup, or 8BitDo's stock v1.09 bank A.</summary>
public static class Restore
{
    /// <summary>Bank A may be in any state (this exists for damaged firmware); the unit checks still apply.</summary>
    public static Plan PlanRestore(IChannel channel, Backup backup, string source, byte[]? stock,
        bool forceUnknown = false, string mode = "boot")
    {
        Patch.CheckUnit(channel, backup, mode);
        if (source is not ("backup" or "stock"))
            throw new OpException($"unknown restore source '{source}'");

        var current = Patch.CurrentBankA(channel, mode);
        if (source == "stock")   // a backup restore puts the update record back too
        {
            Patch.CheckNoPendingUpdate(channel,
                "A firmware update is pending on this controller, and it could replace the restored firmware the next time " +
                "the controller starts. Nothing was written. Restoring your backup still works.", mode);

            var (version, _) = Probe.DetectVersion(channel, mode, current);
            if (version is null && !forceUnknown)
                throw new OpException("This controller's firmware version can't be identified, so nothing was written. Restoring your " +
                                      "own backup still works, or you can use \"Install Firmware 1.09 Anyway…\" in the Diagnostics tab if you accept the risk.");
            if (version is not null && Math.Abs(version.Value - Image.SupportedVersion) > 1e-6)
                throw new OpException($"This controller has firmware {version:F2}, and this app only supports {Image.SupportedVersion:F2}. " +
                                      "Update it with 8BitDo's own updater first. Restoring your own backup still works.");
        }

        var target = (byte[])backup.Data.Clone();
        if (source == "stock")
        {
            if (stock is null || stock.Length != Image.BankASize)
                throw new OpException("There's no original firmware 1.09 to restore.");
            Array.Copy(Recipes.Whole(stock), 0, target, Image.Base, Image.AppSize);
        }

        var want = target[Image.Base..Image.AppEnd];
        var others = new List<int>();
        if (source == "backup")
        {
            byte[] now;
            try
            {
                now = Probe.Read(channel, mode, Image.BankAEraseEnd, Image.ConfigStart - Image.BankAEraseEnd);
            }
            catch (ProtocolException e)
            {
                throw new OpException($"{e.Message}. Nothing was written.");
            }
            for (var addr = Image.BankAEraseEnd; addr < Image.ConfigStart; addr += Image.Sector)
            {
                var offset = addr - Image.BankAEraseEnd;
                if (!now[offset..(offset + Image.Sector)].SequenceEqual(target[addr..(addr + Image.Sector)]))
                    others.Add(addr);
            }
        }

        var diff = current.Zip(want).Count(pair => pair.First != pair.Second);
        var what = source == "backup"
            ? $"This restores the backup \u201c{System.IO.Path.GetFileName(backup.FilePath)}\u201d, made on {BackupFile.Taken(backup.FilePath)}, " +
              $"which has {BackupFile.Describe(backup.State)}."
            : "This installs 8BitDo's original firmware 1.09.";
        var blocks = others.Count > 0
            ? $" and {Plan.Count(others.Count, "other block")} of the controller's memory"
            : "";
        return new Plan(target, !current.SequenceEqual(want), others, diff,
            $"{what} It changes {Plan.Count(diff, "byte")} of the firmware{blocks}.");
    }
}
