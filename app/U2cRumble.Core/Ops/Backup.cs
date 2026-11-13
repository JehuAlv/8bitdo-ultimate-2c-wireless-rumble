using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using U2cRumble.Core.Firmware;
using U2cRumble.Core.Hid;
using U2cRumble.Core.Protocol;

namespace U2cRumble.Core.Ops;

/// <summary>A full read-only backup of the controller's flash.</summary>
public sealed record Backup(string FilePath, string Sha256, FirmwareState State, byte[] Data);

/// <summary>Full read-only backup of the controller's flash, over the cable (update channel) or in boot mode.</summary>
public static partial class BackupFile
{
    private static readonly string[] CableModes = { "cable", "cable_360" };

    public static Backup Take(IChannel channel, string mode, string store, Action<string, int, int> progress)
    {
        byte[] a;
        byte[] b;
        try
        {
            if (CableModes.Contains(mode) || mode == "boot")
                Probe.CheckDevice(channel, mode, mode == "boot" ? BootProtocol.Identify(channel) : null);

            a = Probe.Read(channel, mode, 0, Image.FlashSize, (done, total) => progress("pass 1", done, total));
            b = Probe.Read(channel, mode, 0, Image.FlashSize, (done, total) => progress("pass 2", done, total));
        }
        catch (ProtocolException e)
        {
            throw new OpException(e.Message);
        }

        if (!a.SequenceEqual(b))
        {
            var places = 0;
            for (var i = 0; i < a.Length; i += BootProtocol.Chunk)
            {
                var end = Math.Min(i + BootProtocol.Chunk, a.Length);
                if (!a[i..end].SequenceEqual(b[i..end]))
                    places++;
            }
            throw new OpException($"The two reads of the controller's memory differ in {places} place(s), so nothing was saved. " +
                                  "Try again with another cable.");
        }

        Directory.CreateDirectory(store);
        var path = System.IO.Path.Combine(store,
            $"controller-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.bin");
        var suffix = 1;
        while (File.Exists(path))
        {
            var stem = System.IO.Path.GetFileNameWithoutExtension(path).Split('+')[0];
            path = System.IO.Path.Combine(store, $"{stem}+{suffix}.bin");
            suffix++;
        }

        File.WriteAllBytes(path, a);
        var sha = Convert.ToHexString(SHA256.HashData(a)).ToLowerInvariant();
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() != sha)
            throw new OpException($"{path} didn't read back identically.");

        File.AppendAllText(System.IO.Path.Combine(store, "SHA256SUMS"), $"{sha}  {System.IO.Path.GetFileName(path)}\n");
        return new Backup(path, sha, IdentifyFrom(a), a);
    }

    public static Backup Load(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length != Image.FlashSize)
            throw new OpException($"{path} isn't a full backup of the controller ({data.Length} bytes).");
        return new Backup(path, Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), IdentifyFrom(data), data);
    }

    private static FirmwareState IdentifyFrom(byte[] data) =>
        Recipes.Identify(data[Image.Base..Image.AppEnd]);

    private static IEnumerable<Backup> Stored(string store)
    {
        if (!Directory.Exists(store))
            return Enumerable.Empty<Backup>();
        return Directory.GetFiles(store, "controller-*.bin")
            .OrderByDescending(p => p, StringComparer.Ordinal)
            .Select(p =>
            {
                try
                {
                    return Load(p);
                }
                catch (Exception e) when (e is IOException or OpException)
                {
                    return null;
                }
            })
            .Where(b => b is not null)!;
    }

    /// <summary>Backups in the store taken from this unit (same bootloader and address), newest first.</summary>
    public static IReadOnlyList<Backup> FindBackups(string store, byte[] bootloader, byte[] mac) =>
        Stored(store)
            .Where(b => b.Data[..Image.BootEnd].SequenceEqual(bootloader)
                        && Image.SameUnit(b.Data[Image.MacSector..(Image.MacSector + Image.Sector)], mac))
            .ToList();

    /// <summary>The backup Restore uses: the newest whose firmware the app recognises, never a damaged or foreign one.</summary>
    public static Backup? Restorable(IEnumerable<Backup> backups) => backups.FirstOrDefault(b => b.State.Known);

    /// <summary>When a backup was made, from its name, or else from the file.</summary>
    public static string Taken(string path)
    {
        var match = BackupName().Match(System.IO.Path.GetFileName(path));
        var date = match.Success
            ? DateTime.ParseExact(match.Groups[1].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            : File.GetLastWriteTime(path);
        return $"{date.Day} {date.ToString("MMM yyyy", CultureInfo.InvariantCulture)} at {date:HH:mm}";
    }

    /// <summary>The firmware in words: "the original firmware 1.09", "firmware 1.09 with A and B", or that it isn't recognised.</summary>
    public static string Describe(FirmwareState state)
    {
        if (!state.Known)
            return "firmware this app doesn't recognise";
        var titles = Tweaks.Shown(state.Recipes).Select(k => Recipes.All[k].Title).ToList();
        return titles.Count == 0 ? "the original firmware 1.09" : "firmware 1.09 with " + Plan.Listed(titles);
    }

    /// <summary>The original firmware from this backup or any other, in bank A or in bank B.</summary>
    public static byte[]? FindOriginal(Backup backup, string store)
    {
        foreach (var candidate in new[] { backup }.Concat(Stored(store)))
        {
            foreach (var start in new[] { Image.Base, Image.BankB })
            {
                var bank = candidate.Data[start..(start + Image.BankASize)];
                var state = Recipes.Identify(bank);
                if (state.Known && state.Recipes.Count == 0)
                    return bank;
            }
        }
        return null;
    }

    [GeneratedRegex(@"controller-(\d{8}-\d{6})")]
    private static partial Regex BackupName();
}
