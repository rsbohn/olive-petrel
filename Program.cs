using OlivePetrel;
using System.Linq;
using System.Globalization;
using System.IO;

const int TtiDevice = 3;
const int TtoDevice = 4;
const int Tc08ControlDevice = 8;
const int Tc08DataDevice = 9;
const string HelpDirectory = "docs/help";

var machine = new Pdp8();
var tc08 = new Tc08();
var linePrinter = new LinePrinter();
var rx8e = new Rx8e();
var helpSystem = new HelpSystem(HelpDirectory);
machine.LinePrinter = linePrinter;
machine.Rx8e = rx8e;
machine.Tc08 = tc08;
Console.WriteLine("Olive Petrel PDP-8 Emulator");
Console.WriteLine("Type 'help' for commands.");

while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (line is null)
    {
        break;
    }

    var commandArgs = SplitCommand(line);
    if (commandArgs.Count == 0)
    {
        continue;
    }

    var command = commandArgs[0].ToLowerInvariant();
    switch (command)
    {
        case "help":
        case ".help":
        case "h":
            helpSystem.StartHelpShell();
            break;
        case ".a":
        case "a":
            AssembleFile(commandArgs);
            break;
        case "tnfs":
            StartTnfsShell();
            break;
        case "quit":
        case "exit":
        case "q":
            return;
        case "reset":
            machine.Reset();
            Console.WriteLine("Reset complete.");
            break;
        case "regs":
            PrintRegisters(machine);
            break;
        case "mem":
            DumpMemory(machine, commandArgs);
            break;
        case "dep":
            DepositMemory(machine, commandArgs);
            break;
        case "load":
            LoadImage(machine, commandArgs);
            break;
        case "save":
            SaveImage(machine, commandArgs);
            break;
        case "pc":
            SetProgramCounter(machine, commandArgs);
            break;
        case "show":
            ShowDevice(tc08, linePrinter, rx8e, commandArgs);
            break;
        case "step":
        case "s":
            StepMachine(machine, commandArgs);
            break;
        case "run":
        case "r":
            RunMachine(machine, commandArgs);
            break;
        case "trace":
            TraceMachine(machine, commandArgs);
            break;
        default:
            if (TryHandleDeviceCommand(machine, tc08, linePrinter, rx8e, commandArgs))
            {
                break;
            }

            Console.WriteLine("Unknown command. Type 'help' for available commands.");
            break;
    }
}

static List<string> SplitCommand(string line)
{
    var parts = new List<string>();
    var current = new List<char>();
    var inQuotes = false;

    foreach (var ch in line)
    {
        if (ch == '"')
        {
            inQuotes = !inQuotes;
            continue;
        }

        if (!inQuotes && char.IsWhiteSpace(ch))
        {
            if (current.Count > 0)
            {
                parts.Add(new string(current.ToArray()));
                current.Clear();
            }

            continue;
        }

        current.Add(ch);
    }

    if (current.Count > 0)
    {
        parts.Add(new string(current.ToArray()));
    }

    return parts;
}

static void PrintRegisters(Pdp8 machine)
{
    Console.WriteLine(
        $"PC={Pdp8.ToOctal(machine.PC, 4)} AC={Pdp8.ToOctal(machine.AC, 4)} " +
        $"MQ={Pdp8.ToOctal(machine.MQ, 4)} L={(machine.Link ? 1 : 0)} IR={Pdp8.ToOctal(machine.IR, 4)} " +
        $"HLT={(machine.Halted ? 1 : 0)}");
}

static void DumpMemory(Pdp8 machine, List<string> args)
{
    if (args.Count < 2)
    {
        Console.WriteLine("Usage: mem <addr> [count]");
        return;
    }

    if (!TryParseOctal(args[1], out var start))
    {
        Console.WriteLine("Invalid address.");
        return;
    }

    var count = 16;
    if (args.Count > 2 && !TryParseOctal(args[2], out count))
    {
        Console.WriteLine("Invalid count.");
        return;
    }

    for (var i = 0; i < count; i += 8)
    {
        var addr = (start + i) & 0xFFF;
        Console.Write($"{Pdp8.ToOctal(addr, 4)}: ");
        for (var j = 0; j < 8 && i + j < count; j++)
        {
            var word = machine.Read((addr + j) & 0xFFF);
            Console.Write($"{Pdp8.ToOctal(word, 4)} ");
        }

        Console.WriteLine();
    }
}

static void DepositMemory(Pdp8 machine, List<string> args)
{
    if (args.Count < 3)
    {
        Console.WriteLine("Usage: dep <addr> <word|string> [word|string...]");
        return;
    }

    if (!TryParseOctal(args[1], out var address))
    {
        Console.WriteLine("Invalid address.");
        return;
    }

    var current = address & 0xFFF;
    var deposited = 0;
    for (var i = 2; i < args.Count; i++)
    {
        if (TryParseOctal(args[i], out var value))
        {
            machine.Write(current, (ushort)value);
            current = (current + 1) & 0xFFF;
            deposited++;
            continue;
        }
        var expanded = ExpandEscapes(args[i]);
        foreach (var ch in expanded)
        {
            machine.Write(current, (ushort)(ch & 0x7F));
            current = (current + 1) & 0xFFF;
            deposited++;
        }
    }

    Console.WriteLine($"Deposited {deposited} word(s) starting at {Pdp8.ToOctal(address, 4)}.");
}

static bool TryHandleDeviceCommand(Pdp8 machine, Tc08 tc08, LinePrinter linePrinter, Rx8e rx8e, List<string> args)
{
    var device = args[0].ToLowerInvariant();
    switch (device)
    {
        case "dt0":
        case "dt1":
            HandleTc08Device(machine, tc08, args);
            return true;
        case "rx0":
        case "rx1":
            HandleRx8eDevice(machine, rx8e, args);
            return true;
        case "lpt":
            HandleLinePrinterCommand(linePrinter, args);
            return true;
        case "watchdog":
            HandleWatchdogCommand(args);
            return true;
        default:
            return false;
    }
}

static void HandleTc08Device(Pdp8 machine, Tc08 tc08, List<string> args)
{
    if (args.Count < 2)
    {
        Console.WriteLine("Usage: dt0|dt1 att <file> [new]");
        Console.WriteLine("       dt0|dt1 read <block> <addr>");
        Console.WriteLine("       dt0|dt1 write <block> <addr>");
        return;
    }

    if (!TryParseDrive(args[0], out var driveIndex))
    {
        Console.WriteLine("Unknown drive. Use dt0 or dt1.");
        return;
    }

    var deviceCommand = args[1].ToLowerInvariant();
    switch (deviceCommand)
    {
        case "att":
        case "attach":
            AttachTape(tc08, args, driveIndex);
            return;
        case "read":
        case "write":
            HandleTapeCommand(machine, tc08, args);
            return;
        default:
            Console.WriteLine("Unknown TC08 command. Use att/attach, read, or write.");
            return;
    }
}

static void HandleLinePrinterCommand(LinePrinter linePrinter, List<string> args)
{
    if (args.Count < 2)
    {
        Console.WriteLine("Usage: lpt att|attach <file>");
        return;
    }

    var deviceCommand = args[1].ToLowerInvariant();
    if (deviceCommand == "att" || deviceCommand == "attach")
    {
        if (args.Count < 3)
        {
            Console.WriteLine("Usage: lpt att|attach <file>");
            return;
        }

        var path = string.Join(' ', args.GetRange(2, args.Count - 2));
        if (!linePrinter.Attach(path, out var error))
        {
            Console.WriteLine(error);
            return;
        }

        Console.WriteLine($"Line printer attached to {linePrinter.Path}.");
        return;
    }

    Console.WriteLine("Unknown LPT command. Use att|attach.");
}

static void HandleWatchdogCommand(List<string> args)
{
    if (args.Count < 2)
    {
        Console.WriteLine("Usage: watchdog restart");
        return;
    }

    var deviceCommand = args[1].ToLowerInvariant();
    if (deviceCommand == "restart")
    {
        Console.WriteLine("Watchdog device not yet implemented; restart acknowledged.");
        return;
    }

    Console.WriteLine("Unknown watchdog command. Use restart.");
}

static void HandleRx8eDevice(Pdp8 machine, Rx8e rx8e, List<string> args)
{
    if (args.Count < 2)
    {
        Console.WriteLine("Usage: rx0|rx1 att|attach <file> [new]");
        Console.WriteLine("       rx0|rx1 read <track> <sector> <addr>");
        Console.WriteLine("       rx0|rx1 write <track> <sector> <addr>");
        return;
    }

    var deviceCommand = args[1].ToLowerInvariant();
    var drive = args[0] == "rx1" ? 1 : 0;
    switch (deviceCommand)
    {
        case "att":
        case "attach":
            AttachRx(rx8e, drive, args);
            return;
        case "read":
            RxRead(machine, rx8e, drive, args);
            return;
        case "write":
            RxWrite(machine, rx8e, drive, args);
            return;
        default:
            Console.WriteLine("Unknown RX8E command. Use attach, read, or write.");
            return;
    }
}

static void AttachRx(Rx8e rx8e, int drive, List<string> args)
{
    if (args.Count < 3)
    {
        Console.WriteLine("Usage: rx0|rx1 att|attach <file> [new]");
        return;
    }

    var createIfMissing = args.Count > 3 && string.Equals(args[^1], "new", StringComparison.OrdinalIgnoreCase);
    var pathArgs = createIfMissing ? args.GetRange(2, args.Count - 3) : args.GetRange(2, args.Count - 2);
    if (pathArgs.Count == 0)
    {
        Console.WriteLine("Usage: rx0|rx1 att|attach <file> [new]");
        return;
    }

    var path = string.Join(' ', pathArgs);
    if (!rx8e.Attach(drive, path, createIfMissing, out var error))
    {
        Console.WriteLine($"Attach failed: {error}");
        return;
    }

    Console.WriteLine($"RX{drive} attached to {path}.");
}

static void RxRead(Pdp8 machine, Rx8e rx8e, int drive, List<string> args)
{
    if (args.Count < 5)
    {
        Console.WriteLine("Usage: rx0|rx1 read <track> <sector> <addr>");
        return;
    }

    if (!TryParseOctal(args[2], out var track))
    {
        Console.WriteLine("Invalid track.");
        return;
    }

    if (!TryParseOctal(args[3], out var sector))
    {
        Console.WriteLine("Invalid sector.");
        return;
    }

    if (!TryParseOctal(args[4], out var addr))
    {
        Console.WriteLine("Invalid address.");
        return;
    }

    var status = rx8e.GetStatus(drive);
    var wordsPerSector = status.Density == RxDensity.Rx02 ? 128 : 64;
    var buffer = new ushort[wordsPerSector];
    if (!rx8e.TryReadSector(drive, track, sector, buffer, out var error))
    {
        var currentFail = addr & 0xFFF;
        for (var i = 0; i < wordsPerSector; i++)
        {
            machine.Write(currentFail, 0);
            currentFail = (currentFail + 1) & 0xFFF;
        }

        Console.WriteLine($"Read failed: {error}");
        return;
    }

    var current = addr & 0xFFF;
    for (var i = 0; i < wordsPerSector; i++)
    {
        machine.Write(current, buffer[i]);
        current = (current + 1) & 0xFFF;
    }

    Console.WriteLine($"RX{drive} read track {track} sector {sector} into {Pdp8.ToOctal(addr, 4)}.");
}

static void RxWrite(Pdp8 machine, Rx8e rx8e, int drive, List<string> args)
{
    if (args.Count < 5)
    {
        Console.WriteLine("Usage: rx0|rx1 write <track> <sector> <addr>");
        return;
    }

    if (!TryParseOctal(args[2], out var track))
    {
        Console.WriteLine("Invalid track.");
        return;
    }

    if (!TryParseOctal(args[3], out var sector))
    {
        Console.WriteLine("Invalid sector.");
        return;
    }

    if (!TryParseOctal(args[4], out var addr))
    {
        Console.WriteLine("Invalid address.");
        return;
    }

    var status = rx8e.GetStatus(drive);
    var wordsPerSector = status.Density == RxDensity.Rx02 ? 128 : 64;
    var buffer = new ushort[wordsPerSector];
    var current = addr & 0xFFF;
    for (var i = 0; i < wordsPerSector; i++)
    {
        buffer[i] = machine.Read(current);
        current = (current + 1) & 0xFFF;
    }

    if (!rx8e.TryWriteSector(drive, track, sector, buffer, out var error))
    {
        Console.WriteLine($"Write failed: {error}");
        return;
    }

    Console.WriteLine($"RX{drive} wrote track {track} sector {sector} from {Pdp8.ToOctal(addr, 4)}.");
}

static void HandleTapeCommand(Pdp8 machine, Tc08 tc08, List<string> args)
{
    if (args.Count < 4)
    {
        Console.WriteLine("Usage: dt0|dt1 read <block> <addr>");
        Console.WriteLine("       dt0|dt1 write <block> <addr>");
        return;
    }

    if (!TryParseDrive(args[0], out var driveIndex))
    {
        Console.WriteLine("Unknown drive. Use dt0 or dt1.");
        return;
    }

    var operation = args[1].ToLowerInvariant();
    if (!TryParseOctal(args[2], out var block))
    {
        Console.WriteLine("Invalid block number.");
        return;
    }

    if (!TryParseOctal(args[3], out var address))
    {
        Console.WriteLine("Invalid address.");
        return;
    }

    var startAddress = address & 0xFFF;
    if (operation == "read")
    {
        var buffer = new ushort[Tc08.WordsPerBlock];
        if (!tc08.TryReadBlock(driveIndex, block, buffer, out var error))
        {
            Console.WriteLine($"Read failed: {error}");
            return;
        }

        var current = startAddress;
        for (var i = 0; i < Tc08.WordsPerBlock; i++)
        {
            machine.Write(current, buffer[i]);
            current = (current + 1) & 0xFFF;
        }

        Console.WriteLine(
            $"Read block {Pdp8.ToOctal(block, 4)} from DT{driveIndex} into {Pdp8.ToOctal(startAddress, 4)}.");
        return;
    }

    if (operation == "write")
    {
        var buffer = new ushort[Tc08.WordsPerBlock];
        var current = startAddress;
        for (var i = 0; i < Tc08.WordsPerBlock; i++)
        {
            buffer[i] = machine.Read(current);
            current = (current + 1) & 0xFFF;
        }

        if (!tc08.TryWriteBlock(driveIndex, block, buffer, out var error))
        {
            Console.WriteLine($"Write failed: {error}");
            return;
        }

        Console.WriteLine(
            $"Wrote block {Pdp8.ToOctal(block, 4)} from {Pdp8.ToOctal(startAddress, 4)} to DT{driveIndex}.");
        return;
    }

    Console.WriteLine("Unknown operation. Use read or write.");
}

static void AttachTape(Tc08 tc08, List<string> args, int driveIndex)
{
    if (args.Count < 3)
    {
        Console.WriteLine("Usage: dt0|dt1 att <file> [new]");
        return;
    }

    string? attachError;
    var createIfMissing = false;
    if (args.Count > 3 && string.Equals(args[^1], "new", StringComparison.OrdinalIgnoreCase))
    {
        createIfMissing = true;
    }

    var pathArgs = createIfMissing ? args.GetRange(2, args.Count - 3) : args.GetRange(2, args.Count - 2);
    if (pathArgs.Count == 0)
    {
        Console.WriteLine("Usage: dt0|dt1 att <file> [new]");
        return;
    }

    var attachedPath = string.Join(' ', pathArgs);
    if (!tc08.Attach(driveIndex, attachedPath, createIfMissing, out attachError))
    {
        Console.WriteLine($"Attach failed: {attachError}");
        return;
    }

    Console.WriteLine($"Attached DT{driveIndex} to {attachedPath}.");
}

static void ShowDevice(Tc08 tc08, LinePrinter linePrinter, Rx8e rx8e, List<string> args)
{
    if (args.Count < 2)
    {
        Console.WriteLine("Usage: show dev|dt");
        return;
    }

    var device = args[1].ToLowerInvariant();
    if (device == "dev")
    {
        ShowAllDevices(tc08, linePrinter, rx8e);
        return;
    }

    if (device == "dt")
    {
        ShowTc08(tc08);
        return;
    }

    Console.WriteLine("Unknown device.");
}

static void ShowAllDevices(Tc08 tc08, LinePrinter linePrinter, Rx8e rx8e)
{
    Console.WriteLine("Devices:");
    Console.WriteLine($"  TTI ({Pdp8.ToOctal(TtiDevice, 2)}): console input (KSR)");
    Console.WriteLine($"  TTO ({Pdp8.ToOctal(TtoDevice, 2)}): console output (KSR)");
    var lptStatus = linePrinter.Attached
        ? $"attached to {linePrinter.Path}"
        : "not attached";
    Console.WriteLine($"  LPT (060): line printer ({lptStatus})");
    var rx0 = rx8e.GetStatus(0);
    var rx1 = rx8e.GetStatus(1);
    var rx0Status = rx0.Attached ? $"{rx0.Density} @ {rx0.Path}" : "empty";
    var rx1Status = rx1.Attached ? $"{rx1.Density} @ {rx1.Path}" : "empty";
    Console.WriteLine($"  RX8E: rx0 {rx0Status}, rx1 {rx1Status}");

    var dt0 = tc08.GetDriveStatus(0);
    var dt1 = tc08.GetDriveStatus(1);
    var dtSummary = $"dt0 {(dt0.Attached ? "attached" : "empty")}, dt1 {(dt1.Attached ? "attached" : "empty")}";
    Console.WriteLine(
        $"  TC08 ({Pdp8.ToOctal(Tc08ControlDevice, 2)}/{Pdp8.ToOctal(Tc08DataDevice, 2)}): {dtSummary}");
}

static void ShowTc08(Tc08 tc08)
{
    Console.WriteLine("TC08:");
    for (var i = 0; i < Tc08.DriveCount; i++)
    {
        var status = tc08.GetDriveStatus(i);
        if (status.Attached)
        {
            Console.WriteLine($"  DT{i}: {status.Path} ({status.SizeBytes} bytes)");
        }
        else
        {
            Console.WriteLine($"  DT{i}: empty");
        }
    }
}

static bool TryParseDrive(string token, out int driveIndex)
{
    var normalized = token.ToLowerInvariant();
    if (normalized == "dt0")
    {
        driveIndex = 0;
        return true;
    }

    if (normalized == "dt1")
    {
        driveIndex = 1;
        return true;
    }

    driveIndex = -1;
    return false;
}

static string ExpandEscapes(string value)
{
    if (value.IndexOf('\\') < 0)
    {
        return value;
    }

    var result = new List<char>(value.Length);
    for (var i = 0; i < value.Length; i++)
    {
        var ch = value[i];
        if (ch != '\\' || i == value.Length - 1)
        {
            result.Add(ch);
            continue;
        }

        var next = value[++i];
        switch (next)
        {
            case 'n':
                result.Add('\n');
                break;
            case 'r':
                result.Add('\r');
                break;
            case 't':
                result.Add('\t');
                break;
            case '\\':
                result.Add('\\');
                break;
            case '"':
                result.Add('\"');
                break;
            case '0':
                result.Add('\0');
                break;
            default:
                result.Add(next);
                break;
        }
    }

    return new string(result.ToArray());
}

static void LoadImage(Pdp8 machine, List<string> args)
{
    if (args.Count < 2)
    {
        Console.WriteLine("Usage: load <file>");
        return;
    }

    try
    {
        var loaded = machine.LoadImage(args[1]);
        Console.WriteLine($"Loaded {loaded} words.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Load failed: {ex.Message}");
    }
}

static void SaveImage(Pdp8 machine, List<string> args)
{
    if (args.Count < 2)
    {
        Console.WriteLine("Usage: save <file>");
        return;
    }

    try
    {
        using var writer = new StreamWriter(args[1], false);
        for (var addr = 0; addr < Pdp8.MemorySize; addr += 8)
        {
            var lineWords = new List<string>(9)
            {
                $"{Pdp8.ToOctal(addr, 4)}:"
            };
            for (var i = 0; i < 8 && addr + i < Pdp8.MemorySize; i++)
            {
                var word = machine.Read(addr + i);
                lineWords.Add(Pdp8.ToOctal(word, 4));
            }

            writer.WriteLine(string.Join(' ', lineWords));
        }

        Console.WriteLine($"Saved {Pdp8.MemorySize} words to {args[1]}.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Save failed: {ex.Message}");
    }
}

static void AssembleFile(List<string> args)
{
    if (args.Count < 2)
    {
        Console.WriteLine("Usage: .a <source.pa> [dest.srec]");
        return;
    }

    var source = args[1];
    var dest = args.Count > 2 ? args[2] : Path.ChangeExtension(source, ".srec");

    try
    {
        Pdp8Assembler.AssembleFile(source, dest);
        Console.WriteLine($"Assembled {source} -> {dest}");
    }
    catch (AsmError ex)
    {
        Console.WriteLine($"Assembly failed: {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Assembly error: {ex.Message}");
    }
}

static void SetProgramCounter(Pdp8 machine, List<string> args)
{
    if (args.Count < 2)
    {
        Console.WriteLine("Usage: pc <addr>");
        return;
    }

    if (!TryParseOctal(args[1], out var addr))
    {
        Console.WriteLine("Invalid address.");
        return;
    }

    machine.SetProgramCounter((ushort)addr);
    Console.WriteLine($"PC set to {Pdp8.ToOctal(addr, 4)}");
}

static void StepMachine(Pdp8 machine, List<string> args)
{
    var steps = 1;
    if (args.Count > 1 && !TryParseOctal(args[1], out steps))
    {
        Console.WriteLine("Invalid step count.");
        return;
    }

    var executed = 0;
    for (var i = 0; i < steps; i++)
    {
        if (machine.Halted)
        {
            break;
        }

        machine.Step();
        executed++;
    }

    Console.WriteLine($"Executed {executed} step(s)." );
    PrintRegisters(machine);
}

static void RunMachine(Pdp8 machine, List<string> args)
{
    var maxSteps = 100000;
    if (args.Count > 1 && !TryParseOctal(args[1], out maxSteps))
    {
        Console.WriteLine("Invalid max step count.");
        return;
    }

    machine.ClearHalt();
    var executed = machine.Run(maxSteps);
    Console.WriteLine($"\nExecuted {executed} step(s)." );
    PrintRegisters(machine);
}

static void TraceMachine(Pdp8 machine, List<string> args)
{
    var steps = 1;
    if (args.Count > 1 && !TryParseOctal(args[1], out steps))
    {
        Console.WriteLine("Invalid trace count.");
        return;
    }

    machine.ClearHalt();
    for (var i = 0; i < steps; i++)
    {
        if (machine.Halted)
        {
            Console.WriteLine("Halt encountered.");
            break;
        }

        machine.Step();
        Console.Write($"{i + 1}: ");
        PrintRegisters(machine);
    }
}

static void StartTnfsShell()
{
    StartTnfsShellAsync().GetAwaiter().GetResult();
}

static async Task StartTnfsShellAsync()
{
    const int DefaultPort = 16384;
    const byte DefaultVersionMajor = 1;
    const byte DefaultVersionMinor = 2;

    TnfsClient? client = null;
    string? currentHost = null;
    int currentPort = DefaultPort;
    string? currentMount = null;

    Console.WriteLine("TNFS shell. Type 'help' for commands or 'exit' to return.");
    try
    {
        while (true)
        {
            Console.Write("tnfs> ");
            var line = Console.ReadLine();
            if (line is null)
            {
                break;
            }

            var parts = SplitCommand(line.Trim());
            if (parts.Count == 0)
            {
                continue;
            }

            var command = parts[0].ToLowerInvariant();
            switch (command)
            {
                case "help":
                case "?":
                    ShowTnfsHelp();
                    break;
                case "exit":
                case "quit":
                case "back":
                    return;
                case "status":
                    ShowStatus();
                    break;
                case "mount":
                    await HandleMount(parts).ConfigureAwait(false);
                    break;
                case "ls":
                case "dir":
                    await HandleList(parts).ConfigureAwait(false);
                    break;
                case "get":
                    await HandleGet(parts).ConfigureAwait(false);
                    break;
                case "umount":
                case "unmount":
                    await HandleUmount().ConfigureAwait(false);
                    break;
                default:
                    Console.WriteLine("Unknown TNFS command. Type 'help' for commands.");
                    break;
            }
        }
    }
    finally
    {
        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    async Task HandleMount(List<string> args)
    {
        if (args.Count < 3)
        {
            PrintMountUsage();
            return;
        }

        if (!TryParseHost(args[1], DefaultPort, out var host, out var port))
        {
            Console.WriteLine("Invalid host.");
            return;
        }

        var mountPath = args[2];
        var user = (string?)null;
        var password = (string?)null;
        var versionMajor = DefaultVersionMajor;
        var versionMinor = DefaultVersionMinor;

        for (var i = 3; i < args.Count; i++)
        {
            var token = args[i];
            if (TryGetOption(args, ref i, "user", out var optUser))
            {
                user = optUser;
                continue;
            }

            if (TryGetOption(args, ref i, "pass", out var optPass) ||
                TryGetOption(args, ref i, "password", out optPass))
            {
                password = optPass;
                continue;
            }

            if (TryGetOption(args, ref i, "port", out var portText))
            {
                if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
                {
                    Console.WriteLine("Invalid port.");
                    return;
                }

                continue;
            }

            if (TryGetOption(args, ref i, "ver", out var verText) ||
                TryGetOption(args, ref i, "version", out verText))
            {
                if (!TryParseVersion(verText, out versionMajor, out versionMinor))
                {
                    Console.WriteLine("Invalid version. Use major.minor (e.g. 1.2).");
                    return;
                }

                continue;
            }

            Console.WriteLine($"Unknown option: {token}");
            return;
        }

        if (port <= 0 || port > 65535)
        {
            Console.WriteLine("Invalid port. Must be between 1 and 65535.");
            return;
        }

        if (client is not null && client.IsMounted)
        {
            Console.WriteLine("Already mounted. Run 'umount' first.");
            return;
        }

        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            client = null;
        }

        client = new TnfsClient(host, port);
        try
        {
            var result = await client
                .MountAsync(versionMajor, versionMinor, mountPath, user, password)
                .ConfigureAwait(false);

            currentHost = host;
            currentPort = port;
            currentMount = mountPath;
            var serverVersion = FormatVersion(result.ServerVersion);
            Console.WriteLine(
                $"Mounted '{mountPath}' on {host}:{port} (conn {result.ConnectionId}, server {serverVersion}, min retry {result.MinRetryMilliseconds} ms).");
        }
        catch (TnfsException ex)
        {
            Console.WriteLine($"TNFS mount failed (status 0x{ex.StatusCode:X2}): {ex.Message}");
            await client.DisposeAsync().ConfigureAwait(false);
            client = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TNFS mount error: {ex.Message}");
            await client.DisposeAsync().ConfigureAwait(false);
            client = null;
        }
    }

    async Task HandleUmount()
    {
        if (client is null)
        {
            Console.WriteLine("Not connected.");
            return;
        }

        if (!client.IsMounted)
        {
            Console.WriteLine("Not mounted.");
            return;
        }

        try
        {
            await client.UmountAsync().ConfigureAwait(false);
            currentMount = null;
            Console.WriteLine("Unmounted.");
        }
        catch (TnfsException ex)
        {
            Console.WriteLine($"TNFS umount failed (status 0x{ex.StatusCode:X2}): {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TNFS umount error: {ex.Message}");
        }
    }

    async Task HandleList(List<string> args)
    {
        if (!EnsureMounted())
        {
            return;
        }

        var pathArg = args.Count > 1 ? args[1] : string.Empty;
        var remotePath = ResolveRemotePath(pathArg);
        byte handle;
        try
        {
            handle = await client!.OpenDirAsync(remotePath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TNFS opendir failed: {ex.Message}");
            return;
        }

        var entries = new List<(string Name, TnfsStat? Stat)>();
        try
        {
            while (true)
            {
                var entry = await client!.ReadDirEntryAsync(handle).ConfigureAwait(false);
                if (entry is null)
                {
                    break;
                }

                if (entry == "." || entry == "..")
                {
                    continue;
                }

                TnfsStat? stat = null;
                try
                {
                    var fullPath = CombineRemotePath(remotePath, entry);
                    stat = await client.StatAsync(fullPath).ConfigureAwait(false);
                }
                catch (TnfsException)
                {
                    // Ignore stat errors in listings.
                }
                catch
                {
                    // Ignore other stat failures and keep listing names.
                }

                entries.Add((entry, stat));
            }
        }
        finally
        {
            try
            {
                await client!.CloseDirAsync(handle).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (entries.Count == 0)
        {
            Console.WriteLine("(empty)");
            return;
        }

        foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            var stat = entry.Stat;
            var isDir = stat?.IsDirectory == true;
            var sizeText = stat.HasValue ? stat.Value.Size.ToString(CultureInfo.InvariantCulture) : "?";
            var suffix = isDir ? "/" : string.Empty;
            var kind = isDir ? "d" : "-";
            Console.WriteLine($"{kind} {sizeText,10} {entry.Name}{suffix}");
        }
    }

    async Task HandleGet(List<string> args)
    {
        if (args.Count < 2)
        {
            Console.WriteLine("Usage: get <remote-path> [local-path]");
            return;
        }

        if (!EnsureMounted())
        {
            return;
        }

        var remotePath = ResolveRemotePath(args[1]);
        var localPath = args.Count > 2
            ? args[2]
            : Path.Combine("sd", Path.GetFileName(remotePath));

        if (string.IsNullOrWhiteSpace(localPath))
        {
            Console.WriteLine("Invalid local path.");
            return;
        }

        var localDir = Path.GetDirectoryName(localPath);
        if (string.IsNullOrEmpty(localDir))
        {
            localDir = "sd";
            localPath = Path.Combine(localDir, localPath);
        }

        try
        {
            Directory.CreateDirectory(localDir);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create local directory '{localDir}': {ex.Message}");
            return;
        }

        byte handle;
        try
        {
            handle = await client!.OpenFileAsync(remotePath, TnfsClient.O_RDONLY).ConfigureAwait(false);
        }
        catch (TnfsException ex)
        {
            Console.WriteLine($"TNFS open failed (status 0x{ex.StatusCode:X2}): {ex.Message}");
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TNFS open error: {ex.Message}");
            return;
        }

        var total = 0;
        try
        {
            await using var fs = File.Create(localPath);
            while (true)
            {
                var read = await client!.ReadFileAsync(handle, 1024).ConfigureAwait(false);
                if (read.IsEof)
                {
                    break;
                }

                if (read.BytesRead == 0)
                {
                    break;
                }

                await fs.WriteAsync(read.Data, CancellationToken.None).ConfigureAwait(false);
                total += read.BytesRead;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Download failed: {ex.Message}");
            try
            {
                File.Delete(localPath);
            }
            catch
            {
            }

            return;
        }
        finally
        {
            try
            {
                await client!.CloseFileAsync(handle).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        Console.WriteLine($"Downloaded {total} byte(s) to {localPath}.");
    }

    void ShowStatus()
    {
        if (client is null)
        {
            Console.WriteLine("Not connected. Use 'mount <host> <path>' to connect.");
            return;
        }

        var target = currentHost is null ? "(unknown host)" : $"{currentHost}:{currentPort}";
        if (!client.IsMounted)
        {
            Console.WriteLine($"Not mounted (client configured for {target}).");
            return;
        }

        var serverVersion = FormatVersion(client.ServerVersion);
        var path = currentMount ?? "(unknown path)";
        Console.WriteLine(
            $"Mounted to {target} path '{path}' (conn {client.ConnectionId}, server {serverVersion}, min retry {client.MinRetryMilliseconds} ms).");
    }

    bool EnsureMounted()
    {
        if (client is null || !client.IsMounted)
        {
            Console.WriteLine("Not mounted. Use 'mount <host> <path>' first.");
            return false;
        }

        return true;
    }

    string ResolveRemotePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return currentMount ?? "/";
        }

        if (path.StartsWith("/", StringComparison.Ordinal) || path.Contains(':'))
        {
            return path;
        }

        return CombineRemotePath(currentMount ?? "/", path);
    }

    static string CombineRemotePath(string basePath, string child)
    {
        if (string.IsNullOrEmpty(basePath))
        {
            return child;
        }

        if (basePath.EndsWith("/", StringComparison.Ordinal))
        {
            return basePath + child;
        }

        return basePath + "/" + child;
    }

    void ShowTnfsHelp()
    {
        Console.WriteLine("TNFS commands:");
        PrintMountUsage();
        Console.WriteLine("  ls [path]              List files at path (default: mount point)");
        Console.WriteLine("  get <remote> [local]   Download a file (default local ./sd/<name>)");
        Console.WriteLine("  umount                 Disconnect the current session");
        Console.WriteLine("  status                 Show current mount state");
        Console.WriteLine("  exit                   Return to the main shell");
    }

    void PrintMountUsage()
    {
        Console.WriteLine(
            "  mount <host> <path> [user <id>] [pass <pwd>] [port <port>] [ver <major.minor>]");
    }

    static bool TryGetOption(List<string> args, ref int index, string name, out string value)
    {
        var token = args[index];
        if (string.Equals(token, name, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= args.Count)
            {
                value = string.Empty;
                return false;
            }

            value = args[index + 1];
            index++;
            return true;
        }

        var prefix = name + "=";
        if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = token[prefix.Length..];
            return true;
        }

        value = string.Empty;
        return false;
    }

    static bool TryParseHost(string hostArg, int defaultPort, out string host, out int port)
    {
        host = hostArg;
        port = defaultPort;
        if (string.IsNullOrWhiteSpace(hostArg))
        {
            return false;
        }

        if (hostArg.StartsWith("[", StringComparison.Ordinal) && hostArg.Contains(']'))
        {
            var end = hostArg.IndexOf(']');
            if (end <= 1)
            {
                return false;
            }

            host = hostArg[1..end];
            var remainder = hostArg[(end + 1)..];
            if (remainder.StartsWith(":", StringComparison.Ordinal) &&
                int.TryParse(remainder[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBracketPort))
            {
                port = parsedBracketPort;
            }

            return true;
        }

        var firstColon = hostArg.IndexOf(':');
        var lastColon = hostArg.LastIndexOf(':');
        if (firstColon > 0 && firstColon == lastColon &&
            int.TryParse(hostArg[(lastColon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort))
        {
            host = hostArg[..lastColon];
            port = parsedPort;
        }

        return true;
    }

    static bool TryParseVersion(string text, out byte major, out byte minor)
    {
        major = 0;
        minor = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('.', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out major);
        }

        if (parts.Length != 2)
        {
            return false;
        }

        if (!byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out major))
        {
            return false;
        }

        return byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minor);
    }

    static string FormatVersion(ushort version)
    {
        var major = version >> 8;
        var minor = version & 0xFF;
        return $"{major}.{minor}";
    }
}

static bool TryParseOctal(string token, out int value)
{
    try
    {
        value = Convert.ToInt32(token, 8);
        return true;
    }
    catch
    {
        value = 0;
        return false;
    }
}
