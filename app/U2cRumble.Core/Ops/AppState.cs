namespace U2cRumble.Core.Ops;

/// <summary>Per-user state: where backups live, whether the disclaimer has been accepted, and the device lock.</summary>
public static class AppState
{
    public static string DefaultStore() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "u2c-customfw");

    public static bool Accepted(string store) => File.Exists(Path.Combine(store, "disclaimer-accepted"));

    public static void Accept(string store)
    {
        Directory.CreateDirectory(store);
        File.WriteAllText(Path.Combine(store, "disclaimer-accepted"), "accepted\n");
    }

    /// <summary>
    /// Taken while a front end uses the controller, so two of them can't interleave their requests. The open file,
    /// or null if another process holds it.
    /// </summary>
    public static FileStream? LockDevice(string store)
    {
        Directory.CreateDirectory(store);
        try
        {
            return new FileStream(Path.Combine(store, "device.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static void UnlockDevice(FileStream? lockFile) => lockFile?.Dispose();
}
