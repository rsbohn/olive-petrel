using OlivePetrel;

const int TtiDevice = 3;
const int TtoDevice = 4;
const int Tc08ControlDevice = 8;
const int Tc08DataDevice = 9;

var machine = new Pdp8();
var tc08 = new Tc08();
var linePrinter = new LinePrinter();
var rx8e = new Rx8e();
machine.LinePrinter = linePrinter;
machine.Rx8e = rx8e;
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
        case "h":
            PrintHelp();
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

static void PrintHelp()
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  help                Show this help.");
    Console.WriteLine("  regs                Show registers.");
    Console.WriteLine("  mem <addr> [count]  Dump memory (octal, e.g. 020).");
    Console.WriteLine("  dep <addr> <word|string>.. Deposit octal words or ASCII strings.");
    Console.WriteLine("  dt0|dt1 att|attach <file> [new]   Attach or create a DECtape image.");
    Console.WriteLine("  dt0|dt1 read <block> <addr>  Read a 129-word block into memory.");
    Console.WriteLine("  dt0|dt1 write <block> <addr> Write a 129-word block from memory.");
    Console.WriteLine("  lpt att|attach <file>       Attach a line printer output file.");
    Console.WriteLine("  watchdog restart     Restart the watchdog.");
    Console.WriteLine("  rx0|rx1 att|attach <file> [new] Attach or create an RX01/RX02 image.");
    Console.WriteLine("  rx0|rx1 read <track> <sector> <addr>  Read a sector into memory.");
    Console.WriteLine("  rx0|rx1 write <track> <sector> <addr> Write a sector from memory.");
    Console.WriteLine("  load <file>         Load a simple octal image.");
    Console.WriteLine("  save <file>         Save core memory as a loadable image.");
    Console.WriteLine("  pc <addr>           Set the program counter.");
    Console.WriteLine("  show dev            Show attached devices.");
    Console.WriteLine("  show dt             Show TC08 status.");
    Console.WriteLine("  step [count]        Execute one or more instructions.");
    Console.WriteLine("  run [max]           Run up to max instructions (default 100000).");
    Console.WriteLine("  trace [count]       Step count instructions, showing registers after each.");
    Console.WriteLine("  reset               Clear memory and registers.");
    Console.WriteLine("  quit                Exit the emulator.");
    Console.WriteLine();
    Console.WriteLine("Image format:");
    Console.WriteLine("  Use <addr>: <word0> <word1> ... to set the load address.");
    Console.WriteLine("  Example: 0200: 7300 6041 7402 0000");
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
    Console.WriteLine($"Executed {executed} step(s)." );
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
