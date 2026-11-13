namespace U2cRumble.Core.Hid;

/// <summary>A HID interface Windows exposes, with what the toolkit needs to pick the right one.</summary>
public sealed record DeviceInfo(
    ushort Vendor,
    ushort Product,
    ushort UsagePage,
    string Transport,
    string Name,
    string Path,
    ushort Version);

/// <summary>One open HID interface: 64-byte reports in, 64-byte reports out.</summary>
public interface IChannel : IDisposable
{
    /// <summary>Sends a report and returns the first reply it accepts, or null on timeout.</summary>
    byte[]? Xfer(byte[] report, double timeoutSeconds = 0.5, Func<byte[], bool>? accept = null);
}

/// <summary>The OS refused to open the device.</summary>
public sealed class PermissionDeniedException : Exception
{
    public PermissionDeniedException(string message) : base(message)
    {
    }
}
