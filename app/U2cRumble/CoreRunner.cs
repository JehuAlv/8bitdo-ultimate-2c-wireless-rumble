using System.Diagnostics;
using System.IO;
using System.Text;

namespace U2cRumble;

/// <summary>Runs the Python flashing core that lives in the repository's core/ folder.</summary>
public sealed class CoreRunner
{
    public string CoreDir { get; }

    private readonly string _exe;

    private CoreRunner(string coreDir, string exe)
    {
        CoreDir = coreDir;
        _exe = exe;
    }

    /// <summary>Walks up from the app folder to find core/, and picks a bundled exe or the python on PATH.</summary>
    public static CoreRunner? Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var core = Path.Combine(dir.FullName, "core");
            if (File.Exists(Path.Combine(core, "u2c_customfw", "__init__.py")))
            {
                var bundled = Path.Combine(core, "u2c-customfw.exe");
                return new CoreRunner(core, File.Exists(bundled) ? bundled : FindPython());
            }
            dir = dir.Parent;
        }
        return null;
    }

    // U2C_PYTHON overrides the interpreter, e.g. a virtualenv
    private static string FindPython()
    {
        var env = Environment.GetEnvironmentVariable("U2C_PYTHON");
        return string.IsNullOrWhiteSpace(env) ? "python" : env;
    }

    private bool UsesBundledExe =>
        Path.GetFileName(_exe).Equals("u2c-customfw.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs one core command and reports each output line as it arrives. Returns the exit code.</summary>
    public async Task<int> RunAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _exe,
            WorkingDirectory = CoreDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (!UsesBundledExe)
        {
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add("u2c_customfw");
            psi.Environment["PYTHONPATH"] = CoreDir;
        }
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        psi.Environment["PYTHONUTF8"] = "1";   // the core prints utf-8; a cp1252 console would choke on it
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(ct);
        proc.WaitForExit();   // let the async output readers flush before the caller reads the exit code
        return proc.ExitCode;
    }
}
