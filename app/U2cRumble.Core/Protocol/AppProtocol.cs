using U2cRumble.Core.Hid;

namespace U2cRumble.Core.Protocol;

/// <summary>
/// Normal-mode reads through the running controller's update channel (output report 0x81, input report 0x02).
/// Reads are <c>81 05 05</c> (any address, at most 46 bytes).
/// </summary>
public static class AppProtocol
{
    private static readonly byte[] SessionStart = Fill(new byte[] { 0x81, 0x05, 0x00, 0x21, 0x01 });
    private static readonly byte[] WriteUnprotect = Fill(new byte[] { 0x81, 0x05, 0xC1 });

    public static byte[] Request(int addr, int count)
    {
        var report = new byte[64];
        report[0] = 0x81;
        report[1] = 0x05;
        report[2] = 0x05;
        report[6] = (byte)count;
        report[7] = (byte)(count >> 8);
        report[14] = (byte)addr;
        report[15] = (byte)(addr >> 8);
        report[16] = (byte)(addr >> 16);
        report[17] = (byte)(addr >> 24);
        return report;
    }

    /// <summary>One chunk, accepted only if the reply echoes status 0, the read command, the length and the address.</summary>
    public static byte[]? Read(IChannel channel, int addr, int count, int tries = 3)
    {
        bool Answers(byte[] r) =>
            r.Length >= 18 + count
            && r[0] == 0x02
            && r[1] == 0x05
            && r[2] == 0x00
            && r[4] == 0x05
            && r[6] == (byte)count
            && r[7] == (byte)(count >> 8)
            && r[14] == (byte)addr
            && r[15] == (byte)(addr >> 8)
            && r[16] == (byte)(addr >> 16)
            && r[17] == (byte)(addr >> 24);

        for (var i = 0; i < tries; i++)
        {
            var reply = channel.Xfer(Request(addr, count), accept: Answers);
            if (reply is not null && Answers(reply))
            {
                var data = new byte[count];
                Array.Copy(reply, 18, data, 0, count);
                return data;
            }
        }
        return null;
    }

    /// <summary>Restarts the controller into update mode. No reply.</summary>
    public static void EnterUpdateMode(IChannel channel) => Command(channel, 0x51, 0x00);

    /// <summary>Make reads work. Returns "cold", "session" or "unprotect" (what had to be sent), or null.</summary>
    public static string? OpenSession(IChannel channel)
    {
        if (Read(channel, 0x10000, 16, tries: 2) is not null)
            return "cold";
        for (var i = 0; i < 4; i++)
        {
            channel.Xfer(SessionStart, 0.8);
            Thread.Sleep(20);
            if (Read(channel, 0x10000, 16, tries: 2) is not null)
                return "session";
        }

        channel.Xfer(WriteUnprotect, 0.8);
        Thread.Sleep(20);
        return Read(channel, 0x10000, 16, tries: 3) is not null ? "unprotect" : null;
    }

    // a command with no reply, so nothing to wait for once it has gone out
    private static void Command(IChannel channel, params byte[] data)
    {
        var report = new byte[64];
        report[0] = 0x81;
        report[1] = 0x05;
        report[2] = 0x00;
        Array.Copy(data, 0, report, 3, data.Length);
        channel.Xfer(report, 0.02);
    }

    private static byte[] Fill(byte[] prefix)
    {
        var report = new byte[64];
        Array.Copy(prefix, report, prefix.Length);
        return report;
    }
}
