using System.Diagnostics;
using System.Runtime.InteropServices;

if (args.Length < 1)
{
    Console.WriteLine("Usage: xbox-driver-convertor <command> <arg?> <path-to-disc-if-needed>");
    Console.WriteLine("Supported commands: 'list', 'read', 'set', 'toggle'");
    Console.WriteLine("Command examples:");
    Console.WriteLine("         list - print list of xbox discs with mode");
    Console.WriteLine("         read /dev/mydiscpath - read the mode of `/dev/mydiscpath` disc");
    Console.WriteLine("         set pc /dev/mydiscpath - set the mode to `pc` for `/dev/mydiscpath` disc");
    Console.WriteLine("         set xbox /dev/mydiscpath - set the mode to `xbox` for `/dev/mydiscpath` disc");
    Console.WriteLine("         toggle - toggle mode from for first mounted xbox disc");
    Console.WriteLine("Don't forget run app from administrator user. Example for Linux and MacOS: sudo ./Xbox.DriverConvertor list");
    Console.WriteLine("");
    Console.WriteLine("How use? Mount xbox disc and execute command: sudo ./Xbox.DriverConvertor toggle");
    Console.WriteLine("");
    return;
}

var command = args[0].ToLower();
switch (command)
{
    case "list":
        PrintXboxDiscs();
        break;
    case "read":
        if (args.Length != 2)
        {
            Console.WriteLine("Please provide the disc path for reading");
            return;
        }

        ReadXboxDiscMode(args[1]);
        break;
    case "set":
        if (args.Length != 3)
        {
            Console.WriteLine("Please provide mode (`xbox` or `pc`) and then the disc path");
            return;
        }

        SetXboxDiscMode(args[2], args[1]);
        break;
    case "toggle":
        ToggleXboxDisc();
        break;
    default:
        Console.WriteLine($"Invalid command {args[0]}. Use 'list', 'read', 'set' or 'toggle'");
        break;
}

static void PrintXboxDiscs()
{
    foreach (var xboxDisc in ReadXboxDiscs())
    {
        Console.WriteLine($"{xboxDisc.Path} - {xboxDisc.Mode}");
    }
}


static string? ReadXboxDiscMode(string path)
{
    try
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        fs.Seek(510, SeekOrigin.Begin);
        var bytes = new byte[2];
        fs.Read(bytes, 0, 2);

        return bytes[0] switch
        {
            153 when bytes[1] == 204 => "xbox",
            85 when bytes[1] == 170 => "pc",
            _ => null
        };
    }
    catch (Exception ex)
    {
        if (!ex.Message.StartsWith("Access"))
        {
            Console.WriteLine(ex.Message);
        }
    }

    return null;
}


static void SetXboxDiscMode(string path, string mode)
{
    try
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        fs.Seek(510, SeekOrigin.Begin);
        var bytes = new byte[2];

        switch (mode)
        {
            case "xbox":
                bytes[0] = 153;
                bytes[1] = 204;
                break;
            case "pc":
                bytes[0] = 85;
                bytes[1] = 170;
                break;
            default:
                throw new ArgumentException($"Invalid mode specified: {mode}");
        }

        fs.Write(bytes, 0, 2);
        Console.WriteLine($"Successfully set to {mode} mode");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred: {ex.Message}");
    }
}

static void ToggleXboxDisc()
{
    var xboxDisc = ReadXboxDiscs().FirstOrDefault();
    if (xboxDisc == null) return;

    var newMode = xboxDisc.Mode == "xbox" ? "pc" : "xbox";
    SetXboxDiscMode(xboxDisc.Path, newMode);
    Console.WriteLine($"Disc {xboxDisc.Path} switched from {xboxDisc.Mode} to {newMode}");
}


static string RunCommand(string arguments)
{
    var process = new Process();
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        process.StartInfo.FileName = "cmd.exe";
        process.StartInfo.Arguments = "/c " + arguments;
    }
    else // MacOS and Linux
    {
        process.StartInfo.FileName = "bash";
        process.StartInfo.Arguments = "-c \"" + arguments + "\"";
    }

    process.StartInfo.RedirectStandardOutput = true;
    process.StartInfo.UseShellExecute = false;
    process.StartInfo.CreateNoWindow = true;
    process.Start();

    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();

    return output;
}

static IEnumerable<XboxDisc> ReadXboxDiscs()
{
    var hasXboxDisc = false;
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        var output = RunCommand("wmic discdrive list brief");
        foreach (var xboxDevice in ProcessWindowsReadXboxDiscs(output))
        {
            hasXboxDisc = true;
            yield return xboxDevice;
        }
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        var output = RunCommand("lsblk -d");
        foreach (var xboxDevice in ProcessLinuxReadXboxDiscs(output))
        {
            hasXboxDisc = true;
            yield return xboxDevice;
        }
    }
    else // MacOS
    {
        var output = RunCommand("discutil list");
        foreach (var xboxDevice in ProcessMacReadXboxDiscs(output))
        {
            hasXboxDisc = true;
            yield return xboxDevice;
        }
    }

    if (!hasXboxDisc)
    {
        Console.WriteLine($"Can't find xbox disc. Mount xbox disc and run the command as administrator");
    }
}


static IEnumerable<XboxDisc> ProcessMacReadXboxDiscs(string output)
{
    var lines = output.Split('\n');
    foreach (var line in lines)
    {
        if (!line.Contains("/dev/")) continue;

        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var path = parts[0];
        var mode = ReadXboxDiscMode(path);
        if (mode == null) continue;

        yield return new XboxDisc(path, mode);
    }
}

static IEnumerable<XboxDisc> ProcessWindowsReadXboxDiscs(string output)
{
    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    foreach (var line in lines.Skip(1)) // Skip headers
    {
        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var path = $"\\\\.\\PHYSICALDRIVE{parts[0]}"; // May be needed change index
        var mode = ReadXboxDiscMode(path);
        if (mode == null) continue;

        yield return new XboxDisc(path, mode);
    }
}

static IEnumerable<XboxDisc> ProcessLinuxReadXboxDiscs(string output)
{
    var lines = output.Split('\n');
    foreach (var line in lines.Skip(1)) // Skip headers
    {
        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var path = $"/dev/{parts[0]}"; // May be needed change index
        var mode = ReadXboxDiscMode(path);
        if (mode == null) continue;

        yield return new XboxDisc(path, mode);
    }
}

record XboxDisc(string Path, string Mode);