using U2cRumble.Core.Hid;

namespace U2cRumble.Core.Ops;

/// <summary>One controller-like device and the mode it is in.</summary>
public sealed record Found(DeviceInfo Info, string Mode);

/// <summary>Find the controller (or dongle) in whatever mode it is in.</summary>
public static class Devices
{
    // the update channel of the running controller is vendor page 0xff7a
    private static readonly (ushort Vendor, ushort Product, ushort? Page, string Mode)[] Modes =
    {
        (0x2DC8, 0x310A, 0xFF7A, "cable"),
        (0x045E, 0x028E, 0xFF7A, "cable_360"),
        (0x2DC8, 0x3208, null, "boot"),
        (0x2DC8, 0x301C, 0xFFA0, "dongle"),
        (0x045E, 0x0B13, null, "bluetooth_xbox"),
        (0x2DC8, 0x301B, null, "bluetooth"),
    };

    // the patched dongle's bcdDevice; the controller by cable keeps 0x0114
    private const ushort DongleBcd = 0x0103;

    /// <summary>Every controller-like device found; a Bluetooth pad shows several HID interfaces and is listed once.</summary>
    public static IReadOnlyList<Found> Find()
    {
        var output = new List<Found>();
        foreach (var device in HidTransport.ListDevices())
        {
            foreach (var (vendor, product, page, mode) in Modes)
            {
                if (device.Vendor != vendor || device.Product != product || (page is not null && device.UsagePage != page))
                    continue;

                if (mode.StartsWith("bluetooth", StringComparison.Ordinal))
                {
                    if (!device.Transport.Contains("Bluetooth", StringComparison.Ordinal)
                        || output.Any(f => f.Mode == mode))
                        continue;
                }

                output.Add(new Found(device,
                    mode == "cable_360" && device.Version == DongleBcd ? "dongle_paired" : mode));
            }
        }
        return output;
    }
}
