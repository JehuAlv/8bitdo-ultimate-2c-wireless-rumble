using System.Security.Cryptography;
using U2cRumble.Core.Firmware;

// checks the C# firmware builder against the image the original Python core produced for the same controller
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: ParityCheck <backup.bin> [expected.bin] [bt_xbox,bt_rumble,ble_interval]");
    return 2;
}

// sha256 of the app area the validated Python core built from a stock 1.09 backup
const string ExpectedSha256 = "aa63a987cdfb377c2ed8b6835aca73ed6265e6ff9ca689f7331b0d352c68ea45";
const int ExpectedSize = 77824;

var backup = File.ReadAllBytes(args[0]);
if (backup.Length != Image.FlashSize)
{
    Console.Error.WriteLine($"not a full backup: {backup.Length} bytes");
    return 2;
}

var stock = backup[Image.Base..(Image.Base + Image.BankASize)];
var keys = (args.Length > 2 ? args[2] : "bt_xbox,bt_rumble,ble_interval")
    .Split(',', StringSplitOptions.RemoveEmptyEntries);

byte[] built;
try
{
    built = Recipes.Build(stock, keys);
}
catch (RecipeException e)
{
    Console.Error.WriteLine($"the builder refused the backup: {e.Message}");
    return 1;
}

var ok = true;
void Check(bool condition, string message)
{
    Console.WriteLine((condition ? "PASS " : "FAIL ") + message);
    ok &= condition;
}

Check(stock.Length == Image.BankASize, $"stock bank A is {stock.Length} bytes");
Check(Convert.ToHexString(SHA256.HashData(stock)).ToLowerInvariant() == Image.StockSha256,
    $"stock bank A matches {Image.StockSha256}");

var builtSha = Convert.ToHexString(SHA256.HashData(built)).ToLowerInvariant();
Check(built.Length == ExpectedSize, $"built image is {built.Length} bytes (expected {ExpectedSize})");
Check(builtSha == ExpectedSha256, $"built sha256 {builtSha}");

// an expected image from the Python core can be compared byte for byte
if (args.Length > 1)
{
    var expected = File.ReadAllBytes(args[1]);
    var expectedSha = Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant();
    Check(built.Length == expected.Length, $"expected image is {expected.Length} bytes");
    Check(builtSha == expectedSha, $"expected sha256 {expectedSha}");
    if (built.Length == expected.Length)
    {
        var first = Enumerable.Range(0, built.Length).FirstOrDefault(i => built[i] != expected[i], -1);
        Check(first < 0, first < 0 ? "built image is byte-for-byte identical" : $"first difference at 0x{first:x}");
    }
}

var builtState = Recipes.Identify(built);
Check(builtState.Known, "built image is recognised");
Check(string.Join(",", builtState.Recipes) == "ble_interval,bt_rumble,bt_xbox",
    $"built image has {string.Join(", ", builtState.Recipes)}");

var stockState = Recipes.Identify(stock);
Check(stockState.Known && stockState.Recipes.Count == 0, "stock bank A is recognised as original 1.09");

Console.WriteLine(ok ? "PARITY OK" : "PARITY FAILED");
return ok ? 0 : 1;
