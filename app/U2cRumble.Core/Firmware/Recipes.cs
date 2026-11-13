using System.Security.Cryptography;

namespace U2cRumble.Core.Firmware;

/// <summary>The tweaks, as hash-checked edits to stock v1.09 bank A, and identification of an image.</summary>
public static class Recipes
{
    public static readonly byte[] Name = "8BitDo Ultimate 2C (X/S)"u8.ToArray();
    public static readonly byte[] OldName = "8BitDo Ultimate 2C (One)"u8.ToArray();
    public const int OldNameLength = 27;

    private static readonly (int Start, int End) PackerSlot = (0xB908, 0xBAD4);

    public static readonly (string Key, string Title, int Level)[] RingLevels =
    {
        ("ring_off", "Home ring: off", 0),
        ("ring_0_1", "Home ring: 0.1 %", 1),
        ("ring_1", "Home ring: 1 %", 10),
        ("ring_5", "Home ring: 5 %", 50),
        ("ring_25", "Home ring: 25 %", 250),
        ("ring_normal", "Home ring: normal", 0xFFFF),
    };

    private static readonly (string Key, string Title, int Level)[] OldRingLevels = RingLevels[..5];

    public static readonly string[] RingOrder =
        { "ring_normal", "ring_25", "ring_5", "ring_1", "ring_0_1", "ring_off" };

    private static readonly (int Start, int End) RingCode = (0x18400, 0x18FF0);
    private const int AlertsFlag = 0x18FF0;
    private const int SettingsFlag = 0x18FF1;
    private const int RestFlag = 0x18FF2;
    private const int StickThreshold = 14;

    private static readonly (Dictionary<string, Recipe> Map, string[] Order) Table = Make();

    public static readonly IReadOnlyDictionary<string, Recipe> All = Table.Map;
    public static readonly IReadOnlyList<string> Order = Table.Order;

    // the tweak table, in the order edits are applied
    private static (Dictionary<string, Recipe> Map, string[] Order) Make()
    {
        var pad = new byte[OldNameLength - Name.Length];
        var slot = PackerSlot.End - PackerSlot.Start;
        var list = new List<Recipe>
        {
            new("bt_xbox", "Xbox Series X|S controller over Bluetooth", Array.Empty<string>(), new[]
            {
                Edit(PackerSlot.Start, Blob("packer.bin").Pad(slot)),
                Edit(0x18274, Blob("report-map.bin")),
                Edit(0x18390, Blob("noop.bin"), supersededBy: "bt_rumble"),
                Edit(0x177A0, U32((uint)Blob("report-map.bin").Length)),
                Edit(0x177A8, U32(0x18274)),
                Edit(0x17764, U32(0x18391)),
                Edit(0x173C0, Convert.FromHexString("025e04130b0905")),
                Edit(0x18098, Convert.FromHexString("0302")),
                Edit(0x172C0, Convert.FromHexString("c403")),
                Edit(0x172DC, Join(Name, pad), accepts: new[] { Join(OldName, pad) }),
                Edit(0x17488, U32((uint)Name.Length)),
                Edit(0x17254, Join(new[] { (byte)(Name.Length + 1), (byte)9 }, Name, pad),
                    accepts: new[] { Join(new[] { (byte)(OldName.Length + 1), (byte)9 }, OldName, pad) }),
                Edit(0xA214, new[] { (byte)(Name.Length + 2), (byte)0xA1 }),
            }),
            new("bt_rumble", "Rumble over Bluetooth", new[] { "bt_xbox" }, new[]
            {
                Edit(0x18390, Blob("rumble.bin").Pad(0x70),
                    accepts: new[] { Blob("rumble02.bin").Pad(0x70), Blob("rumble03.bin").Pad(0x70) }),
            }),
            new("cable_360", "Xbox 360 controller over cable", Array.Empty<string>(), new[]
            {
                Edit(0x17994, Convert.FromHexString("ffffff")),
                Edit(0x17998, Convert.FromHexString("5e048e02")),
            }),
            new("dim_leds", "Dimmer LEDs", Array.Empty<string>(), new[]
            {
                Edit(0x7738, Blob("led.bin")),
                Edit(0xA43C, Blob("hook.bin")),
            }),
        };

        foreach (var (form, at) in new[] { ("", ""), ("03", "@0.3") })
        {
            foreach (var (key, title, level) in RingLevels)
            {
                list.Add(new Recipe(key + at, title, Array.Empty<string>(), new[]
                {
                    Edit(0xA3FA, new byte[] { 0 }),
                    Edit(0xD4F0, Blob($"ringhook{form}.bin")),
                    Edit(0xF83C, Blob($"vhooka{form}.bin")),
                    Edit(0xF842, Blob($"vhookb{form}.bin")),
                    Edit(0xA484, Blob($"pollhook{form}.bin"), supersededBy: "cpu_rest"),
                    Edit(0x7640, new byte[0x90]),
                    Edit(0x76D0, U16((ushort)level)),
                    Edit(RingCode.Start, Blob($"ringtail{form}.bin").Pad(RingCode.End - RingCode.Start, 0xFF)),
                }, Group: "ring", EarlierFormOf: form.Length == 0 ? null : key));
            }
        }

        foreach (var (key, title, level) in OldRingLevels)
        {
            list.Add(new Recipe($"{key}@0.2", title, Array.Empty<string>(), new[]
            {
                Edit(0xA3FA, new byte[] { 0 }),
                Edit(0xD4F0, Blob("ringhook02.bin")),
                Edit(0x7640, Blob("ring02.bin").Pad(0x90)),
                Edit(0x76D0, U16((ushort)level)),
            }, Group: "ring", EarlierFormOf: key));
        }

        list.Add(new Recipe("battery_alerts", "Battery alerts", Array.Empty<string>(), new[]
        {
            Edit(AlertsFlag, new byte[] { 1 }),
        }, RequiresGroup: "ring"));

        list.Add(new Recipe("settings", "Controller settings", Array.Empty<string>(), new[]
        {
            Edit(0xDCE6, Blob("hgamea.bin")),
            Edit(0xEAF4, Blob("hgameb.bin")),
            Edit(0xA0F8, Blob("hbuzza.bin")),
            Edit(0xDAA8, Blob("hbuzzb.bin")),
            Edit(0xD1C8, Blob("hidle.bin")),
            Edit(0x6A90, Blob("hstickl.bin")),
            Edit(0x6ABC, Blob("hstickr.bin")),
            Edit(0xC910, Blob("hscan.bin")),
            Edit(0x7210, Blob("sram.bin").Pad(0xC0)),
            Edit(SettingsFlag, new byte[] { 1 }),
        }, RequiresGroup: "ring"));

        list.Add(new Recipe("fixes", "Fixes", Array.Empty<string>(), new[]
        {
            Edit(0xA480, Blob("hidletask.bin")),
            Edit(0xEA88, Blob("hunplug.bin")),
            Edit(0x178CC, Blob("hota.bin")),
        }, RequiresGroup: "ring"));

        list.Add(new Recipe("stick_precision", "Finer stick movement", Array.Empty<string>(), new[]
        {
            Edit(0xB1F2, new[] { (byte)StickThreshold }),
            Edit(0xB208, new[] { (byte)StickThreshold }),
            Edit(0xB220, new[] { (byte)StickThreshold }),
            Edit(0xB238, new[] { (byte)StickThreshold }),
            Edit(0xA2DC, Blob("hsweep.bin")),
        }, RequiresGroup: "ring"));

        list.Add(new Recipe("cpu_rest", "Processor rest between radio events", Array.Empty<string>(), new[]
        {
            Edit(0xA484, Blob("resthook.bin")),
            Edit(0x6020, Blob("wdog.bin").Pad(0x94)),
            Edit(0x848C, Blob("stall.bin").Pad(0x74)),
            Edit(0xA4AE, Blob("hstall.bin")),
            Edit(RestFlag, new byte[] { 1 }),
        }, RequiresGroup: "ring"));

        list.Add(new Recipe("ble_interval", "Bluetooth connection request of 12.5 ms", Array.Empty<string>(), new[]
        {
            Edit(0xA0DE, Convert.FromHexString("0aa00aa100a2c8a3")),
        }));

        list.Add(new Recipe("fast_scan", "Faster button reading", Array.Empty<string>(), new[]
        {
            Edit(0xD288, U32(40000)),
            Edit(0xC8E2, Convert.FromHexString("13aa")),
            Edit(0xC8E6, Convert.FromHexString("14a2")),
            Edit(0xC640, Convert.FromHexString("0bae")),
        }));

        return (list.ToDictionary(r => r.Key, StringComparer.Ordinal), list.Select(r => r.Key).ToArray());
    }

    private static Edit Edit(int addr, byte[] @new, string? supersededBy = null, byte[][]? accepts = null) =>
        new(addr, @new, supersededBy, accepts ?? Array.Empty<byte[]>());

    private static byte[] Blob(string name) => Blobs.Get(name);

    private static byte[] U16(ushort value) => new[] { (byte)value, (byte)(value >> 8) };

    private static byte[] U32(uint value) =>
        new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) };

    private static byte[] Join(params byte[][] parts)
    {
        var length = parts.Sum(p => p.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Array.Copy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }
        return result;
    }

    /// <summary>The whole app area: a v1.09 payload gets its blank tail.</summary>
    public static byte[] Whole(byte[] image)
    {
        if (image.Length != Image.BankASize)
            return image;
        var whole = new byte[Image.AppSize];
        Array.Copy(image, whole, image.Length);
        for (var i = Image.BankASize; i < whole.Length; i++)
            whole[i] = 0xFF;
        return whole;
    }

    /// <summary>The app area with every edit region zeroed: what no recipe touches.</summary>
    public static byte[] Masked(byte[] image)
    {
        var output = (byte[])image.Clone();
        foreach (var recipe in All.Values)
        {
            foreach (var edit in recipe.Edits)
            {
                var offset = edit.Addr - Image.Base;
                Array.Clear(output, offset, edit.Size);
            }
        }
        return output;
    }

    /// <summary>The app area: stock plus the chosen tweaks. Refuses anything but stock, an unknown tweak or a missing dependency.</summary>
    public static byte[] Build(byte[] stock, IReadOnlyList<string> keys)
    {
        if (stock.Length != Image.BankASize || Sha256Hex(stock) != Image.StockSha256)
            throw new RecipeException("the base image is not stock v1.09 bank A");

        foreach (var key in keys)
        {
            if (!All.TryGetValue(key, out var recipe) || recipe.EarlierFormOf is not null)
                throw new RecipeException($"unknown tweak '{key}'");
            foreach (var need in recipe.Requires)
            {
                if (!keys.Contains(need))
                    throw new RecipeException($"{key} needs {need}");
            }
            if (recipe.RequiresGroup is { } group
                && !keys.Any(other => All[other].Group == group))
                throw new RecipeException($"{key} needs a Home ring level");
        }

        var groups = keys.Where(k => All[k].Group is not null).Select(k => All[k].Group!).ToList();
        if (groups.Count != groups.Distinct(StringComparer.Ordinal).Count())
            throw new RecipeException("choose only one Home ring level");

        var baseImage = Whole(stock);
        var image = (byte[])baseImage.Clone();
        foreach (var key in Order)   // recipe order: a dependent's edit lands over the one it supersedes
        {
            var recipe = All[key];
            if (!keys.Contains(key))
                continue;
            foreach (var edit in recipe.Edits)
            {
                if (Sha256Hex(Region(baseImage, edit)) != StockHashes.Region[(edit.Addr, edit.Size)])
                    throw new RecipeException($"{key}: stock bytes at 0x{edit.Addr:x} are not as expected");
                Array.Copy(edit.New, 0, image, edit.Addr - Image.Base, edit.Size);
            }
        }
        return image;
    }

    /// <summary>Which tweaks an app area holds, or unknown if any edit region is neither stock nor a tweak's, or the rest differs.</summary>
    public static FirmwareState Identify(byte[] bankA)
    {
        bankA = Whole(bankA);
        if (bankA.Length != Image.AppSize || Sha256Hex(Masked(bankA)) != StockHashes.Remainder)
            return new FirmwareState(false);

        string EditState(Edit edit)
        {
            var got = Region(bankA, edit);
            if (got.SequenceEqual(edit.New) || edit.Accepts.Any(a => got.SequenceEqual(a)))
                return "new";
            if (Sha256Hex(got) == StockHashes.Region[(edit.Addr, edit.Size)])
                return "stock";

            // the region a tweak takes over from another's reads as untouched while the other is present
            foreach (var recipe in All.Values)
            {
                foreach (var other in recipe.Edits)
                {
                    if (other.Addr != edit.Addr || other.SupersededBy is null)
                        continue;
                    if (!All[other.SupersededBy].Edits.Any(candidate => SameEdit(candidate, edit)))
                        continue;
                    if (got.SequenceEqual(other.New.Pad(edit.Size)))
                        return "stock";
                }
            }
            return "other";
        }

        var plain = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var key in Order)
            plain[key] = All[key].Edits.Where(e => e.SupersededBy is null).Select(EditState).ToList();

        var present = new HashSet<string>(
            plain.Where(pair => pair.Value.Count > 0 && pair.Value.All(x => x == "new")).Select(pair => pair.Key),
            StringComparer.Ordinal);

        var chosen = new HashSet<string>(
            present.Select(k => All[k].Group).Where(g => g is not null)!,
            StringComparer.Ordinal);

        foreach (var key in Order)
        {
            var recipe = All[key];
            if (recipe.Group is { } group && chosen.Contains(group) && !present.Contains(key))
                continue;   // the group's other options share regions with the installed one

            var states = new List<string>(plain[key]);
            states.AddRange(recipe.Edits
                .Where(e => e.SupersededBy is not null && !present.Contains(e.SupersededBy))
                .Select(EditState));
            if (states.Contains("other") || (states.Contains("new") && states.Contains("stock")))
                return new FirmwareState(false);
        }

        var previous = present;   // the second pass tests against this one, as the original does
        var final = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in Order)
        {
            var recipe = All[key];
            if (recipe.Edits
                .Where(e => e.SupersededBy is null || !previous.Contains(e.SupersededBy))
                .All(e => EditState(e) == "new"))
                final.Add(key);
        }

        foreach (var key in final)
        {
            if (All[key].Requires.Any(need => !final.Contains(need)))
                return new FirmwareState(false);
            if (All[key].RequiresGroup is { } group
                && !final.Any(other => All[other].Group == group))
                return new FirmwareState(false);
        }

        var names = final.Select(k => All[k].EarlierFormOf ?? k)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var outdated = final.Any(k => All[k].EarlierFormOf is not null);
        return new FirmwareState(true, names, outdated);
    }

    // Python compares the edit dataclasses by value; the arrays have to be compared by content here
    private static bool SameEdit(Edit a, Edit b) =>
        a.Addr == b.Addr
        && a.SupersededBy == b.SupersededBy
        && a.New.SequenceEqual(b.New)
        && a.Accepts.Count == b.Accepts.Count
        && a.Accepts.Zip(b.Accepts).All(pair => pair.First.SequenceEqual(pair.Second));

    private static byte[] Region(byte[] image, Edit edit)
    {
        var region = new byte[edit.Size];
        Array.Copy(image, edit.Addr - Image.Base, region, 0, edit.Size);
        return region;
    }

    internal static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}

public sealed class RecipeException : Exception
{
    public RecipeException(string message) : base(message)
    {
    }
}

public sealed record Edit(int Addr, byte[] New, string? SupersededBy, IReadOnlyList<byte[]> Accepts)
{
    public int Size => New.Length;
}

public sealed record Recipe(
    string Key,
    string Title,
    IReadOnlyList<string> Requires,
    IReadOnlyList<Edit> Edits,
    string? Group = null,
    string? RequiresGroup = null,
    string? EarlierFormOf = null);

public sealed record FirmwareState(bool Known, IReadOnlyList<string> Recipes, bool Outdated = false)
{
    public FirmwareState(bool known) : this(known, Array.Empty<string>())
    {
    }

    public static readonly FirmwareState Unknown = new(false);
}

internal static class ByteArrayExtensions
{
    // pads with a byte, or trims, to the requested length
    public static byte[] Pad(this byte[] data, int length, byte pad = 0)
    {
        if (data.Length == length)
            return data;
        var output = new byte[length];
        Array.Copy(data, output, Math.Min(data.Length, length));
        for (var i = data.Length; i < length; i++)
            output[i] = pad;
        return output;
    }
}
