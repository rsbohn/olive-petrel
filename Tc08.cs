namespace OlivePetrel;

public sealed class Tc08
{
    public const int DriveCount = 2;
    public const int WordsPerBlock = 129;
    public const int DataWordsPerBlock = 128;

    private readonly string?[] _paths = new string?[DriveCount];
    private readonly long[] _sizes = new long[DriveCount];

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

        var info = new FileInfo(fullPath);
        _paths[driveIndex] = fullPath;
        _sizes[driveIndex] = info.Length;
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
