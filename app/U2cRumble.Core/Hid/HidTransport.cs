using System.Runtime.InteropServices;
using System.Text;

namespace U2cRumble.Core.Hid;

/// <summary>Finds HID interfaces and opens them for 64-byte report exchange, like the Python hidapi transport.</summary>
public static class HidTransport
{
    private const int ProductStringBytes = 256;

    /// <summary>Every HID interface Windows exposes, with its usage page and bus.</summary>
    public static IReadOnlyList<DeviceInfo> ListDevices()
    {
        var devices = new List<DeviceInfo>();
        var guid = Native.GUID_DEVINTERFACE_HID;
        var set = Native.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero,
            Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == Native.InvalidHandle)
            return devices;

        try
        {
            for (uint i = 0; ; i++)
            {
                var data = new Native.SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>(),
                };
                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data))
                    break;

                var path = GetDevicePath(set, ref data, out var deviceInfo);
                if (path is null)
                    continue;

                var info = Describe(path, set, ref deviceInfo);
                if (info is not null)
                    devices.Add(info);
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(set);
        }

        return devices;
    }

    /// <summary>Opens one interface: report ID 0x81 for the update channel, 0 for the bootloader.</summary>
    public static IChannel OpenChannel(DeviceInfo device, int reportId) => new HidChannel(device, reportId);

    // the device interface path, which is what CreateFile needs
    private static string? GetDevicePath(IntPtr set, ref Native.SP_DEVICE_INTERFACE_DATA data,
        out Native.SP_DEVINFO_DATA deviceInfo)
    {
        deviceInfo = default;
        Native.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
        if (size == 0)
            return null;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            var info = new Native.SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<Native.SP_DEVINFO_DATA>() };
            if (!Native.SetupDiGetDeviceInterfaceDetailWithInfo(set, ref data, buffer, size, out _, ref info))
                return null;
            deviceInfo = info;

            // the path follows the DWORD cbSize; try both plausible offsets just in case
            foreach (var offset in new[] { 4, 8 })
            {
                var s = Marshal.PtrToStringUni(IntPtr.Add(buffer, offset));
                if (!string.IsNullOrEmpty(s) && s.StartsWith("\\\\", StringComparison.Ordinal))
                    return s;
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // opens the interface briefly to read its attributes, usage page and product name
    private static DeviceInfo? Describe(string path, IntPtr set, ref Native.SP_DEVINFO_DATA deviceInfo)
    {
        var handle = Native.CreateFile(path,
            Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == Native.InvalidHandle)
            return null;

        try
        {
            var attributes = new Native.HIDD_ATTRIBUTES
            {
                Size = Marshal.SizeOf<Native.HIDD_ATTRIBUTES>(),
            };
            if (!Native.HidD_GetAttributes(handle, ref attributes))
                return null;

            var caps = ReadCaps(handle);
            var name = ReadName(handle, set, ref deviceInfo);

            return new DeviceInfo(
                attributes.VendorID,
                attributes.ProductID,
                caps?.UsagePage ?? 0,
                BusFrom(path),
                name,
                path,
                attributes.VersionNumber);
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private static Native.HIDP_CAPS? ReadCaps(IntPtr handle)
    {
        if (!Native.HidD_GetPreparsedData(handle, out var preparsed))
            return null;
        try
        {
            var caps = new Native.HIDP_CAPS { Reserved = new ushort[17] };
            return Native.HidP_GetCaps(preparsed, ref caps) == Native.HIDP_STATUS_SUCCESS ? caps : null;
        }
        finally
        {
            Native.HidD_FreePreparsedData(preparsed);
        }
    }

    // Windows can return an empty product string for a BLE HID interface; the registry name is the fallback
    private static string ReadName(IntPtr handle, IntPtr set, ref Native.SP_DEVINFO_DATA deviceInfo)
    {
        var product = ReadProductString(handle);
        if (product.Length > 0)
            return product;

        return ReadRegistryString(set, ref deviceInfo, Native.SPDRP_FRIENDLYNAME)
               ?? ReadRegistryString(set, ref deviceInfo, Native.SPDRP_DEVICEDESC)
               ?? "";
    }

    private static string? ReadRegistryString(IntPtr set, ref Native.SP_DEVINFO_DATA deviceInfo, uint property)
    {
        var buffer = new byte[1024];
        if (!Native.SetupDiGetDeviceRegistryProperty(set, ref deviceInfo, property,
                out _, buffer, (uint)buffer.Length, out var required))
            return null;

        var text = Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(buffer.Length, required));
        var end = text.IndexOf('\0');
        return end >= 0 ? text[..end] : text;
    }

    private static string ReadProductString(IntPtr handle)
    {
        var buffer = Marshal.AllocHGlobal(ProductStringBytes);
        try
        {
            for (var i = 0; i < ProductStringBytes; i++)
                Marshal.WriteByte(buffer, i, 0);
            return Native.HidD_GetProductString(handle, buffer, ProductStringBytes)
                ? Marshal.PtrToStringUni(buffer) ?? ""
                : "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // the BLE HID service path is the reliable sign of a Bluetooth interface on Windows
    private static string BusFrom(string path)
    {
        var upper = path.ToUpperInvariant();
        if (upper.Contains("00001812-0000-1000-8000-00805F9B34FB") || upper.Contains("BTHENUM"))
            return "Bluetooth";
        return "";
    }
}

/// <summary>A synchronous-overlapped HID channel with per-call timeouts.</summary>
internal sealed class HidChannel : IChannel
{
    private const int ReportSize = 64;

    private IntPtr _handle;
    private readonly IntPtr _event;
    private readonly int _reportId;
    private readonly int _inputLen;
    private readonly int _outputLen;
    private bool _stale;
    private bool _dead;
    private bool _disposed;

    public HidChannel(DeviceInfo device, int reportId)
    {
        _reportId = reportId;
        _handle = Native.CreateFile(device.Path,
            Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (_handle == Native.InvalidHandle)
            throw new PermissionDeniedException(
                $"The device couldn't be opened: Win32 error {Marshal.GetLastWin32Error()}.");

        _event = Native.CreateEvent(IntPtr.Zero, true, false, null);
        if (_event == IntPtr.Zero)
            throw new PermissionDeniedException("Couldn't create the I/O event.");

        var caps = GetCaps();
        _inputLen = caps.InputReportByteLength > 0 ? caps.InputReportByteLength : 128;
        _outputLen = caps.OutputReportByteLength > 0 ? caps.OutputReportByteLength : ReportSize;
    }

    public byte[]? Xfer(byte[] report, double timeoutSeconds = 0.5, Func<byte[], bool>? accept = null)    {
        ThrowIfDisposed();

        if (_stale)
        {
            var drain = new byte[Math.Max(_inputLen, ReportSize)];
            for (var i = 0; i < 32; i++)
            {
                if (!TryRead(drain, 1, out _))
                    break;
            }
            _stale = false;
        }

        var data = new byte[ReportSize];
        Array.Copy(report, data, Math.Min(report.Length, ReportSize));
        if (_reportId == 0)
        {
            var withId = new byte[ReportSize + 1];
            Array.Copy(data, 0, withId, 1, ReportSize);
            data = withId;
        }
        data = Fit(data, _outputLen);

        var timeoutMs = (int)Math.Round(timeoutSeconds * 1000);
        if (!TryWrite(data, Math.Max(1, timeoutMs)))
        {
            _stale = true;
            return null;
        }

        var deadline = Environment.TickCount64 + Math.Max(0, timeoutMs);
        while (true)
        {
            var left = deadline - Environment.TickCount64;
            if (left <= 0)
            {
                _stale = true;
                return null;
            }

            var buffer = new byte[Math.Max(_inputLen, ReportSize)];
            if (!TryRead(buffer, (int)Math.Max(1, left), out var read) || read <= 0)
            {
                _stale = true;
                return null;
            }

            var reply = new byte[read];
            Array.Copy(buffer, reply, read);

            // a device with no report IDs (the bootloader) gets a 0x00 report ID from the driver; hidapi drops it
            if (_reportId == 0 && reply.Length == ReportSize + 1)
                reply = reply[1..];

            if (accept is null || accept(reply))
                return reply;
        }
    }

    // pads or trims the report to the device's declared output length
    private static byte[] Fit(byte[] data, int length)
    {
        if (length <= 0 || data.Length == length)
            return data;
        var output = new byte[length];
        Array.Copy(data, output, Math.Min(data.Length, length));
        return output;
    }

    private Native.HIDP_CAPS GetCaps()
    {
        if (!Native.HidD_GetPreparsedData(_handle, out var preparsed))
            return new Native.HIDP_CAPS { Reserved = new ushort[17] };
        try
        {
            var caps = new Native.HIDP_CAPS { Reserved = new ushort[17] };
            return Native.HidP_GetCaps(preparsed, ref caps) == Native.HIDP_STATUS_SUCCESS
                ? caps
                : new Native.HIDP_CAPS { Reserved = new ushort[17] };
        }
        finally
        {
            Native.HidD_FreePreparsedData(preparsed);
        }
    }

    private bool TryRead(byte[] buffer, int timeoutMs, out int read)
    {
        read = 0;
        Native.ResetEvent(_event);
        var overlapped = new Native.OVERLAPPED { hEvent = _event };

        if (Native.ReadFile(_handle, buffer, (uint)buffer.Length, out var count, ref overlapped))
        {
            read = (int)count;
            return true;
        }

        if (Marshal.GetLastWin32Error() != Native.ERROR_IO_PENDING)
            return false;

        return Wait(ref overlapped, timeoutMs, out read);
    }

    private bool TryWrite(byte[] buffer, int timeoutMs)
    {
        Native.ResetEvent(_event);
        var overlapped = new Native.OVERLAPPED { hEvent = _event };

        if (Native.WriteFile(_handle, buffer, (uint)buffer.Length, out _, ref overlapped))
            return true;

        if (Marshal.GetLastWin32Error() != Native.ERROR_IO_PENDING)
            return false;

        return Wait(ref overlapped, timeoutMs, out _);
    }

    // the overlapped structure must keep its address until the request completes, so it travels by reference
    private bool Wait(ref Native.OVERLAPPED overlapped, int timeoutMs, out int transferred)
    {
        transferred = 0;
        var result = Native.WaitForSingleObject(_event, (uint)Math.Max(0, timeoutMs));
        if (result == Native.WAIT_OBJECT_0)
        {
            if (!Native.GetOverlappedResult(_handle, ref overlapped, out var count, false))
                return false;
            transferred = (int)count;
            return true;
        }

        // nothing came back: cancel, then give the driver a moment to report the cancellation
        Native.CancelIoEx(_handle, ref overlapped);
        var end = Environment.TickCount64 + 500;
        while (Environment.TickCount64 < end)
        {
            if (Native.GetOverlappedResult(_handle, ref overlapped, out _, false))
                return false;   // completed (or aborted) after the timeout
            if (Marshal.GetLastWin32Error() != Native.ERROR_IO_INCOMPLETE)
                return false;
            Thread.Sleep(1);
        }

        // the driver won't let go, so dropping the handle is the only way out; the channel is unusable
        Kill();
        return false;
    }

    // closes the handle so a stuck request can't block the thread forever
    private void Kill()
    {
        _dead = true;
        if (_handle != Native.InvalidHandle)
        {
            Native.CloseHandle(_handle);
            _handle = Native.InvalidHandle;
        }
        Thread.Sleep(100);   // give the kernel time to finish with the abandoned request
    }

    private void ThrowIfDisposed()
    {
        if (_disposed || _dead)
            throw new ObjectDisposedException(nameof(HidChannel));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_event != IntPtr.Zero)
            Native.CloseHandle(_event);
        if (_handle != Native.InvalidHandle)
            Native.CloseHandle(_handle);
    }
}
