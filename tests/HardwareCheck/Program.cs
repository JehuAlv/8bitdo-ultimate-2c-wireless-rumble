using U2cRumble.Core;

// read-only runs of the C# flashing core against a connected controller
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: HardwareCheck <devices|backup|check|restore-check>");
    return 2;
}

var service = new FlashService();
void Log(string line) => Console.WriteLine(line);
void Progress(string label, int done, int total)
{
    var percent = Math.Min(100, done * 100 / Math.Max(total, 1));
    Console.Write($"\r{label} {percent,3} %");
}

try
{
    switch (args[0])
    {
        case "devices":
            foreach (var line in service.DescribeDevices())
                Console.WriteLine(line);
            break;

        case "backup":
            service.Backup(Log, Progress);
            break;

        case "check":
            service.Install(new[] { "bt_xbox", "bt_rumble" }, write: false, disclaimerAccepted: false,
                Log, Progress);
            break;

        case "restore-check":
            service.Restore("backup", write: false, forceUnknown: false, disclaimerAccepted: false,
                Log, Progress);
            break;

        default:
            Console.Error.WriteLine($"unknown command {args[0]}");
            return 2;
    }
}
catch (Exception e)
{
    Console.WriteLine();
    Console.Error.WriteLine(e.Message);
    return 1;
}

Console.WriteLine();
return 0;
