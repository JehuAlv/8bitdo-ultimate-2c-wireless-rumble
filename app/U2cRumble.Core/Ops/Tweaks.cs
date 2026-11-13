using U2cRumble.Core.Firmware;

namespace U2cRumble.Core.Ops;

/// <summary>Recipes kept in the code but never offered or shown by the app.</summary>
public static class Tweaks
{
    public static readonly IReadOnlySet<string> Hidden = new HashSet<string> { "ble_interval" };

    public static IReadOnlyList<string> Shown(IEnumerable<string> keys) =>
        keys.Where(k => !Hidden.Contains(k)).ToList();
}
