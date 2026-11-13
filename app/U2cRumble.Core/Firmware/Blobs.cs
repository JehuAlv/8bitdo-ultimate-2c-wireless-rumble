using System.Reflection;

namespace U2cRumble.Core.Firmware;

/// <summary>The small firmware fragments the tweaks install, embedded in this assembly.</summary>
internal static class Blobs
{
    private static readonly Assembly Assembly = typeof(Blobs).Assembly;
    private static readonly Dictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    // loads a blob by file name, however the build named the resource
    public static byte[] Get(string name)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached))
                return cached;

            var resource = Assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("." + name, StringComparison.Ordinal)
                                     || n.EndsWith("/" + name, StringComparison.Ordinal));
            if (resource is null)
                throw new RecipeException($"The embedded firmware fragment {name} is missing.");

            using var stream = Assembly.GetManifestResourceStream(resource)
                ?? throw new RecipeException($"The embedded firmware fragment {name} couldn't be read.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var data = memory.ToArray();
            Cache[name] = data;
            return data;
        }
    }
}
