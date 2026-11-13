using U2cRumble.Core.Firmware;
using U2cRumble.Core.Hid;
using U2cRumble.Core.Protocol;

namespace U2cRumble.Core.Ops;

/// <summary>What a controller holds: firmware state, version, pending update and Home ring level.</summary>
public sealed record Firmware(
    FirmwareState State,
    double? Version,
    string VersionFrom,
    bool Pending = false,
    int? BootIdentity = null,
    int? RingLevel = null);

/// <summary>Read the connected controller (read-only): which firmware it holds, its version, and which tweaks are applied.</summary>
public static class Probe
{
    /// <summary>Read n bytes by either channel; progress(done, total) is called along the way.</summary>
    public static byte[] Read(IChannel channel, string mode, int addr, int count, Action<int, int>? progress = null)
    {
        if (mode == "boot")
            return BootProtocol.Read(channel, addr, count, progress);

        var output = new List<byte>(count);
        for (var a = addr; a < addr + count; a += BootProtocol.Chunk)
        {
            var chunk = AppProtocol.Read(channel, a, Math.Min(BootProtocol.Chunk, addr + count - a));
            if (chunk is null)
                throw new OpException($"The controller didn't answer at 0x{a:x}.");
            output.AddRange(chunk);
            if (progress is not null && output.Count % 0x4000 < BootProtocol.Chunk)
                progress(output.Count, count);
        }
        return output.ToArray();
    }

    public static void CheckDevice(IChannel channel, string mode, int? identity)
    {
        if (mode == "boot")
        {
            if (BootProtocol.KindFrom(channel, identity) != "controller")
                throw new OpException("The device in update mode isn't a controller. The 2.4 GHz dongle isn't supported.");
            return;
        }

        if (AppProtocol.OpenSession(channel) is null)
            throw new OpException("The controller isn't answering. Unplug it, plug it back in and try again.");
        CheckNotDongle(channel);
    }

    /// <summary>On an open update channel: a paired dongle has the controller's USB ID, but its 0x10000 is blank.</summary>
    public static void CheckNotDongle(IChannel channel)
    {
        var head = AppProtocol.Read(channel, 0x10000, 16);
        if (head is null)
            throw new OpException("The controller isn't answering. Unplug it, plug it back in and try again.");
        if (head.All(b => b == 0xFF))
            throw new OpException("This looks like the 2.4 GHz dongle, which this app doesn't support.");
    }

    /// <summary>Whether the update record holds a staged update.</summary>
    public static bool PendingUpdate(IChannel channel, string mode)
    {
        var record = Read(channel, mode, Image.UpdateRecord, 0x24);
        return HasValue(record[0x18..0x1C]) || HasValue(record[0x20..0x24]);
    }

    private static bool HasValue(byte[] field) =>
        field.Any(b => b != 0x00) && field.Any(b => b != 0xFF);

    /// <summary>Firmware version from the update record (byte +0x0c is the version times 100), or null if blank.</summary>
    public static double? ParseVersion(byte[] record)
    {
        var value = record.Length > 0x0C ? record[0x0C] : 0;
        return value is 0 or 0xFF ? null : value / 100.0;
    }

    /// <summary>The firmware version and where it was read from.</summary>
    public static (double? Version, string From) DetectVersion(IChannel channel, string mode, byte[]? bankA = null)
    {
        try
        {
            var version = ParseVersion(Read(channel, mode, Image.UpdateRecord, 16));
            if (version is not null)
                return (version, "update record");

            bankA ??= Read(channel, mode, Image.Base, Image.AppSize);
            if (Recipes.Identify(bankA).Known)
                return (Image.SupportedVersion, "bank A hash");

            if (Recipes.Identify(Read(channel, mode, Image.BankB, Image.BankASize)).Known)
                return (Image.SupportedVersion, "bank B hash");
        }
        catch (ProtocolException e)
        {
            throw new OpException(e.Message);
        }
        return (null, "unknown");
    }

    // as ring_init in the firmware: a saved level counts while it was saved under the level installed
    private static int? RingLevel(IChannel channel, string mode, FirmwareState state)
    {
        var installed = -1;
        for (var i = 0; i < Recipes.RingOrder.Length; i++)
        {
            if (state.Recipes.Contains(Recipes.RingOrder[i]))
            {
                installed = i;
                break;
            }
        }
        if (installed < 0 || state.Outdated)
            return null;

        var settings = Read(channel, mode, Image.Settings, Image.SettingsSize);
        var saved = settings[Image.RingSaved - Image.Settings];
        return saved >> 4 == installed && (saved & 15) < Recipes.RingOrder.Length ? saved & 15 : installed;
    }

    public static Firmware ReadFirmware(IChannel channel, string mode)
    {
        var identity = mode == "boot" ? BootProtocol.Identify(channel) : null;
        FirmwareState state;
        bool pending;
        int? ring;
        try
        {
            CheckDevice(channel, mode, identity);
            var bankA = Read(channel, mode, Image.Base, Image.AppSize);
            pending = PendingUpdate(channel, mode);
            state = Recipes.Identify(bankA);
            ring = RingLevel(channel, mode, state);
        }
        catch (ProtocolException e)
        {
            throw new OpException(e.Message);
        }

        var (version, from) = DetectVersion(channel, mode);
        return new Firmware(state, version, from, pending, identity, ring);
    }
}
