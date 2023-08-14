using System.Diagnostics;
using System.Runtime.InteropServices;

if (args.Length == 0)
{
  Console.WriteLine("Usage: xbox-driver-convertor <command> <arg?> <path-to-disk-if-needed>");
  Console.WriteLine("Supported commands: 'list', 'read', 'set', 'toggle'");
  Console.WriteLine("Command examples:");
  Console.WriteLine("    list - print list of xbox disks with mode");
  Console.WriteLine("    read /dev/mydiskpath - read the mode of `/dev/mydiskpath` disk");
  Console.WriteLine("    set pc /dev/mydiskpath - set the mode to `pc` for `/dev/mydiskpath` disk");
  Console.WriteLine("    set xbox /dev/mydiskpath - set the mode to `xbox` for `/dev/mydiskpath` disk");
  Console.WriteLine("    toggle - toggle mode from for first mounted xbox disk");
  Console.WriteLine(
      "Don't forget run app from administrator user. Example for Linux and MacOS: sudo ./Xbox.DriverConvertor list\n");
  Console.WriteLine("How use? Mount xbox disk and execute command: sudo ./Xbox.DriverConvertor toggle\n");
  return;
}

var command = args[0].ToLower();
switch (command)
{
  case "list":
    PrintXboxDisks();
    break;
  case "read":
    if (args.Length != 2)
    {
      Console.WriteLine("Please provide the disk path for reading");
      return;
    }

    var mode = ReadXboxDiskMode(args[1]);
    Console.WriteLine($"Disk {args[1]} has mode '{mode ?? "unknown"}'");
    break;
  case "set":
    if (args.Length != 3)
    {
      Console.WriteLine("Please provide mode (`xbox` or `pc`) and then the disk path");
      return;
    }

    if (SetXboxDiskMode(args[2], args[1]))
    {
      Console.WriteLine($"Disk {args[2]} switched to {args[1]}");
    }
    break;
  case "toggle":
    ToggleXboxDisk();
    break;
  default:
    Console.WriteLine($"Invalid command '{args[0]}'. Use 'list', 'read', 'set' or 'toggle'");
    break;
}

static void PrintXboxDisks()
{
  foreach (var xboxDisk in ReadXboxDisks())
  {
    Console.WriteLine($"{xboxDisk.Path} - {xboxDisk.Mode}");
  }
}

static string? ReadXboxDiskMode(string path)
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

static bool SetXboxDiskMode(string path, string mode)
{
  try
  {
    using var fs = new FileStream(path, FileMode.Open, FileAccess.Write);
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
    return true;
  }
  catch (Exception ex)
  {
    Console.WriteLine(ex.Message);
    return false;
  }
}

static void ToggleXboxDisk()
{
  var xboxDisk = ReadXboxDisks().FirstOrDefault();
  if (xboxDisk == null) return;

  var newMode = xboxDisk.Mode == "xbox" ? "pc" : "xbox";
  if (SetXboxDiskMode(xboxDisk.Path, newMode))
  {
    Console.WriteLine($"Disk {xboxDisk.Path} switched from {xboxDisk.Mode} to {newMode}");
  }
}

static string RunCommand(string arguments)
{
  var process = new Process();
  if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
  {
    process.StartInfo.FileName = "cmd.exe";
    process.StartInfo.Arguments = "/c " + arguments;
  }
  else // Another systems
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

static IEnumerable<XboxDisk> ReadXboxDisks()
{
  var hasXboxDisk = false;
  if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
  {
    var output = RunCommand("wmic diskdrive list brief");
    foreach (var xboxDisk in ProcessWindowsReadXboxDisks(output))
    {
      hasXboxDisk = true;
      yield return xboxDisk;
    }
  }
  else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
  {
    var output = RunCommand("diskutil list");
    foreach (var xboxDisk in ProcessMacReadXboxDisks(output))
    {
      hasXboxDisk = true;
      yield return xboxDisk;
    }
  }
  else // Another systems
  {
    var output = RunCommand("lsblk -d");
    foreach (var xboxDisk in ProcessLinuxReadXboxDisks(output))
    {
      hasXboxDisk = true;
      yield return xboxDisk;
    }
  }

  if (!hasXboxDisk)
  {
    Console.WriteLine($"Can't find xbox disk. Mount xbox disk and run the command as administrator");
  }
}

static IEnumerable<XboxDisk> ProcessMacReadXboxDisks(string output)
{
  var lines = output.Split('\n');
  foreach (var line in lines)
  {
    if (!line.Contains("/dev/")) continue;

    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    var path = parts[0];
    var mode = ReadXboxDiskMode(path);
    if (mode == null) continue;

    yield return new XboxDisk(path, mode);
  }
}

static IEnumerable<XboxDisk> ProcessWindowsReadXboxDisks(string output)
{
  var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
  foreach (var line in lines.Skip(1)) // Skip headers
  {
    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    var path = $"\\\\.\\PHYSICALDRIVE{parts[0]}"; // May be needed change index
    var mode = ReadXboxDiskMode(path);
    if (mode == null) continue;

    yield return new XboxDisk(path, mode);
  }
}

static IEnumerable<XboxDisk> ProcessLinuxReadXboxDisks(string output)
{
  var lines = output.Split('\n');
  foreach (var line in lines.Skip(1)) // Skip headers
  {
    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    var path = $"/dev/{parts[0]}"; // May be needed change index
    var mode = ReadXboxDiskMode(path);
    if (mode == null) continue;

    yield return new XboxDisk(path, mode);
  }
}

record XboxDisk(string Path, string Mode);
