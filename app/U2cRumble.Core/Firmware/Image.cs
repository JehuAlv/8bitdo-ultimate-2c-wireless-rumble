namespace U2cRumble.Core.Firmware;

/// <summary>Layout of the controller's flash (firmware 1.09) and the hash that identifies stock.</summary>
public static class Image
{
    public const int FlashSize = 0x80000;
    public const int BootEnd = 0x6000;              // bootloader 0x0-0x6000: never written
    public const int Base = 0x6000;                 // bank A, the running app
    public const int BankASize = 0x12400;           // the v1.09 payload
    public const int BankAEnd = Base + BankASize;
    public const int BankAEraseEnd = 0x19000;       // bank A rounded up to 4 KB sectors
    public const int AppEnd = BankAEraseEnd;        // the app area, with room for code past the payload
    public const int AppSize = AppEnd - Base;
    public const int BankB = 0x40000;               // second copy of the app, where updates are staged
    public const int ConfigStart = 0x73000;         // config, MAC, calibration, Bluetooth bonds: never written
    public const int MacSector = 0x76000;
    public const int Sector = 0x1000;
    public const int UpdateRecord = 0x3F000;        // 52-byte record; byte +0x0c is the version times 100
    public const int Settings = 0x73000;            // the firmware's settings record
    public const int SettingsSize = 26;
    public const int RingSaved = Settings + 25;      // the Home ring's saved level (the firmware writes it)
    public const double SupportedVersion = 1.09;
    public const int NewAddresses = 0x1000;         // how far pairing again can plausibly move an address

    public const string StockSha256 = "dfd443c77f831008aa453ee2c1c9cb6871c36f8947c548e61d4387fba0a9da70";
    public const string StockDatSha256 = "87349e603bb92d6ae9ff6defb6f32b10de2f2d3252fe4e442fa7993f982f2a5a";

    /// <summary>Whether two copies of the address sector come from one controller.</summary>
    public static bool SameUnit(byte[] a, byte[] b)
    {
        var near = Math.Abs(IntFromBytes(a, 0, 3) - IntFromBytes(b, 0, 3)) <= NewAddresses;
        return near && ByteRangesEqual(a, 3, b, 3, 3) && ByteRangesEqual(a, 7, b, 7, a.Length - 7);
    }

    private static int IntFromBytes(byte[] data, int offset, int count)
    {
        var value = 0;
        for (var i = 0; i < count; i++)
            value |= data[offset + i] << (8 * i);
        return value;
    }

    private static bool ByteRangesEqual(byte[] a, int aOffset, byte[] b, int bOffset, int count)
    {
        if (aOffset + count > a.Length || bOffset + count > b.Length)
            return false;
        for (var i = 0; i < count; i++)
        {
            if (a[aOffset + i] != b[bOffset + i])
                return false;
        }
        return true;
    }
}
