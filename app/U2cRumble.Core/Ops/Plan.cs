using System.Globalization;
using U2cRumble.Core.Firmware;
using U2cRumble.Core.Hid;
using U2cRumble.Core.Protocol;

namespace U2cRumble.Core.Ops;

/// <summary>An operation refused or failed; the message says why and what to do.</summary>
public sealed class OpException : Exception
{
    public OpException(string message) : base(message)
    {
    }
}

/// <summary>A write plan and its execution: the stream proven on hardware, with the hard limits enforced here.</summary>
public sealed record Plan(
    byte[] Target,
    bool BankA,
    IReadOnlyList<int> OtherSectors,
    int DiffBytes,
    string Summary)
{
    public const string Interrupted =
        "The task was interrupted, and the controller may not start until it's finished. Use Restore to install your backup or 8BitDo's original firmware 1.09. Update mode isn't affected.";

    public static string Count(int n, string noun) =>
        n.ToString("N0", CultureInfo.InvariantCulture) + " " + noun + (n == 1 ? "" : "s");

    /// <summary>"a", "a and b", "a, b and c".</summary>
    public static string Listed(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1],
        };
    }

    public static void CheckRanges(Plan plan)
    {
        foreach (var addr in plan.OtherSectors)
        {
            if (addr < Image.BankAEraseEnd || addr >= Image.ConfigStart || addr % Image.Sector != 0)
                throw new OpException($"The app won't write to the memory at 0x{addr:x}.");
        }
    }

    // merges adjacent sectors into runs
    private static List<(int Start, int End)> Runs(IEnumerable<int> sectors)
    {
        var output = new List<(int Start, int End)>();
        foreach (var addr in sectors.OrderBy(a => a))
        {
            if (output.Count > 0 && output[^1].End == addr)
                output[^1] = (output[^1].Start, addr + Image.Sector);
            else
                output.Add((addr, addr + Image.Sector));
        }
        return output;
    }

    public static void Apply(IChannel channel, Plan plan, Action<string, int, int> progress)
    {
        CheckRanges(plan);
        var total = (plan.BankA ? Image.AppSize : 0) + plan.OtherSectors.Count * Image.Sector;
        var done = 0;
        try
        {
            if (plan.BankA)
            {
                BootProtocol.Erase(channel, Image.Base, Image.BankAEraseEnd - Image.Base);
                for (var addr = Image.Base; addr < Image.BankAEnd; addr += BootProtocol.Chunk)
                {
                    var block = Slice(plan.Target, addr, Math.Min(addr + BootProtocol.Chunk, Image.BankAEnd));
                    BootProtocol.WriteChunk(channel, addr, Image.BankASize, block);
                    done += block.Length;
                    progress("write", done, total);
                }

                for (var addr = Image.BankAEnd; addr < Image.AppEnd; addr += BootProtocol.Chunk)   // the same param worked on hardware
                {
                    var block = Slice(plan.Target, addr, Math.Min(addr + BootProtocol.Chunk, Image.AppEnd));
                    if (!block.All(b => b == 0xFF))
                        BootProtocol.WriteChunk(channel, addr, Image.BankASize, block);
                    done += block.Length;
                    progress("write", done, total);
                }

                if (!BootProtocol.Read(channel, Image.Base, Image.AppSize).SequenceEqual(Slice(plan.Target, Image.Base, Image.AppEnd)))
                    throw new OpException("The firmware read back from the controller doesn't match what was written. " + Interrupted);
            }

            foreach (var (lo, hi) in Runs(plan.OtherSectors))
            {
                BootProtocol.Erase(channel, lo, hi - lo);
                for (var addr = lo; addr < hi; addr += BootProtocol.Chunk)
                {
                    var block = Slice(plan.Target, addr, Math.Min(addr + BootProtocol.Chunk, hi));
                    if (!block.All(b => b == 0xFF))
                        BootProtocol.WriteChunk(channel, addr, hi - lo, block);
                    done += block.Length;
                    progress("write", done, total);
                }
                if (!BootProtocol.Read(channel, lo, hi - lo).SequenceEqual(Slice(plan.Target, lo, hi)))
                    throw new OpException($"The memory read back at 0x{lo:x}-0x{hi:x} doesn't match what was written. " + Interrupted);
            }
        }
        catch (ProtocolException e)
        {
            throw new OpException($"{e.Message}. {Interrupted}");
        }
    }

    private static byte[] Slice(byte[] data, int start, int end) => data[start..end];
}
