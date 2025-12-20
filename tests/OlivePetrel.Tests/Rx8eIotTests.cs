using System;
using System.IO;
using OlivePetrel;
using Xunit;

namespace OlivePetrel.Tests;

public class Rx8eIotTests
{
    [Fact]
    public void IotRead_LoadsSectorWordsIntoMemory()
    {
        var rx8e = new Rx8e();
        var machine = new Pdp8 { Rx8e = rx8e };
        var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".rx01");

        try
        {
            Assert.True(rx8e.Attach(0, tmp, createIfMissing: true, out var err), err);

            var sectorWords = new ushort[64];
            for (var i = 0; i < 8; i++)
            {
                sectorWords[i] = O("52");
            }

            Assert.True(rx8e.TryWriteSector(0, 0, 1, sectorWords, out err), err);

            LoadIotReadProgram(machine);
            machine.SetProgramCounter(O("200"));

            for (var i = 0; i < 2000 && !machine.Halted; i++)
            {
                machine.Step();
            }

            Assert.True(machine.Halted);
            for (var i = 0; i < 8; i++)
            {
                Assert.Equal(O("52"), machine.Read(O("2000") + i));
            }
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private static void LoadIotReadProgram(Pdp8 machine)
    {
        var program = new[]
        {
            O("7300"), // CLA CLL
            O("1020"), // TAD 0020 (sector command)
            O("6751"), // RxLcd
            O("7300"), // CLA CLL
            O("1021"), // TAD 0021 (track)
            O("6751"), // RxLcd
            O("6756"), // RxIntr
            O("6752"), // RxXdr
            O("3410"), // DCA I 0010 (auto-increment pointer to 2000)
            O("6752"),
            O("3410"),
            O("6752"),
            O("3410"),
            O("6752"),
            O("3410"),
            O("6752"),
            O("3410"),
            O("6752"),
            O("3410"),
            O("6752"),
            O("3410"),
            O("6752"),
            O("3410"),
            O("7402")  // HLT
        };

        var pc = O("200");
        for (var i = 0; i < program.Length; i++)
        {
            machine.Write(pc + i, program[i]);
        }

        machine.Write(O("10"), O("1777")); // Auto-increment pointer: 0010 pre-increments to 2000
        machine.Write(O("20"), O("1"));    // sector 1, unit 0, read
        machine.Write(O("21"), O("0"));    // track 0
    }

    private static ushort O(string value) => Convert.ToUInt16(value, 8);
}
