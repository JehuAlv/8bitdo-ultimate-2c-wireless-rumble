using System.Diagnostics;
using System.Security.Cryptography;

namespace SignTool;

/// <summary>Signs and verifies the published executable with signtool, then prints its SHA-256.</summary>
internal static class Program
{
    private const string DefaultTimestamp = "http://timestamp.digicert.com";

    private static int Main(string[] args)
    {
        string? file = null, pfx = null, password = null, thumbprint = null;
        var timestamp = DefaultTimestamp;
        var verifyOnly = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    Usage();
                    return 0;
                case "--file":
                    file = Next(args, ref i);
                    break;
                case "--pfx":
                    pfx = Next(args, ref i);
                    break;
                case "--password":
                    password = Next(args, ref i);
                    break;
                case "--thumbprint":
                    thumbprint = Next(args, ref i);
                    break;
                case "--timestamp":
                    timestamp = Next(args, ref i) ?? DefaultTimestamp;
                    break;
                case "--verify-only":
                    verifyOnly = true;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    Usage();
                    return 1;
            }
        }

        file ??= NewestArtifact();
        if (file is null || !File.Exists(file))
        {
            Console.Error.WriteLine("No executable found. Build it first or pass --file.");
            return 1;
        }
        file = Path.GetFullPath(file);

        var signTool = FindSignTool();
        if (signTool is null)
        {
            Console.Error.WriteLine("signtool.exe was not found. Install the Signing Tools component of the Windows SDK.");
            return 1;
        }

        if (!verifyOnly)
        {
            if (thumbprint is null && pfx is null)
            {
                Console.Error.WriteLine("Pass --thumbprint, --pfx, or --verify-only.");
                return 1;
            }

            var sign = new List<string> { "sign", "/fd", "SHA256", "/tr", timestamp, "/td", "SHA256" };
            if (thumbprint is not null)
            {
                sign.AddRange(["/sha1", thumbprint]);
            }
            else
            {
                sign.AddRange(["/f", pfx!]);
                if (password is not null)
                    sign.AddRange(["/p", password]);
            }
            sign.Add(file);

            if (!Run(signTool, sign))
                return 1;
        }

        if (!Run(signTool, ["verify", "/pa", "/v", file]))
            return 1;

        using var stream = File.OpenRead(file);
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        Console.WriteLine($"SHA256 {hash}  {Path.GetFileName(file)}");
        Console.WriteLine("Update SHA256SUMS.txt in the release with this value.");
        return 0;
    }

    // reads the value that follows a flag
    private static string? Next(string[] args, ref int i) => i + 1 < args.Length ? args[++i] : null;

    // prints the options
    private static void Usage()
    {
        Console.WriteLine("""
            Signs and verifies the published executable with signtool.

            Usage:
              dotnet run --project scripts/SignTool -- [options]

            Options:
              --file <exe>          file to sign (default: newest artifacts/U2cRumble-*-win-x64.exe)
              --pfx <path>          .pfx certificate file
              --password <value>    password for the .pfx file
              --thumbprint <sha1>   thumbprint of a certificate in the Windows store
              --timestamp <url>     RFC 3161 timestamp server (default: http://timestamp.digicert.com)
              --verify-only         only verify the signature
              -h, --help            show this help
            """);
    }

    // picks the newest published executable when none is given
    private static string? NewestArtifact()
    {
        var dir = new DirectoryInfo("artifacts");
        if (!dir.Exists)
            return null;

        return dir.GetFiles("U2cRumble-*-win-x64.exe")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    // finds signtool in the Windows SDK or on PATH
    private static string? FindSignTool()
    {
        var kits = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Windows Kits", "10", "bin");
        if (Directory.Exists(kits))
        {
            var found = Directory.GetDirectories(kits)
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .Select(d => Path.Combine(d, "x64", "signtool.exe"))
                .FirstOrDefault(File.Exists);
            if (found is not null)
                return found;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(part.Trim('"'), "signtool.exe");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    // runs signtool and lets it write to the console
    private static bool Run(string tool, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(tool) { UseShellExecute = false };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info);
        if (process is null)
        {
            Console.Error.WriteLine($"Could not start {tool}.");
            return false;
        }

        process.WaitForExit();
        if (process.ExitCode == 0)
            return true;

        Console.Error.WriteLine($"signtool failed with code {process.ExitCode}.");
        return false;
    }
}
