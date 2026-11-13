using U2cRumble.Core.Hid;

namespace U2cRumble.Core.Protocol;

/// <summary>
/// Bootloader protocol (2dc8:3208), as reversed by jgus from 8BitDo's updater. 64-byte reports, no report ID:
/// [0]=05 [1:3]=cmd [5:7]=data length [7:9]=CRC-16/MODBUS of the data [9:13]=param [13:17]=address [17:]=data.
/// </summary>
public static class BootProtocol
{
    public const int Chunk = 46;
    public const int ControllerId = 0x301B;

    // the literal the dongle's bootloader answers with
    public const int DongleId = 0x301C;

    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return (ushort)crc;
    }

    public static byte[] Request(int cmd, int addr = 0, int param = 0, byte[]? data = null, int? datalen = null)
    {
        data ??= Array.Empty<byte>();
        var report = new byte[64];
        report[0] = 0x05;
        report[1] = (byte)cmd;
        report[2] = (byte)(cmd >> 8);
        var length = datalen ?? data.Length;
        report[5] = (byte)length;
        report[6] = (byte)(length >> 8);
        if (data.Length > 0)
        {
            var crc = Crc16(data);
            report[7] = (byte)crc;
            report[8] = (byte)(crc >> 8);
        }
        report[9] = (byte)param;
        report[10] = (byte)(param >> 8);
        report[11] = (byte)(param >> 16);
        report[12] = (byte)(param >> 24);
        report[13] = (byte)addr;
        report[14] = (byte)(addr >> 8);
        report[15] = (byte)(addr >> 16);
        report[16] = (byte)(addr >> 24);
        Array.Copy(data, 0, report, 17, data.Length);
        return report;
    }

    // both bootloaders build every reply from the request's first 16 bytes, so a late reply can't pass for this one's
    private static byte[]? Call(IChannel channel, byte[] request, double timeout = 1.0, int tries = 3)
    {
        bool Answers(byte[] a) =>
            a.Length >= 18
            && a[0] == 0
            && a[1] == 5
            && a[2] == 0
            && a[4] == request[1]
            && a[14] == request[13]
            && a[15] == request[14]
            && a[16] == request[15]
            && a[17] == request[16];

        for (var i = 0; i < tries; i++)
        {
            var answer = channel.Xfer(request, timeout, Answers);
            if (answer is not null && Answers(answer))
                return answer;
        }
        return null;
    }

    public static byte[] Read(IChannel channel, int addr, int count, Action<int, int>? progress = null)
    {
        var output = new byte[count];
        var done = 0;
        while (done < count)
        {
            var length = Math.Min(Chunk, count - done);
            var answer = Call(channel, Request(5, addr + done, length, datalen: length));
            if (answer is null)
                throw new ProtocolException($"The controller gave no valid reply while reading 0x{addr + done:x}");
            Array.Copy(answer, 18, output, done, length);
            done += length;
            progress?.Invoke(done, count);
        }
        return output;
    }

    public static void Erase(IChannel channel, int addr, int count)
    {
        var timeout = Math.Max(10.0, 0.3 * count / 0x1000);   // up to 0.3 s a sector
        if (Call(channel, Request(4, addr, count), timeout, tries: 1) is null)
            throw new ProtocolException($"Erasing 0x{addr:x}-0x{addr + count:x} failed");
    }

    public static void WriteChunk(IChannel channel, int addr, int param, byte[] data)
    {
        if (Call(channel, Request(3, addr, param, data)) is null)
            throw new ProtocolException($"The controller rejected the write at 0x{addr:x}");
    }

    /// <summary>The bootloader's identity (cmd 8), or null if it doesn't answer.</summary>
    public static int? Identify(IChannel channel)
    {
        var answer = Call(channel, Request(8, param: 4, datalen: 4), tries: 1);
        if (answer is null || answer.Length < 20 || answer[6] != 0x02 || answer[7] != 0x00)
            return null;
        return answer[18] | (answer[19] << 8);
    }

    public static string? Kind(IChannel channel) => KindFrom(channel, Identify(channel));

    /// <summary>Controller or dongle, given the identity already asked with Identify().</summary>
    public static string? KindFrom(IChannel channel, int? who)
    {
        if (who == DongleId)
            return "dongle";
        if (who is not null && who != ControllerId)
            return null;

        var lo = Read(channel, 0, 16);
        var hi = Read(channel, 0x20000, 16);
        if (hi.All(b => b == 0xFF))
            return "controller";
        if (hi.SequenceEqual(lo) && lo.Any(b => b != 0xFF))
            return "dongle";
        return null;
    }
}
