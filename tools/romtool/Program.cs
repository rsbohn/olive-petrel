using System.Text.RegularExpressions;
using OlivePetrel;

namespace RomTool;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        try
        {
            return command switch
            {
                "build-lib" => BuildLib(rest),
                "link" => Link(rest),
                _ => Fail($"Unknown command '{command}'.")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"romtool error: {ex.Message}");
            return 1;
        }
    }

    private static int BuildLib(string[] args)
    {
        var outPath = RequireArg(args, "--out");
        var symPath = RequireArg(args, "--sym");
        var baseText = GetArg(args, "--base") ?? "0200";
        var pageText = GetArg(args, "--page") ?? "0200";
        var fileArgs = GetArgList(args, "--files");
        if (fileArgs.Count == 0)
        {
            return Fail("No input files. Use --files <file1> <file2> ...");
        }

        var baseAddr = ParseOctal(baseText);
        var pageSize = ParseOctal(pageText);
        if (pageSize <= 0)
        {
            return Fail("Page size must be positive.");
        }

        var memory = new Dictionary<int, int>();
        var symbols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var currentPageBase = baseAddr;
        var offset = 0;

        foreach (var file in fileArgs)
        {
            var lines = File.ReadAllLines(file);
            if (ContainsOriginDirective(lines))
            {
                return Fail($"Origin directive '*' not allowed in library routine: {file}");
            }

            var size = ComputeSize(lines, file);
            if (size > pageSize)
            {
                return Fail($"Routine {file} is {size} words; exceeds page size {pageSize}.");
            }

            if (offset + size > pageSize)
            {
                currentPageBase += pageSize;
                offset = 0;
            }

            var origin = currentPageBase + offset;
            var assembled = AssembleWithOrigin(lines, origin);
            MergeMemory(memory, assembled.Memory, file);
            MergeSymbols(symbols, assembled.Symbols, file);
            offset += size;
        }

        var startAddr = baseAddr;
        var records = Pdp8Assembler.WordsToSRecords(memory, startAddr);
        File.WriteAllText(outPath, string.Join(Environment.NewLine, records) + Environment.NewLine);

        var symLines = symbols
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => $"{kvp.Key} = {FormatOctal(kvp.Value, 4)}");
        File.WriteAllText(symPath, string.Join(Environment.NewLine, symLines) + Environment.NewLine);

        Console.WriteLine($"Built {outPath} ({memory.Count} words) and {symPath} ({symbols.Count} symbols).");
        return 0;
    }

    private static int Link(string[] args)
    {
        var libPath = RequireArg(args, "--lib");
        var symPath = RequireArg(args, "--sym");
        var appPath = RequireArg(args, "--app");
        var outPath = RequireArg(args, "--out");

        var libMemory = ReadSRecords(libPath);
        var symbolMap = ReadSymbolTable(symPath);

        var appLines = File.ReadAllLines(appPath);
        var linkedLines = ApplyLinkPlaceholders(appLines, symbolMap, appPath);

        var assembler = new Pdp8Assembler(linkedLines);
        assembler.FirstPass();
        var appMemory = assembler.SecondPass();
        var appSymbols = assembler.Symbols;

        var combined = new Dictionary<int, int>(libMemory);
        MergeMemory(combined, appMemory, appPath);

        var startAddr = appSymbols.TryGetValue("START", out var start) ? start : appMemory.Keys.Min();
        var records = Pdp8Assembler.WordsToSRecords(combined, startAddr);
        File.WriteAllText(outPath, string.Join(Environment.NewLine, records) + Environment.NewLine);
        Console.WriteLine($"Linked {appPath} -> {outPath} ({combined.Count} words).");
        return 0;
    }

    private static (Dictionary<int, int> Memory, IReadOnlyDictionary<string, int> Symbols) AssembleWithOrigin(
        string[] lines,
        int origin)
    {
        var originLine = $"* {FormatOctal(origin, 4)}";
        var expanded = new List<string> { originLine };
        expanded.AddRange(lines);

        var assembler = new Pdp8Assembler(expanded);
        assembler.FirstPass();
        var memory = assembler.SecondPass();
        return (memory, assembler.Symbols);
    }

    private static int ComputeSize(string[] lines, string sourcePath)
    {
        var assembler = new Pdp8Assembler(lines);
        assembler.FirstPass();
        if (assembler.Statements.Count == 0)
        {
            throw new AsmError($"No statements in {sourcePath}");
        }

        var minAddr = assembler.Statements.Min(s => s.Address);
        var maxAddr = assembler.Statements.Max(s => s.Address);
        if (minAddr != 0)
        {
            throw new AsmError($"Routine {sourcePath} must be position-independent (expected origin 0000).");
        }

        return (maxAddr - minAddr) + 1;
    }

    private static bool ContainsOriginDirective(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var slashIndex = raw.IndexOf('/');
            var line = slashIndex >= 0 ? raw[..slashIndex] : raw;
            if (line.TrimStart().StartsWith("*", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void MergeMemory(Dictionary<int, int> target, Dictionary<int, int> source, string name)
    {
        foreach (var (addr, value) in source)
        {
            if (target.TryGetValue(addr, out var existing) && existing != value)
            {
                throw new InvalidOperationException(
                    $"Memory overlap at {FormatOctal(addr, 4)} between {name} and existing image.");
            }

            target[addr] = value;
        }
    }

    private static void MergeSymbols(Dictionary<string, int> target, IReadOnlyDictionary<string, int> source, string name)
    {
        foreach (var (sym, value) in source)
        {
            if (target.TryGetValue(sym, out var existing) && existing != value)
            {
                throw new InvalidOperationException(
                    $"Duplicate symbol '{sym}' from {name} (existing at {FormatOctal(existing, 4)}).");
            }

            target[sym] = value;
        }
    }

    private static Dictionary<string, int> ReadSymbolTable(string path)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lineNo = 0;
        foreach (var raw in File.ReadLines(path))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var match = Regex.Match(line, @"^([A-Za-z0-9_]+)\s*(?:=|\s)\s*([0-7]+)$");
            if (!match.Success)
            {
                throw new InvalidOperationException($"Invalid symbol table line {lineNo}: {raw}");
            }

            var name = match.Groups[1].Value;
            var addr = ParseOctal(match.Groups[2].Value);
            map[name] = addr;
        }

        return map;
    }

    private static string[] ApplyLinkPlaceholders(string[] lines, Dictionary<string, int> symbols, string sourcePath)
    {
        var output = new string[lines.Length];
        var regex = new Regex(@"^(?<lead>\s*(?:[A-Za-z0-9_]+\s*,\s*)?)LINK\s+(?<sym>[A-Za-z0-9_]+)\s*$",
            RegexOptions.IgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var slashIndex = raw.IndexOf('/');
            var code = slashIndex >= 0 ? raw[..slashIndex] : raw;
            var comment = slashIndex >= 0 ? raw[slashIndex..] : string.Empty;

            var match = regex.Match(code);
            if (!match.Success)
            {
                output[i] = raw;
                continue;
            }

            var sym = match.Groups["sym"].Value;
            if (!symbols.TryGetValue(sym, out var addr))
            {
                throw new InvalidOperationException($"Unknown LINK symbol '{sym}' in {sourcePath} line {i + 1}.");
            }

            var lead = match.Groups["lead"].Value;
            output[i] = $"{lead}{FormatOctal(addr, 4)}{comment}";
        }

        return output;
    }

    private static Dictionary<int, int> ReadSRecords(string path)
    {
        var byteMap = new Dictionary<int, int>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || !line.StartsWith("S1", StringComparison.Ordinal))
            {
                continue;
            }

            var count = Convert.ToInt32(line.Substring(2, 2), 16);
            var address = Convert.ToInt32(line.Substring(4, 4), 16);
            var dataLen = (count - 3) * 2;
            var dataHex = line.Substring(8, dataLen);
            for (var i = 0; i < dataHex.Length; i += 2)
            {
                var value = Convert.ToInt32(dataHex.Substring(i, 2), 16);
                byteMap[address + (i / 2)] = value;
            }
        }

        var memory = new Dictionary<int, int>();
        foreach (var (byteAddr, value) in byteMap)
        {
            if ((byteAddr & 1) != 0)
            {
                continue;
            }

            if (!byteMap.TryGetValue(byteAddr + 1, out var high))
            {
                continue;
            }

            var word = (high << 8) | value;
            memory[byteAddr / 2] = word & 0x0FFF;
        }

        return memory;
    }

    private static int ParseOctal(string text)
    {
        return Convert.ToInt32(text, 8) & 0x0FFF;
    }

    private static string FormatOctal(int value, int width = 4)
    {
        var octal = Convert.ToString(value & 0x0FFF, 8);
        return octal.PadLeft(width, '0');
    }

    private static string? GetArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string RequireArg(string[] args, string name)
    {
        var value = GetArg(args, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required argument {name}.");
        }

        return value;
    }

    private static List<string> GetArgList(string[] args, string name)
    {
        var list = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == name)
            {
                for (var j = i + 1; j < args.Length; j++)
                {
                    if (args[j].StartsWith("--", StringComparison.Ordinal))
                    {
                        break;
                    }

                    list.Add(args[j]);
                }
                break;
            }
        }

        return list;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("romtool build-lib --out <lib.rom> --sym <lib.sym> --files <file1> <file2> ... [--base 0200] [--page 0200]");
        Console.WriteLine("romtool link --lib <lib.rom> --sym <lib.sym> --app <app.pa> --out <app.rom>");
    }
}
