namespace OlivePetrel;

public sealed class Tc08
{
    public const int DriveCount = 2;
    public const int WordsPerBlock = 129;
    public const int DataWordsPerBlock = 128;

    private readonly string?[] _paths = new string?[DriveCount];
    private readonly long[] _sizes = new long[DriveCount];
    private readonly ushort[]?[] _srecImages = new ushort[]?[DriveCount];
    private readonly bool[] _srecReadOnly = new bool[DriveCount];

    public bool Attach(int driveIndex, string path, bool createIfMissing, out string? error)
    {
        if (driveIndex < 0 || driveIndex >= DriveCount)
        {
            error = "Invalid drive index.";
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            if (!createIfMissing)
            {
                error = $"File not found: {fullPath}";
                return false;
            }

            try
            {
                using var _ = new FileStream(fullPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            }
            catch (Exception ex)
            {
                error = $"Create failed: {ex.Message}";
                return false;
            }
        }

        if (LooksLikeSRecord(fullPath))
        {
            if (!TryLoadSRecord(fullPath, out var image, out error))
            {
                return false;
            }

            _srecImages[driveIndex] = image;
            _srecReadOnly[driveIndex] = true;
            _paths[driveIndex] = fullPath;
            _sizes[driveIndex] = image?.Length * sizeof(ushort) ?? 0;
            return true;
        }

        var info = new FileInfo(fullPath);
        _paths[driveIndex] = fullPath;
        _sizes[driveIndex] = info.Length;
        _srecImages[driveIndex] = null;
        _srecReadOnly[driveIndex] = false;
        error = null;
        return true;
    }

    public Tc08DriveStatus GetDriveStatus(int driveIndex)
    {
        if (driveIndex < 0 || driveIndex >= DriveCount)
        {
            throw new ArgumentOutOfRangeException(nameof(driveIndex), driveIndex, "Invalid drive index.");
        }

        var path = _paths[driveIndex];
        var attached = !string.IsNullOrEmpty(path);
        var size = attached ? _sizes[driveIndex] : 0;
        return new Tc08DriveStatus(attached, path, size);
    }

    public bool TryReadBlock(int driveIndex, int block, Span<ushort> words, out string? error)
    {
        if (driveIndex < 0 || driveIndex >= DriveCount)
        {
            error = "Invalid drive index.";
            return false;
        }

        if (block < 0)
        {
            error = "Invalid block number.";
            return false;
        }

        if (words.Length < WordsPerBlock)
        {
            error = "Buffer too small.";
            return false;
        }

        if (_srecImages[driveIndex] is { } srec)
        {
            var start = block * WordsPerBlock;
            for (var i = 0; i < WordsPerBlock; i++)
            {
                var idx = start + i;
                words[i] = idx < srec.Length ? srec[idx] : (ushort)0;
            }

            error = null;
            return true;
        }

        var path = _paths[driveIndex];
        if (string.IsNullOrEmpty(path))
        {
            error = "Drive not attached.";
            return false;
        }

        var offset = (long)block * WordsPerBlock * sizeof(ushort);
        if (offset < 0)
        {
            error = "Invalid block offset.";
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (offset + WordsPerBlock * sizeof(ushort) > stream.Length)
            {
                error = "Block beyond end of tape.";
                return false;
            }

            stream.Position = offset;
            using var reader = new BinaryReader(stream);
            for (var i = 0; i < WordsPerBlock; i++)
            {
                words[i] = (ushort)(reader.ReadUInt16() & 0x0FFF);
            }
        }
        catch (Exception ex)
        {
            error = $"Read failed: {ex.Message}";
            return false;
        }

        error = null;
        return true;
    }

    public bool TryWriteBlock(int driveIndex, int block, ReadOnlySpan<ushort> words, out string? error)
    {
        if (driveIndex < 0 || driveIndex >= DriveCount)
        {
            error = "Invalid drive index.";
            return false;
        }

        if (block < 0)
        {
            error = "Invalid block number.";
            return false;
        }

        if (words.Length < WordsPerBlock)
        {
            error = "Buffer too small.";
            return false;
        }

        if (_srecImages[driveIndex] is not null)
        {
            error = "Image is read-only (S-record).";
            return false;
        }

        var path = _paths[driveIndex];
        if (string.IsNullOrEmpty(path))
        {
            error = "Drive not attached.";
            return false;
        }

        var offset = (long)block * WordsPerBlock * sizeof(ushort);
        if (offset < 0)
        {
            error = "Invalid block offset.";
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            stream.Position = offset;
            using var writer = new BinaryWriter(stream);
            for (var i = 0; i < WordsPerBlock; i++)
            {
                var word = i == DataWordsPerBlock ? 0 : words[i];
                writer.Write((ushort)(word & 0x0FFF));
            }
        }
        catch (Exception ex)
        {
            error = $"Write failed: {ex.Message}";
            return false;
        }

        try
        {
            var info = new FileInfo(path);
            _sizes[driveIndex] = info.Length;
        }
        catch
        {
            _sizes[driveIndex] = 0;
        }

        error = null;
        return true;
    }

    private static bool LooksLikeSRecord(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                line = line.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                return line.Length > 1 && line[0] == 'S' && char.IsDigit(line[1]);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryLoadSRecord(string path, out ushort[]? image, out string? error)
    {
        image = null;
        error = null;
        List<string> lines;
        try
        {
            lines = File.ReadAllLines(path).ToList();
        }
        catch (Exception ex)
        {
            error = $"Unable to read {path}: {ex.Message}";
            return false;
        }

        var byteMap = new Dictionary<int, byte>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line[0] != 'S')
            {
                continue;
            }

            if (line.Length < 4)
            {
                continue;
            }

            var type = line[1];
            var count = ParseHexByte(line, 2);
            var address = ParseHexWord(line, 4);

            if (type != '1')
            {
                continue;
            }

            var dataByteCount = count - 3;
            var expectedLength = 8 + dataByteCount * 2 + 2;
            if (line.Length < expectedLength)
            {
                error = $"Truncated S-record data in '{line}'";
                return false;
            }

            var dataBytes = ParseHexBytes(line, 8, dataByteCount);
            var checksum = ParseHexByte(line, 8 + dataByteCount * 2);
            if (!VerifySRecordChecksum(count, address, dataBytes, checksum))
            {
                error = $"Checksum mismatch in '{line}'";
                return false;
            }

            for (var i = 0; i < dataBytes.Count; i++)
            {
                byteMap[address + i] = dataBytes[i];
            }
        }

        if (byteMap.Count == 0)
        {
            error = "No data found in S-record image.";
            return false;
        }

        var maxByteAddr = byteMap.Keys.Max();
        var wordCount = (maxByteAddr / 2) + 1;
        image = new ushort[wordCount];
        foreach (var (addr, value) in byteMap)
        {
            var wordIndex = addr / 2;
            if ((addr & 1) == 0)
            {
                image[wordIndex] |= value;
            }
            else
            {
                image[wordIndex] |= (ushort)((value & 0x0F) << 8);
            }
        }

        return true;
    }

    private static byte ParseHexByte(string text, int offset)
    {
        return byte.Parse(text.AsSpan(offset, 2), System.Globalization.NumberStyles.HexNumber);
    }

    private static int ParseHexWord(string text, int offset)
    {
        var high = ParseHexByte(text, offset);
        var low = ParseHexByte(text, offset + 2);
        return (high << 8) | low;
    }

    private static List<byte> ParseHexBytes(string text, int offset, int count)
    {
        var bytes = new List<byte>(count);
        for (var i = 0; i < count; i++)
        {
            bytes.Add(ParseHexByte(text, offset + i * 2));
        }

        return bytes;
    }

    private static bool VerifySRecordChecksum(int count, int address, IReadOnlyCollection<byte> dataBytes, byte checksum)
    {
        var sum = count + ((address >> 8) & 0xFF) + (address & 0xFF);
        foreach (var b in dataBytes)
        {
            sum += b;
        }

        var computed = unchecked((byte)~sum);
        return computed == checksum;
    }
}

public readonly struct Tc08DriveStatus
{
    public Tc08DriveStatus(bool attached, string? path, long sizeBytes)
    {
        Attached = attached;
        Path = path;
        SizeBytes = sizeBytes;
    }

    public bool Attached { get; }
    public string? Path { get; }
    public long SizeBytes { get; }
}
