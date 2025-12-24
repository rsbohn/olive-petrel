using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace OlivePetrel;

public sealed class AsmError : Exception
{
    public int? LineNumber { get; }
    public string? SourceText { get; }

    public AsmError(string message, int? lineNumber = null, string? text = null)
        : base(BuildMessage(message, lineNumber, text))
    {
        LineNumber = lineNumber;
        SourceText = text;
    }

    private static string BuildMessage(string message, int? lineNumber, string? text)
    {
        var prefix = lineNumber is null ? string.Empty : $"Line {lineNumber}: ";
        var suffix = string.IsNullOrEmpty(text) ? string.Empty : $" [source: {text}]";
        return prefix + message + suffix;
    }
}

public enum StatementKind
{
    Data,
    DataSymbol,
    Iot,
    Mem,
    Operate,
    EmitAddress
}

public abstract record Statement(int Address, int LineNumber, string Text, string Raw)
{
    public abstract StatementKind Kind { get; }
}

public sealed record DataStatement(int Address, int Value, int LineNumber, string Text, string Raw)
    : Statement(Address, LineNumber, Text, Raw)
{
    public override StatementKind Kind => StatementKind.Data;
}

public sealed record DataSymbolStatement(int Address, string Symbol, int LineNumber, string Text, string Raw)
    : Statement(Address, LineNumber, Text, Raw)
{
    public override StatementKind Kind => StatementKind.DataSymbol;
}

public sealed record IotStatement(int Address, string Token, int LineNumber, string Text, string Raw)
    : Statement(Address, LineNumber, Text, Raw)
{
    public override StatementKind Kind => StatementKind.Iot;
}

public sealed record MemStatement(int Address, string Opcode, bool Indirect, string OperandToken, int LineNumber, string Text, string Raw)
    : Statement(Address, LineNumber, Text, Raw)
{
    public override StatementKind Kind => StatementKind.Mem;
}

public sealed record OperateStatement(int Address, IReadOnlyList<string> Tokens, int LineNumber, string Text, string Raw)
    : Statement(Address, LineNumber, Text, Raw)
{
    public override StatementKind Kind => StatementKind.Operate;
}

public sealed record EmitAddressStatement(int Address, int LineNumber, string Text, string Raw)
    : Statement(Address, LineNumber, Text, Raw)
{
    public override StatementKind Kind => StatementKind.EmitAddress;
}

public sealed class Pdp8Assembler
{
    private const int WordMask = 0x0FFF;
    private static readonly int PageSize = Octal("200");

    private static readonly Dictionary<string, int> MemrefOps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AND"] = Octal("0000"),
        ["TAD"] = Octal("1000"),
        ["ISZ"] = Octal("2000"),
        ["DCA"] = Octal("3000"),
        ["JMS"] = Octal("4000"),
        ["JMP"] = Octal("5000")
    };

    private static readonly Dictionary<string, int> Group1Bits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CLA"] = Octal("0200"),
        ["CLL"] = Octal("0100"),
        ["CMA"] = Octal("0040"),
        ["CML"] = Octal("0020"),
        ["RAR"] = Octal("0010"),
        ["RAL"] = Octal("0004"),
        ["RTR"] = Octal("0012"),
        ["RTL"] = Octal("0006"),
        ["BSW"] = Octal("0002"),
        ["IAC"] = Octal("0001")
    };

    private static readonly Dictionary<string, int> Group2Bits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SMA"] = Octal("0100"),
        ["SZA"] = Octal("0040"),
        ["SNL"] = Octal("0020"),
        ["SPA"] = Octal("0110"),
        ["SNA"] = Octal("0050"),
        ["SZL"] = Octal("0030"),
        ["CLA"] = Octal("0200"),
        ["OSR"] = Octal("0004"),
        ["HLT"] = Octal("0002"),
        ["ION"] = Octal("0001"),
        ["IOFF"] = Octal("0000")
    };

    private static readonly Dictionary<string, int> IotMnemonics = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SKON"] = Octal("6002")
    };

    public static readonly Dictionary<string, int> PseudoOps = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _lines;
    private readonly Dictionary<string, int> _symbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Statement> _statements = new();
    private readonly List<int> _origins = new();
    private readonly TextWriter? _verboseWriter;

    public IReadOnlyDictionary<string, int> Symbols => new ReadOnlyDictionary<string, int>(_symbols);
    public IReadOnlyList<Statement> Statements => _statements;
    public IReadOnlyList<int> Origins => _origins;

    public Pdp8Assembler(IEnumerable<string> lines, TextWriter? verboseWriter = null)
    {
        _lines = lines.ToList();
        _verboseWriter = verboseWriter;
    }

    public void FirstPass()
    {
        var pseudoOpPattern = new Regex(@"^([A-Za-z0-9_]+)\s*=\s*([0-7]+)");
        var labelPattern = new Regex(@"\s*([A-Za-z0-9_]+),\s*(.*)");
        var location = 0;

        for (var index = 0; index < _lines.Count; index++)
        {
            var lineNo = index + 1;
            var raw = _lines[index];

            if (raw.Contains("/#show-table", StringComparison.Ordinal))
            {
                DumpPseudoOpcodeTable();
            }

            var stripped = StripComment(raw).Trim();
            if (string.IsNullOrEmpty(stripped))
            {
                continue;
            }

            var pseudoMatch = pseudoOpPattern.Match(stripped);
            if (pseudoMatch.Success)
            {
                var name = pseudoMatch.Groups[1].Value.ToUpperInvariant();
                var value = Convert.ToInt32(pseudoMatch.Groups[2].Value, 8);
                PseudoOps[name] = value;
                if (_symbols.ContainsKey(name))
                {
                    throw new AsmError($"Duplicate label '{name}'", lineNo);
                }

                _symbols[name] = value;
                continue;
            }

            if (stripped.StartsWith("*", StringComparison.Ordinal))
            {
                try
                {
                    location = Convert.ToInt32(stripped[1..].Trim(), 8);
                }
                catch (Exception ex)
                {
                    throw new AsmError($"Invalid origin directive: {stripped}", lineNo, stripped + $" ({ex.Message})");
                }

                if (_origins.Count == 0 || _origins[^1] != location)
                {
                    _origins.Add(location);
                }

                continue;
            }

            var rest = stripped;
            var labelMatch = labelPattern.Match(stripped);
            if (labelMatch.Success)
            {
                var label = labelMatch.Groups[1].Value.ToUpperInvariant();
                rest = labelMatch.Groups[2].Value;
                if (_symbols.ContainsKey(label))
                {
                    throw new AsmError($"Duplicate label '{label}'", lineNo);
                }

                _symbols[label] = location;
            }

            rest = rest.Trim();
            if (rest.Length == 0)
            {
                continue;
            }

            if (rest == "$")
            {
                break;
            }

            var parts = rest.Split(';')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0);

            foreach (var part in parts)
            {
                if (_origins.Count == 0)
                {
                    _origins.Add(location);
                }

                if (part == ".")
                {
                    _statements.Add(new EmitAddressStatement(location, lineNo, part, raw.TrimEnd('\n')));
                    location++;
                    continue;
                }

                if (part.Length >= 2 && part.StartsWith('"') && part.EndsWith('"'))
                {
                    var inner = part[1..^1];
                    if (inner.Length != 1)
                    {
                        throw new AsmError("Character literal must contain exactly one character", lineNo, part);
                    }

                    var value = inner[0] & 0x7F;
                    _statements.Add(new DataStatement(location, value, lineNo, part, raw.TrimEnd('\n')));
                    location++;
                    continue;
                }

                var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tokens.Length == 0)
                {
                    continue;
                }

                var upperTokens = tokens.Select(t => t.ToUpperInvariant()).ToArray();
                var op = upperTokens[0];

                if (op == "TEXT")
                {
                    var match = Regex.Match(part, "\"([^\"]*)\"");
                    if (!match.Success)
                    {
                        throw new AsmError("TEXT requires a quoted string", lineNo, part);
                    }

                    foreach (var ch in match.Groups[1].Value)
                    {
                        var value = ch & 0x7F;
                        _statements.Add(new DataStatement(location, value, lineNo, part, raw.TrimEnd('\n')));
                        location++;
                    }

                    continue;
                }

                if (PseudoOps.TryGetValue(op, out var pseudoValue))
                {
                    _statements.Add(new DataStatement(location, pseudoValue, lineNo, part, raw.TrimEnd('\n')));
                    location++;
                    continue;
                }

                if (MemrefOps.ContainsKey(op))
                {
                    var indirect = false;
                    string? operandToken;

                    if (upperTokens.Length >= 2 && upperTokens[1] == "I")
                    {
                        indirect = true;
                        operandToken = tokens.Length >= 3 ? tokens[2] : null;
                    }
                    else
                    {
                        operandToken = tokens.Length >= 2 ? tokens[1] : null;
                    }

                    if (operandToken is null)
                    {
                        throw new AsmError($"Missing operand for {op}", lineNo, part);
                    }

                    _statements.Add(new MemStatement(location, op, indirect, operandToken, lineNo, part, raw.TrimEnd('\n')));
                    location++;
                    continue;
                }

                if (op == "IOT")
                {
                    if (tokens.Length != 2)
                    {
                        throw new AsmError("IOT requires a single numeric operand", lineNo, part);
                    }

                    _statements.Add(new IotStatement(location, tokens[1], lineNo, part, raw.TrimEnd('\n')));
                    location++;
                    continue;
                }

                if (IotMnemonics.TryGetValue(op, out var iotValue))
                {
                    _statements.Add(new IotStatement(location, $"0o{Convert.ToString(iotValue, 8)}", lineNo, part, raw.TrimEnd('\n')));
                    location++;
                    continue;
                }

                if (upperTokens.Any(t => Group2Bits.ContainsKey(t) || t is "SNA" or "SPA" or "SZL") ||
                    upperTokens.All(t => Group1Bits.ContainsKey(t)))
                {
                    _statements.Add(new OperateStatement(location, upperTokens, lineNo, part, raw.TrimEnd('\n')));
                    location++;
                    continue;
                }

                if (TryParseNumber(tokens[0], out var literal))
                {
                    _statements.Add(new DataStatement(location, literal, lineNo, part, raw.TrimEnd('\n')));
                }
                else
                {
                    _statements.Add(new DataSymbolStatement(location, tokens[0], lineNo, part, raw.TrimEnd('\n')));
                }

                location++;
            }
        }
    }

    public Dictionary<int, int> SecondPass()
    {
        var memory = new Dictionary<int, int>();
        foreach (var stmt in _statements)
        {
            var word = AssembleStatement(stmt) & WordMask;
            memory[stmt.Address] = word;
        }

        return memory;
    }

    public (Dictionary<int, int> Memory, List<(Statement Statement, int? Word, AsmError? Error)> Rows, List<AsmError> Errors) AssembleListing()
    {
        var memory = new Dictionary<int, int>();
        var rows = new List<(Statement, int?, AsmError?)>();
        var errors = new List<AsmError>();

        foreach (var stmt in _statements)
        {
            try
            {
                var word = AssembleStatement(stmt) & WordMask;
                memory[stmt.Address] = word;
                rows.Add((stmt, word, null));
            }
            catch (AsmError ex)
            {
                errors.Add(ex);
                rows.Add((stmt, null, ex));
            }
        }

        return (memory, rows, errors);
    }

    public static List<string> WordsToSRecords(Dictionary<int, int> memory, int startAddress)
    {
        if (memory.Count == 0)
        {
            return new List<string>();
        }

        var byteMap = new Dictionary<int, int>();
        foreach (var (addr, wordValue) in memory)
        {
            var word = wordValue & WordMask;
            var byteAddr = addr * 2;
            byteMap[byteAddr] = word & 0xFF;
            byteMap[byteAddr + 1] = (word >> 8) & 0x0F;
        }

        var records = new List<string>();
        var sorted = byteMap.OrderBy(kvp => kvp.Key).ToList();
        const int maxBytesPerRecord = 32;
        int? currentStart = null;
        var currentBytes = new List<int>();
        int? previousAddr = null;

        void Emit()
        {
            if (currentStart is null || currentBytes.Count == 0)
            {
                return;
            }

            var address = currentStart.Value;
            var count = currentBytes.Count + 3;
            var recordBytes = new List<int> { count, (address >> 8) & 0xFF, address & 0xFF };
            recordBytes.AddRange(currentBytes);
            var checksum = unchecked(~recordBytes.Sum() & 0xFF);
            var dataField = string.Concat(currentBytes.Select(b => b.ToString("X2")));
            var record = $"S1{count:X2}{address:X4}{dataField}{checksum:X2}";
            records.Add(record);
            currentStart = null;
            currentBytes.Clear();
        }

        foreach (var (addr, value) in sorted)
        {
            if (currentStart is null)
            {
                currentStart = addr;
            }

            var contiguous = previousAddr is not null && addr == previousAddr + 1;
            var exceeds = currentBytes.Count >= maxBytesPerRecord;
            if (!contiguous || exceeds)
            {
                Emit();
                currentStart = addr;
            }

            currentBytes.Add(value & 0xFF);
            previousAddr = addr;
        }

        Emit();

        var startByteAddr = (startAddress & WordMask) * 2;
        const int startRecordCount = 3;
        var startChecksum = unchecked(~(startRecordCount + (startByteAddr >> 8) + (startByteAddr & 0xFF)) & 0xFF);
        records.Add($"S903{startByteAddr:X4}{startChecksum:X2}");
        return records;
    }

    public static void WriteSRecords(Pdp8Assembler assembler, Dictionary<int, int> memory, string outputPath)
    {
        if (memory.Count == 0)
        {
            throw new AsmError("No output generated; empty program?");
        }

        var startAddr = assembler._symbols.TryGetValue("START", out var sym) ? sym : memory.Keys.Min();
        var records = WordsToSRecords(memory, startAddr);
        File.WriteAllText(outputPath, string.Join(Environment.NewLine, records) + Environment.NewLine);
    }

    public static string RenderListing(string source, Pdp8Assembler assembler, IReadOnlyList<(Statement Statement, int? Word, AsmError? Error)> rows, IReadOnlyList<AsmError> errors)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var origins = assembler._origins.Count > 0 ? assembler._origins : new List<int> { 0 };
        var originText = string.Join(", ", origins.Select(o => FormatOctal(o, 4)));

        var header = new List<string>
        {
            "PDP-8 Assembly Listing",
            $"Source: {source}",
            $"Assembled: {timestamp}",
            $"Origins: {originText}",
            string.Empty,
            "ADDR  WORD  SYMBOL/OPCODE      ; SOURCE",
            "----  ----  ------------------  ----------------------------------------"
        };

        var body = new List<string>();
        foreach (var (stmt, word, error) in rows)
        {
            var wordField = word is null ? "????" : FormatOctal(word.Value, 4);
            var symbolField = stmt switch
            {
                MemStatement mem => mem.Opcode,
                OperateStatement op when op.Tokens.Count > 0 => op.Tokens[0],
                IotStatement => "IOT",
                DataSymbolStatement dataSym => dataSym.Symbol.ToUpperInvariant(),
                DataStatement data when !string.IsNullOrWhiteSpace(data.Text) => data.Text.Trim(),
                EmitAddressStatement => ". (emit addr)",
                _ => stmt.Text.Trim()
            };

            symbolField = symbolField.Length > 18 ? symbolField[..18] : symbolField;
            var sourceText = stmt.Raw.TrimEnd();
            var line = $"{FormatOctal(stmt.Address, 4)}  {wordField,4}  {symbolField,-18}  ; {sourceText}";
            if (error is not null)
            {
                line += $"  <<< ERROR: {error.Message}";
            }

            body.Add(line);
        }

        var totalsLine = $"Totals: {rows.Count(r => r.Word is not null)} words, {errors.Count} errors";
        var footer = new List<string> { string.Empty, totalsLine };
        if (errors.Count > 0)
        {
            footer.Add("Errors:");
            footer.AddRange(errors.Select(e => $"  - {e.Message}"));
        }

        return string.Join(Environment.NewLine, header.Concat(body).Concat(footer));
    }

    public static (string? SRecords, string? Listing, int? StartAddress, List<AsmError> Errors) AssembleSource(string source, bool includeListing = false)
    {
        var lines = source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var assembler = new Pdp8Assembler(lines);
        var listingRows = new List<(Statement, int?, AsmError?)>();
        var errors = new List<AsmError>();

        try
        {
            assembler.FirstPass();
        }
        catch (AsmError ex)
        {
            errors.Add(ex);
        }

        Dictionary<int, int> memory = new();
        if (errors.Count == 0)
        {
            (memory, listingRows, var passErrors) = assembler.AssembleListing();
            errors.AddRange(passErrors);
        }

        var listingText = includeListing ? RenderListing("<memory>", assembler, listingRows, errors) : null;

        if (errors.Count > 0)
        {
            return (null, listingText, null, errors);
        }

        if (memory.Count == 0)
        {
            var emptyError = new AsmError("No output generated; empty program?");
            return (null, listingText, null, new List<AsmError> { emptyError });
        }

        var startAddr = assembler._symbols.TryGetValue("START", out var start) ? start : memory.Keys.Min();
        var records = WordsToSRecords(memory, startAddr);
        var srecText = string.Join(Environment.NewLine, records) + Environment.NewLine;
        return (srecText, listingText, startAddr, new List<AsmError>());
    }

    public static void AssembleFile(string sourcePath, string? outputPath = null, bool emitListing = false, TextWriter? listingWriter = null)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(sourcePath);
        }
        catch (Exception ex)
        {
            throw new AsmError($"Unable to read {sourcePath}: {ex.Message}");
        }

        var assembler = new Pdp8Assembler(lines);
        var (memory, listingRows, errors) = assembler.AssembleListingWithFirstPass();

        if (emitListing)
        {
            var listing = RenderListing(sourcePath, assembler, listingRows, errors);
            (listingWriter ?? Console.Error).WriteLine(listing);
        }

        if (errors.Count > 0)
        {
            throw errors[0];
        }

        if (memory.Count == 0)
        {
            throw new AsmError("No output generated; empty program?");
        }

        var startAddr = assembler._symbols.TryGetValue("START", out var start) ? start : memory.Keys.Min();
        var records = WordsToSRecords(memory, startAddr);

        if (outputPath is null)
        {
            foreach (var record in records)
            {
                Console.WriteLine(record);
            }
        }
        else
        {
            File.WriteAllText(outputPath, string.Join(Environment.NewLine, records) + Environment.NewLine);
        }
    }

    private (Dictionary<int, int> Memory, List<(Statement Statement, int? Word, AsmError? Error)> Rows, List<AsmError> Errors) AssembleListingWithFirstPass()
    {
        var listingRows = new List<(Statement, int?, AsmError?)>();
        var errors = new List<AsmError>();

        try
        {
            FirstPass();
        }
        catch (AsmError ex)
        {
            errors.Add(ex);
            return (new Dictionary<int, int>(), listingRows, errors);
        }

        var result = AssembleListing();
        errors.AddRange(result.Errors);
        return (result.Memory, result.Rows, errors);
    }

    private int AssembleStatement(Statement stmt)
    {
        return stmt switch
        {
            DataStatement data => data.Value & WordMask,
            DataSymbolStatement dataSymbol => ResolveSymbol(dataSymbol.Symbol, dataSymbol.LineNumber, dataSymbol.Text, dataSymbol.Address) & WordMask,
            IotStatement iot => ResolveSymbol(iot.Token, iot.LineNumber, iot.Text, iot.Address) & WordMask,
            MemStatement mem => AssembleMem(mem),
            OperateStatement operate => AssembleOperate(operate),
            EmitAddressStatement emit => emit.Address & WordMask,
            _ => throw new AsmError($"Unhandled statement type '{stmt.Kind}'", stmt.LineNumber, stmt.Text)
        };
    }

    private int AssembleMem(MemStatement stmt)
    {
        var opcode = MemrefOps[stmt.Opcode];
        var operandAddr = ResolveSymbol(stmt.OperandToken, stmt.LineNumber, stmt.Text, stmt.Address);
        var currentPage = stmt.Address / PageSize;

        int pageBit;
        int offset;
        if (operandAddr < PageSize)
        {
            pageBit = 0;
            offset = operandAddr;
        }
        else
        {
            var operandPage = operandAddr / PageSize;
            if (operandPage != currentPage)
            {
                throw new AsmError($"Operand '{stmt.OperandToken}' out of range for direct addressing", stmt.LineNumber, stmt.Text);
            }

            pageBit = 1;
            offset = operandAddr & Octal("177");
        }

        var word = opcode | (stmt.Indirect ? Octal("400") : 0) | (pageBit << 7) | offset;
        return word & WordMask;
    }

    private int AssembleOperate(OperateStatement stmt)
    {
        var tokens = stmt.Tokens;
        var group1Possible = tokens.All(Group1Bits.ContainsKey);
        var group2Candidate = tokens.Any(t => Group2Bits.ContainsKey(t) || t is "SNA" or "SPA" or "SZL");

        if (group2Candidate && !group1Possible)
        {
            var bits = 0;
            foreach (var tok in tokens)
            {
                switch (tok)
                {
                    case "SNA":
                        bits |= Group2Bits["SZA"] | Octal("0010");
                        break;
                    case "SPA":
                        bits |= Group2Bits["SMA"] | Octal("0010");
                        break;
                    case "SZL":
                        bits |= Group2Bits["SNL"] | Octal("0010");
                        break;
                    case "SNS":
                        bits |= Octal("0010");
                        break;
                    default:
                        if (!Group2Bits.TryGetValue(tok, out var g2))
                        {
                            throw new AsmError($"Unsupported group 2 op '{tok}'", stmt.LineNumber, stmt.Text);
                        }

                        bits |= g2;
                        break;
                }
            }

            return (Octal("7400") | bits) & WordMask;
        }

        var group1Bits = 0;
        foreach (var tok in tokens)
        {
            if (!Group1Bits.TryGetValue(tok, out var g1))
            {
                throw new AsmError($"Unsupported group 1 op '{tok}'", stmt.LineNumber, stmt.Text);
            }

            group1Bits |= g1;
        }

        return (Octal("7000") | group1Bits) & WordMask;
    }

    private int ResolveSymbol(string token, int lineNo, string text, int? currentAddress = null)
    {
        if (token.StartsWith(".", StringComparison.Ordinal) && currentAddress is not null)
        {
            var relative = Regex.Match(token.Trim(), @"\.(?:([+-])([0-7]+))?");
            if (relative.Success)
            {
                var sign = relative.Groups[1].Value;
                var digits = relative.Groups[2].Value;
                var offset = string.IsNullOrEmpty(digits) ? 0 : Convert.ToInt32(digits, 8);
                if (sign == "-")
                {
                    offset = -offset;
                }

                return (currentAddress.Value + offset) & WordMask;
            }
        }

        var labelArith = Regex.Match(token.Trim(), @"([A-Za-z0-9_]+)\s*([+-])\s*([0-9]+)");
        if (labelArith.Success)
        {
            var label = labelArith.Groups[1].Value.ToUpperInvariant();
            if (!_symbols.TryGetValue(label, out var baseAddr))
            {
                throw new AsmError($"Unknown symbol '{label}'", lineNo, text);
            }

            var op = labelArith.Groups[2].Value;
            var offsetStr = labelArith.Groups[3].Value;
            int offset;
            try
            {
                offset = Convert.ToInt32(offsetStr, 8);
            }
            catch (FormatException)
            {
                offset = Convert.ToInt32(offsetStr, 10);
            }

            if (op == "-")
            {
                offset = -offset;
            }

            return (baseAddr + offset) & WordMask;
        }

        if (TryParseNumber(token, out var numeric))
        {
            return numeric & WordMask;
        }

        if (token.StartsWith("&", StringComparison.Ordinal))
        {
            var symbolName = token[1..].ToUpperInvariant();
            if (!_symbols.TryGetValue(symbolName, out var addr))
            {
                throw new AsmError($"Unknown symbol '{token[1..]}'", lineNo, text);
            }

            return addr & WordMask;
        }

        var name = token.ToUpperInvariant();
        if (!_symbols.TryGetValue(name, out var value))
        {
            throw new AsmError($"Unknown symbol '{token}'", lineNo, text);
        }

        return value & WordMask;
    }

    private static bool TryParseNumber(string token, out int value)
    {
        token = token.Trim();
        try
        {
            value = ParseNumber(token);
            return true;
        }
        catch (FormatException)
        {
        }
        catch (OverflowException)
        {
        }

        value = 0;
        return false;
    }

    private static int ParseNumber(string token)
    {
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToInt32(token[2..], 16) & WordMask;
        }

        if (token.StartsWith("#", StringComparison.Ordinal))
        {
            return Convert.ToInt32(token[1..], 10) & WordMask;
        }

        if (token.StartsWith("-", StringComparison.Ordinal))
        {
            var value = -Convert.ToInt32(token[1..], 8);
            return value & WordMask;
        }

        return Convert.ToInt32(token, 8) & WordMask;
    }

    private static string StripComment(string text)
    {
        var slashIndex = text.IndexOf('/');
        return slashIndex >= 0 ? text[..slashIndex] : text;
    }

    private void DumpPseudoOpcodeTable()
    {
        if (_verboseWriter is null)
        {
            return;
        }

        _verboseWriter.WriteLine("Pseudo-opcode table:");
        if (PseudoOps.Count == 0)
        {
            _verboseWriter.WriteLine("  (empty)");
            return;
        }

        foreach (var (key, value) in PseudoOps)
        {
            _verboseWriter.WriteLine($"  {key} = {FormatOctal(value, 4)}");
        }
    }

    private static string FormatOctal(int value, int width = 4)
    {
        var octal = Convert.ToString(value & WordMask, 8);
        return octal.PadLeft(width, '0');
    }

    private static int Octal(string value) => Convert.ToInt32(value, 8);
}
