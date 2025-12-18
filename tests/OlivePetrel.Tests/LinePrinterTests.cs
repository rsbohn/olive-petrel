using System;
using System.IO;
using OlivePetrel;
using Xunit;

namespace OlivePetrel.Tests
{
    public class LinePrinterTests
    {
        [Fact]
        public void Attach_Write_Detach_WritesFile()
        {
            var lp = new LinePrinter();
            var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                Assert.False(lp.Attached);

                string? err;
                var ok = lp.Attach(tmp, out err);
                Assert.True(ok, err);
                Assert.True(lp.Attached);
                Assert.NotNull(lp.Path);

                lp.Write('A');
                lp.Write('B');

                lp.Detach();
                Assert.False(lp.Attached);
                Assert.Null(lp.Path);

                var content = File.ReadAllText(tmp);
                Assert.Equal("AB", content);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        [Fact]
        public void Write_Without_Attach_DoesNotThrow()
        {
            var lp = new LinePrinter();
            Exception? ex = Record.Exception(() => lp.Write('X'));
            Assert.Null(ex);
        }
    }
}
