namespace OlivePetrel;

public sealed class Pdp8
{
    public const int MemorySize = 4096;

    private static readonly ushort Kcf = OctalConstant("06031");
    private static readonly ushort Ksf = OctalConstant("06032");
    private static readonly ushort Krs = OctalConstant("06034");
    private static readonly ushort Krb = OctalConstant("06036");
    private static readonly ushort Tcf = OctalConstant("06041");
    private static readonly ushort Tsf = OctalConstant("06042");
    private static readonly ushort Tls = OctalConstant("06044");
    private static readonly ushort TlsClear = OctalConstant("06046");
    private static readonly ushort Lpcf = OctalConstant("06601");
    private static readonly ushort Lpsf = OctalConstant("06602");
    private static readonly ushort Lpt = OctalConstant("06604");
    private static readonly ushort LptClear = OctalConstant("06606");

    private readonly ushort[] _memory = new ushort[MemorySize];

    public ushort AC { get; private set; }
    public ushort MQ { get; private set; }
    public ushort PC { get; private set; }
    public ushort IR { get; private set; }
    public bool Link { get; private set; }
    public bool Halted { get; private set; }
    public LinePrinter? LinePrinter { get; set; }

    public void SetProgramCounter(ushort value)
    {
        PC = Mask12(value);
    }

    public void ClearHalt()
    {
        Halted = false;
    }

    public void Reset()
    {
        Array.Clear(_memory, 0, _memory.Length);
        AC = 0;
        MQ = 0;
        PC = 0;
        IR = 0;
        Link = false;
        Halted = false;
    }

    public ushort Read(int address)
    {
        ValidateAddress(address);
        return _memory[address];
    }

    public void Write(int address, ushort value)
    {
        ValidateAddress(address);
        _memory[address] = Mask12(value);
    }

    public int LoadImage(string path)
    {
        var lines = File.ReadAllLines(path);
        int address = 0;
        int loaded = 0;

        foreach (var rawLine in lines)
        {
            var line = StripComment(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (token.StartsWith("@", StringComparison.Ordinal))
                {
                    address = ParseOctal(token[1..]);
                    continue;
                }

                if (token.EndsWith(":", StringComparison.Ordinal))
                {
                    address = ParseOctal(token[..^1]);
                    continue;
                }

                var colonIndex = token.IndexOf(':');
                if (colonIndex > 0)
                {
                    address = ParseOctal(token[..colonIndex]);
                    var valuePart = token[(colonIndex + 1)..];
                    if (!string.IsNullOrEmpty(valuePart))
                    {
                        var inlineValue = ParseOctal(valuePart);
                        Write(address, (ushort)inlineValue);
                        address = (address + 1) & 0xFFF;
                        loaded++;
                    }

                    continue;
                }

                var value = ParseOctal(token);
                Write(address, (ushort)value);
                address = (address + 1) & 0xFFF;
                loaded++;
            }
        }

        return loaded;
    }

    public int Step()
    {
        if (Halted)
        {
            return 0;
        }

        IR = Read(PC);
        PC = (ushort)((PC + 1) & 0xFFF);

        var opcode = (IR >> 9) & 0x7;
        switch (opcode)
        {
            case 0: // AND
            {
                var ea = ResolveEffectiveAddress(IR);
                AC = Mask12((ushort)(AC & Read(ea)));
                return 1;
            }
            case 1: // TAD
            {
                var ea = ResolveEffectiveAddress(IR);
                var sum = AC + Read(ea);
                if (sum > 0xFFF)
                {
                    Link = !Link;
                }

                AC = Mask12((ushort)sum);
                return 1;
            }
            case 2: // ISZ
            {
                var ea = ResolveEffectiveAddress(IR);
                var value = (ushort)((Read(ea) + 1) & 0xFFF);
                Write(ea, value);
                if (value == 0)
                {
                    PC = (ushort)((PC + 1) & 0xFFF);
                }

                return 1;
            }
            case 3: // DCA
            {
                var ea = ResolveEffectiveAddress(IR);
                Write(ea, AC);
                AC = 0;
                return 1;
            }
            case 4: // JMS
            {
                var ea = ResolveEffectiveAddress(IR);
                Write(ea, PC);
                PC = (ushort)((ea + 1) & 0xFFF);
                return 1;
            }
            case 5: // JMP
            {
                var ea = ResolveEffectiveAddress(IR);
                PC = (ushort)ea;
                return 1;
            }
            case 6: // IOT
                return ExecuteIot(IR);
            case 7: // OPR
                return ExecuteOpr(IR);
            default:
                return 0;
        }
    }

    public int Run(int maxSteps)
    {
        int steps = 0;
        while (!Halted && steps < maxSteps)
        {
            Step();
            steps++;
        }

        return steps;
    }

    private static ushort Mask12(ushort value) => (ushort)(value & 0x0FFF);

    private static void ValidateAddress(int address)
    {
        if (address < 0 || address >= MemorySize)
        {
            throw new ArgumentOutOfRangeException(nameof(address), address, "Memory address out of range.");
        }
    }

    private static string StripComment(string line)
    {
        var commentIndex = line.IndexOf(';');
        if (commentIndex >= 0)
        {
            return line[..commentIndex];
        }

        commentIndex = line.IndexOf('#');
        if (commentIndex >= 0)
        {
            return line[..commentIndex];
        }

        return line;
    }

    private static int ParseOctal(string token)
    {
        return Convert.ToInt32(token, 8);
    }

    private static ushort OctalConstant(string token)
    {
        return (ushort)(ParseOctal(token) & 0x0FFF);
    }

    private int ResolveEffectiveAddress(ushort instruction)
    {
        var indirect = (instruction & 0x100) != 0;
        var zeroPage = (instruction & 0x80) == 0;
        var offset = instruction & 0x7F;
        var baseAddress = zeroPage ? 0 : (PC & 0xF80);
        var ea = (baseAddress | offset) & 0xFFF;

        if (!indirect)
        {
            return ea;
        }

        if (zeroPage && ea >= 0x08 && ea <= 0x0F)
        {
            var autoValue = (Read(ea) + 1) & 0xFFF;
            Write(ea, (ushort)autoValue);
        }

        return Read(ea);
    }

    private int ExecuteIot(ushort instruction)
    {
        if (instruction == Kcf)
        {
            return 1;
        }

        if (instruction == Ksf)
        {
            if (TryKeyAvailable())
            {
                PC = (ushort)((PC + 1) & 0xFFF);
            }

            return 1;
        }

        if (instruction == Krs || instruction == Krb)
        {
            if (TryReadKey(out var key))
            {
                AC = (ushort)((AC & 0xF00) | (key & 0xFF));
            }
            else
            {
                AC = (ushort)(AC & 0xF00);
            }

            return 1;
        }

        if (instruction == Tcf)
        {
            return 1;
        }

        if (instruction == Tsf)
        {
            PC = (ushort)((PC + 1) & 0xFFF);
            return 1;
        }

        if (instruction == Tls || instruction == TlsClear)
        {
            var ch = (char)(AC & 0xFF);
            Console.Write(ch);
            return 1;
        }

        if (instruction == Lpcf)
        {
            return 1;
        }

        if (instruction == Lpsf)
        {
            PC = (ushort)((PC + 1) & 0xFFF);
            return 1;
        }

        if (instruction == Lpt || instruction == LptClear)
        {
            var ch = (char)(AC & 0xFF);
            LinePrinter?.Write(ch);
            return 1;
        }

        return 1;
    }

    private int ExecuteOpr(ushort instruction)
    {
        var group2 = (instruction & 0x100) != 0;
        if (!group2)
        {
            return ExecuteOprGroup1(instruction);
        }

        var group3 = (instruction & 0x08) != 0;
        if (group3)
        {
            return ExecuteOprGroup3(instruction);
        }

        return ExecuteOprGroup2(instruction);
    }

    private int ExecuteOprGroup1(ushort instruction)
    {
        if ((instruction & 0x80) != 0)
        {
            AC = 0;
        }

        if ((instruction & 0x40) != 0)
        {
            Link = false;
        }

        if ((instruction & 0x20) != 0)
        {
            AC = Mask12((ushort)~AC);
        }

        if ((instruction & 0x10) != 0)
        {
            Link = !Link;
        }

        if ((instruction & 0x08) != 0)
        {
            var sum = AC + 1;
            if (sum > 0xFFF)
            {
                Link = !Link;
            }

            AC = Mask12((ushort)sum);
        }

        var rotate = instruction & 0x06;
        if (rotate != 0)
        {
            if (rotate == 0x06)
            {
                RotateRightTwice();
            }
            else if (rotate == 0x04)
            {
                RotateRight();
            }
            else if (rotate == 0x02)
            {
                RotateLeft();
            }
        }

        if ((instruction & 0x01) != 0)
        {
            AC = (ushort)(((AC & 0x3F) << 6) | ((AC >> 6) & 0x3F));
        }

        return 1;
    }

    private int ExecuteOprGroup2(ushort instruction)
    {
        if ((instruction & 0x80) != 0)
        {
            AC = 0;
        }

        var skip = false;
        if ((instruction & 0x40) != 0 && (AC & 0x800) != 0)
        {
            skip = true;
        }

        if ((instruction & 0x20) != 0 && AC == 0)
        {
            skip = true;
        }

        if ((instruction & 0x10) != 0 && Link)
        {
            skip = true;
        }

        if (skip)
        {
            PC = (ushort)((PC + 1) & 0xFFF);
        }

        if ((instruction & 0x04) != 0)
        {
            AC = (ushort)(AC | GetSwitchRegister());
        }

        if ((instruction & 0x02) != 0)
        {
            Halted = true;
        }

        return 1;
    }

    private int ExecuteOprGroup3(ushort instruction)
    {
        if ((instruction & 0x80) != 0)
        {
            AC = 0;
        }

        if ((instruction & 0x40) != 0)
        {
            AC = (ushort)(AC | MQ);
        }

        if ((instruction & 0x10) != 0)
        {
            MQ = AC;
            AC = 0;
        }

        return 1;
    }

    private void RotateLeft()
    {
        var combined = (Link ? 1 : 0) | (AC << 1);
        Link = (combined & 0x1000) != 0;
        AC = (ushort)(combined & 0xFFF);
    }

    private void RotateRight()
    {
        var combined = (Link ? 0x1000 : 0) | AC;
        Link = (combined & 1) != 0;
        AC = (ushort)((combined >> 1) & 0xFFF);
    }

    private void RotateRightTwice()
    {
        RotateRight();
        RotateRight();
    }

    private static ushort GetSwitchRegister()
    {
        return 0;
    }

    private static bool TryKeyAvailable()
    {
        try
        {
            return Console.KeyAvailable;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadKey(out int key)
    {
        key = 0;
        try
        {
            if (!Console.KeyAvailable)
            {
                return false;
            }

            key = Console.ReadKey(intercept: true).KeyChar;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ToOctal(int value, int width = 4)
    {
        var octal = Convert.ToString(value & 0xFFF, 8);
        return octal.PadLeft(width, '0');
    }
}
