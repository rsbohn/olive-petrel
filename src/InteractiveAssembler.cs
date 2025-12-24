using System.Text.RegularExpressions;

namespace OlivePetrel;

/// <summary>
/// Interactive assembler for PDP-8 assembly language.
/// Provides a REPL-style interface for entering assembly instructions one line at a time.
/// </summary>
public sealed class InteractiveAssembler
{
    private const int WordMask = 0x0FFF;
    private const int PageSize = 0x80; // 0200 octal
    
    private int _currentAddress;
    private readonly Dictionary<string, int> _symbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, int> _memory = new();
    private readonly Pdp8 _machine;
    
    // Reuse existing opcode definitions from Pdp8Assembler
    private static readonly Dictionary<string, int> MemrefOps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AND"] = Convert.ToInt32("0000", 8),
        ["TAD"] = Convert.ToInt32("1000", 8),
        ["ISZ"] = Convert.ToInt32("2000", 8),
        ["DCA"] = Convert.ToInt32("3000", 8),
        ["JMS"] = Convert.ToInt32("4000", 8),
        ["JMP"] = Convert.ToInt32("5000", 8)
    };

    private static readonly Dictionary<string, int> Group1Bits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CLA"] = Convert.ToInt32("0200", 8),
        ["CLL"] = Convert.ToInt32("0100", 8),
        ["CMA"] = Convert.ToInt32("0040", 8),
        ["CML"] = Convert.ToInt32("0020", 8),
        ["RAR"] = Convert.ToInt32("0010", 8),
        ["RAL"] = Convert.ToInt32("0004", 8),
        ["RTR"] = Convert.ToInt32("0012", 8),
        ["RTL"] = Convert.ToInt32("0006", 8),
        ["BSW"] = Convert.ToInt32("0002", 8),
        ["IAC"] = Convert.ToInt32("0001", 8)
    };

    private static readonly Dictionary<string, int> Group2Bits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SMA"] = Convert.ToInt32("0100", 8),
        ["SZA"] = Convert.ToInt32("0040", 8),
        ["SNL"] = Convert.ToInt32("0020", 8),
        ["SPA"] = Convert.ToInt32("0110", 8),
        ["SNA"] = Convert.ToInt32("0050", 8),
        ["SZL"] = Convert.ToInt32("0030", 8),
        ["CLA"] = Convert.ToInt32("0200", 8),
        ["OSR"] = Convert.ToInt32("0004", 8),
        ["HLT"] = Convert.ToInt32("0002", 8),
        ["ION"] = Convert.ToInt32("0001", 8),
        ["IOFF"] = Convert.ToInt32("0000", 8)
    };

    public InteractiveAssembler(Pdp8 machine, int startAddress = 0x80)
    {
        _machine = machine;
        _currentAddress = startAddress;
        
        // Initialize with common pseudo-ops from Pdp8Assembler
        foreach (var (key, value) in Pdp8Assembler.PseudoOps)
        {
            _symbols[key] = value;
        }
    }

    public int CurrentAddress => _currentAddress;
    public IReadOnlyDictionary<string, int> Symbols => _symbols;
    public IReadOnlyDictionary<int, int> Memory => _memory;
    public bool HasUnloadedCode { get; private set; }

    /// <summary>
    /// Parse and assemble a single line of input.
    /// Returns the assembled word and a disassembly string.
    /// </summary>
    public (int Word, string Disassembly) ParseAndAssemble(string line)
    {
        line = StripComment(line).Trim();
        
        if (string.IsNullOrEmpty(line))
        {
            throw new AsmError("Empty input");
        }

        // Check for label definition
        var labelMatch = Regex.Match(line, @"^([A-Za-z0-9_]+),\s*(.*)");
        if (labelMatch.Success)
        {
            var label = labelMatch.Groups[1].Value.ToUpperInvariant();
            var rest = labelMatch.Groups[2].Value.Trim();
            
            if (_symbols.ContainsKey(label) && !Pdp8Assembler.PseudoOps.ContainsKey(label))
            {
                throw new AsmError($"Duplicate label '{label}'");
            }
            
            _symbols[label] = _currentAddress;
            
            if (string.IsNullOrEmpty(rest))
            {
                // Label only, no instruction
                return (_currentAddress, $"{label}:");
            }
            
            line = rest;
        }

        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            throw new AsmError("No instruction");
        }

        var upperTokens = tokens.Select(t => t.ToUpperInvariant()).ToArray();
        var op = upperTokens[0];

        // Check for character literal
        if (line.Length >= 2 && line.StartsWith('"') && line.EndsWith('"'))
        {
            var inner = line[1..^1];
            if (inner.Length != 1)
            {
                throw new AsmError("Character literal must contain exactly one character");
            }
            var value = inner[0] & 0x7F;
            return (value, $"\"{inner[0]}\"");
        }

        // Check for pseudo-op or symbol
        if (_symbols.TryGetValue(op, out var symbolValue))
        {
            return (symbolValue, op);
        }

        // Check for numeric constant
        if (TryParseNumber(op, out var constant))
        {
            return (constant, FormatOctal(constant, 4));
        }

        // Check for memory reference instruction
        if (MemrefOps.TryGetValue(op, out var memrefBase))
        {
            return AssembleMemref(memrefBase, tokens, upperTokens);
        }

        // Check for IOT instruction
        if (op.StartsWith("IOT", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 2)
            {
                throw new AsmError("IOT requires device code");
            }
            var deviceCode = ParseOperand(tokens[1]);
            return (deviceCode, $"IOT {FormatOctal(deviceCode, 4)}");
        }

        // Check for numeric IOT (6xxx)
        if (TryParseNumber(op, out var iotCode) && (iotCode & Convert.ToInt32("7000", 8)) == Convert.ToInt32("6000", 8))
        {
            return (iotCode, $"IOT {FormatOctal(iotCode, 4)}");
        }

        // Check for operate instruction (Group 1 or Group 2)
        if (IsOperateInstruction(upperTokens))
        {
            return AssembleOperate(upperTokens);
        }

        throw new AsmError($"Unknown instruction or symbol '{op}'");
    }

    /// <summary>
    /// Store the assembled word at the current address and increment.
    /// </summary>
    public void StoreAndAdvance(int word)
    {
        _memory[_currentAddress] = word & WordMask;
        _currentAddress = (_currentAddress + 1) & WordMask;
        HasUnloadedCode = true;
    }

    /// <summary>
    /// Set the current address.
    /// </summary>
    public void SetAddress(int address)
    {
        _currentAddress = address & WordMask;
    }

    /// <summary>
    /// Load assembled memory into the PDP-8 machine.
    /// </summary>
    public void LoadIntoMachine()
    {
        foreach (var (addr, word) in _memory)
        {
            _machine.Write(addr, (ushort)word);
        }
        HasUnloadedCode = false;
    }

    /// <summary>
    /// Clear all assembled memory and symbols (except built-in pseudo-ops).
    /// </summary>
    public void Clear()
    {
        _memory.Clear();
        _symbols.Clear();
        
        // Restore pseudo-ops
        foreach (var (key, value) in Pdp8Assembler.PseudoOps)
        {
            _symbols[key] = value;
        }
    }

    /// <summary>
    /// List assembled memory in a range.
    /// </summary>
    public void ListMemory(int? startAddr = null, int? endAddr = null)
    {
        var start = startAddr ?? _memory.Keys.DefaultIfEmpty(0).Min();
        var end = endAddr ?? _memory.Keys.DefaultIfEmpty(0).Max();

        Console.WriteLine($"Memory [{FormatOctal(start, 4)}-{FormatOctal(end, 4)}]:");
        
        for (var addr = start; addr <= end; addr++)
        {
            if (_memory.TryGetValue(addr, out var word))
            {
                var disasm = DisassembleWord(word);
                Console.WriteLine($"{FormatOctal(addr, 4)}: {FormatOctal(word, 4)} {disasm}");
            }
        }
    }

    /// <summary>
    /// Display current symbols.
    /// </summary>
    public void ListSymbols()
    {
        if (_symbols.Count == 0)
        {
            Console.WriteLine("No symbols defined.");
            return;
        }

        Console.WriteLine("Symbols:");
        foreach (var (name, value) in _symbols.OrderBy(s => s.Value))
        {
            Console.WriteLine($"  {name,-12} = {FormatOctal(value, 4)}");
        }
    }

    private (int Word, string Disassembly) AssembleMemref(int baseOp, string[] tokens, string[] upperTokens)
    {
        var indirect = false;
        string operandToken;

        if (upperTokens.Length >= 2 && upperTokens[1] == "I")
        {
            indirect = true;
            operandToken = tokens.Length >= 3 ? tokens[2] : throw new AsmError("Missing operand after I");
        }
        else
        {
            operandToken = tokens.Length >= 2 ? tokens[1] : throw new AsmError("Missing operand");
        }

        var operand = ParseOperand(operandToken);
        var word = baseOp;

        if (indirect)
        {
            word |= Convert.ToInt32("0400", 8);
        }

        // Calculate page addressing
        var currentPage = _currentAddress & Convert.ToInt32("7600", 8);
        var targetPage = operand & Convert.ToInt32("7600", 8);
        
        if (targetPage == 0)
        {
            // Zero page
            word |= operand & Convert.ToInt32("177", 8);
        }
        else if (targetPage == currentPage)
        {
            // Current page
            word |= Convert.ToInt32("0200", 8) | (operand & Convert.ToInt32("177", 8));
        }
        else
        {
            throw new AsmError($"Address {FormatOctal(operand, 4)} is not on current page or zero page");
        }

        var disasm = $"{upperTokens[0]}{(indirect ? " I" : "")} {FormatOctal(operand, 4)}";
        return (word, disasm);
    }

    private (int Word, string Disassembly) AssembleOperate(string[] tokens)
    {
        // Check if this is Group 2 (has skip conditions)
        var isGroup2 = tokens.Any(t => Group2Bits.ContainsKey(t) && t != "CLA");

        if (isGroup2)
        {
            var word = Convert.ToInt32("7400", 8); // Group 2 base
            var mnemonics = new List<string>();

            foreach (var token in tokens)
            {
                if (Group2Bits.TryGetValue(token, out var bits))
                {
                    word |= bits;
                    mnemonics.Add(token);
                }
                else
                {
                    throw new AsmError($"Invalid Group 2 operate instruction '{token}'");
                }
            }

            return (word, string.Join(" ", mnemonics));
        }
        else
        {
            // Group 1
            var word = Convert.ToInt32("7000", 8); // Group 1 base
            var mnemonics = new List<string>();

            foreach (var token in tokens)
            {
                if (Group1Bits.TryGetValue(token, out var bits))
                {
                    word |= bits;
                    mnemonics.Add(token);
                }
                else
                {
                    throw new AsmError($"Invalid Group 1 operate instruction '{token}'");
                }
            }

            return (word, string.Join(" ", mnemonics));
        }
    }

    private bool IsOperateInstruction(string[] tokens)
    {
        return tokens.Length > 0 && (
            tokens.All(t => Group1Bits.ContainsKey(t)) ||
            tokens.All(t => Group2Bits.ContainsKey(t) || t == "CLA")
        );
    }

    private int ParseOperand(string token)
    {
        token = token.Trim();

        // Check for label arithmetic (e.g., LABEL+1, LABEL-2)
        var labelArith = Regex.Match(token, @"([A-Za-z0-9_]+)\s*([+-])\s*([0-9]+)");
        if (labelArith.Success)
        {
            var label = labelArith.Groups[1].Value.ToUpperInvariant();
            if (!_symbols.TryGetValue(label, out var baseAddr))
            {
                throw new AsmError($"Unknown symbol '{label}'");
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

        // Check for numeric constant
        if (TryParseNumber(token, out var numeric))
        {
            return numeric & WordMask;
        }

        // Check for address-of operator (&SYMBOL)
        if (token.StartsWith("&", StringComparison.Ordinal))
        {
            var symbolName = token[1..].ToUpperInvariant();
            if (!_symbols.TryGetValue(symbolName, out var addr))
            {
                throw new AsmError($"Unknown symbol '{token[1..]}'");
            }
            return addr & WordMask;
        }

        // Try as symbol
        var name = token.ToUpperInvariant();
        if (!_symbols.TryGetValue(name, out var value))
        {
            throw new AsmError($"Unknown symbol '{token}'");
        }

        return value & WordMask;
    }

    private static bool TryParseNumber(string token, out int value)
    {
        try
        {
            value = ParseNumber(token);
            return true;
        }
        catch
        {
            value = 0;
            return false;
        }
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

    private static string FormatOctal(int value, int width = 4)
    {
        var octal = Convert.ToString(value & WordMask, 8);
        return octal.PadLeft(width, '0');
    }

    private string DisassembleWord(int word)
    {
        word &= WordMask;

        // Check for IOT (6xxx)
        if ((word & Convert.ToInt32("7000", 8)) == Convert.ToInt32("6000", 8))
        {
            return $"IOT {FormatOctal(word, 4)}";
        }

        // Check for Operate Group 1 (7000-7377)
        if ((word & Convert.ToInt32("7400", 8)) == Convert.ToInt32("7000", 8))
        {
            var mnemonics = new List<string>();
            foreach (var (name, bits) in Group1Bits)
            {
                if ((word & bits) == bits && bits != 0)
                {
                    mnemonics.Add(name);
                }
            }
            return mnemonics.Count > 0 ? string.Join(" ", mnemonics) : "NOP";
        }

        // Check for Operate Group 2 (7400-7777)
        if ((word & Convert.ToInt32("7400", 8)) == Convert.ToInt32("7400", 8))
        {
            var mnemonics = new List<string>();
            foreach (var (name, bits) in Group2Bits)
            {
                if ((word & bits) == bits && bits != 0)
                {
                    mnemonics.Add(name);
                }
            }
            return mnemonics.Count > 0 ? string.Join(" ", mnemonics) : "NOP";
        }

        // Memory reference instruction
        var opcode = word & Convert.ToInt32("7000", 8);
        var indirect = (word & Convert.ToInt32("0400", 8)) != 0;
        var page = (word & Convert.ToInt32("0200", 8)) != 0;
        var offset = word & Convert.ToInt32("177", 8);

        string opName = opcode switch
        {
            _ when opcode == Convert.ToInt32("0000", 8) => "AND",
            _ when opcode == Convert.ToInt32("1000", 8) => "TAD",
            _ when opcode == Convert.ToInt32("2000", 8) => "ISZ",
            _ when opcode == Convert.ToInt32("3000", 8) => "DCA",
            _ when opcode == Convert.ToInt32("4000", 8) => "JMS",
            _ when opcode == Convert.ToInt32("5000", 8) => "JMP",
            _ => "???"
        };

        var addr = page ? (_currentAddress & Convert.ToInt32("7600", 8)) | offset : offset;
        return $"{opName}{(indirect ? " I" : "")} {FormatOctal(addr, 4)}";
    }
}
