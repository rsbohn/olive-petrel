namespace OlivePetrel;

public sealed class Rx8e
{
    public const int DriveCount = 2;
    private const int Rx01SectorBytes = 128;
    private const int Rx01Bytes = 77 * 26 * Rx01SectorBytes;
    private const int Rx02SectorBytes = 256;
    private const int Rx02Bytes = 77 * 26 * Rx02SectorBytes;

    private readonly DriveState[] _drives = new DriveState[DriveCount];

    private int _loadIndex;
    private int _pendingSector;
    private int _pendingTrack;
    private int _pendingUnit;
    private bool _pendingWrite;

    private readonly ushort[] _sectorWords = new ushort[128];
    private int _wordIndex;
    private int _wordsPerSector;
    private bool _transferReady;
    private bool _done;
    private bool _error;

    public bool Attach(int drive, string path, bool createIfMissing, out string? error)
    {
        if (drive < 0 || drive >= DriveCount)
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

            var size = fullPath.EndsWith(".rx02", StringComparison.OrdinalIgnoreCase) ? Rx02Bytes : Rx01Bytes;
            try
            {
                using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                stream.SetLength(size);
            }
            catch (Exception ex)
            {
                error = $"Create failed: {ex.Message}";
                return false;
            }
        }

        var info = new FileInfo(fullPath);
        var bytes = info.Length;
        var density = bytes >= Rx02Bytes ? RxDensity.Rx02 : RxDensity.Rx01;
        _drives[drive] = new DriveState(fullPath, density, bytes);
        error = null;
        return true;
    }

    public Rx8eDriveStatus GetStatus(int drive)
    {
        if (drive < 0 || drive >= DriveCount)
        {
            throw new ArgumentOutOfRangeException(nameof(drive), drive, "Invalid drive index.");
        }

        var state = _drives[drive];
        var attached = !string.IsNullOrEmpty(state.Path);
        return new Rx8eDriveStatus(attached, state.Path, state.Density, state.SizeBytes);
    }

    public bool TryReadSector(int drive, int track, int sector, Span<ushort> target, out string? error)
    {
        error = null;
        if (!TryValidateAccess(drive, track, sector, out var state, out error))
        {
            return false;
        }

        var words = WordsPerSector(state.Density);
        if (target.Length < words)
        {
            error = "Buffer too small for sector.";
            return false;
        }

        try
        {
            using var stream = new FileStream(state.Path!, FileMode.Open, FileAccess.Read, FileShare.Read);
            var offset = SectorOffset(state.Density, track, sector);
            stream.Position = offset;
            var bytes = new byte[SectorBytes(state.Density)];
            var read = stream.Read(bytes);
            if (read < bytes.Length)
            {
                error = "Short read.";
                return false;
            }

            UnpackWords(bytes, target, state.Density);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Read failed: {ex.Message}";
            return false;
        }
    }

    public bool TryWriteSector(int drive, int track, int sector, ReadOnlySpan<ushort> source, out string? error)
    {
        error = null;
        if (!TryValidateAccess(drive, track, sector, out var state, out error))
        {
            return false;
        }

        var words = WordsPerSector(state.Density);
        if (source.Length < words)
        {
            error = "Source too small for sector.";
            return false;
        }

        try
        {
            var bytes = new byte[SectorBytes(state.Density)];
            PackWords(source, bytes.AsSpan(), state.Density);
            using var stream = new FileStream(state.Path!, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            var offset = SectorOffset(state.Density, track, sector);
            stream.Position = offset;
            stream.Write(bytes);
            stream.Flush(true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Write failed: {ex.Message}";
            return false;
        }
    }

    // IOT handlers
    public void LoadCommand(ushort ac)
    {
        if (_loadIndex == 0)
        {
            _pendingUnit = (ac & 0x20) != 0 ? 1 : 0;
            _pendingSector = ac & 0x1F;
            _pendingWrite = (ac & 0x40) != 0;
            _loadIndex = 1;
            return;
        }

        _pendingTrack = ac & 0xFF;
        _loadIndex = 2;
    }

    public ushort InitializeAndReadStatus()
    {
        _done = false;
        _error = false;
        _transferReady = false;
        _wordIndex = 0;
        _wordsPerSector = 0;
        _loadIndex = 0;

        if (!TryValidateAccess(_pendingUnit, _pendingTrack, _pendingSector, out var state, out var error))
        {
            _error = true;
            return ComposeStatus();
        }

        _wordsPerSector = WordsPerSector(state.Density);

        if (_pendingWrite)
        {
            Array.Clear(_sectorWords, 0, _sectorWords.Length);
            _transferReady = true;
            return ComposeStatus();
        }

        if (!TryReadSector(_pendingUnit, _pendingTrack, _pendingSector, _sectorWords, out error))
        {
            _error = true;
            return ComposeStatus();
        }

        _transferReady = true;
        return ComposeStatus();
    }

    public ushort Transfer(ushort ac)
    {
        if (_error || !_transferReady || _wordsPerSector == 0)
        {
            return ac;
        }

        if (_pendingWrite)
        {
            _sectorWords[_wordIndex] = (ushort)(ac & 0x0FFF);
        }
        else
        {
            ac = _sectorWords[_wordIndex];
        }

        _wordIndex++;
        if (_wordIndex >= _wordsPerSector)
        {
            if (_pendingWrite)
            {
                if (!TryWriteSector(_pendingUnit, _pendingTrack, _pendingSector, _sectorWords, out _))
                {
                    _error = true;
                }
            }

            _transferReady = false;
            _done = true;
        }

        return ac;
    }

    public bool SkipOnTransferReady() => _transferReady;
    public bool SkipOnError() => _error;
    public bool SkipOnDone() => _done;

    public void Reset()
    {
        _loadIndex = 0;
        _pendingSector = 0;
        _pendingTrack = 0;
        _pendingUnit = 0;
        _pendingWrite = false;
        _wordIndex = 0;
        _wordsPerSector = 0;
        _transferReady = false;
        _done = false;
        _error = false;
    }

    private static int SectorOffset(RxDensity density, int track, int sector)
    {
        var bytes = SectorBytes(density);
        return (track * 26 + sector) * bytes;
    }

    private static int SectorBytes(RxDensity density) => density == RxDensity.Rx02 ? Rx02SectorBytes : Rx01SectorBytes;
    private static int WordsPerSector(RxDensity density) => density == RxDensity.Rx02 ? 128 : 64;

    private static void UnpackWords(ReadOnlySpan<byte> bytes, Span<ushort> words, RxDensity density)
    {
        var count = WordsPerSector(density);
        for (var w = 0; w < count; w++)
        {
            var byteIndex = (w * 3) / 2;
            if ((w & 1) == 0)
            {
                words[w] = (ushort)((bytes[byteIndex] | ((bytes[byteIndex + 1] & 0x0F) << 8)) & 0x0FFF);
            }
            else
            {
                words[w] = (ushort)((bytes[byteIndex] >> 4) | (bytes[byteIndex + 1] << 4));
                words[w] &= 0x0FFF;
            }
        }
    }

    private static void PackWords(ReadOnlySpan<ushort> words, Span<byte> bytes, RxDensity density)
    {
        bytes.Clear();
        var count = WordsPerSector(density);
        for (var w = 0; w < count; w++)
        {
            var word = (ushort)(words[w] & 0x0FFF);
            var byteIndex = (w * 3) / 2;
            if ((w & 1) == 0)
            {
                bytes[byteIndex] = (byte)(word & 0xFF);
                bytes[byteIndex + 1] = (byte)((bytes[byteIndex + 1] & 0xF0) | ((word >> 8) & 0x0F));
            }
            else
            {
                bytes[byteIndex] = (byte)((bytes[byteIndex] & 0x0F) | ((word & 0x0F) << 4));
                bytes[byteIndex + 1] = (byte)(word >> 4);
            }
        }
    }

    private bool TryValidateAccess(int drive, int track, int sector, out DriveState state, out string? error)
    {
        error = null;
        state = default;

        if (drive < 0 || drive >= DriveCount)
        {
            error = "Invalid drive.";
            return false;
        }

        state = _drives[drive];
        if (string.IsNullOrEmpty(state.Path))
        {
            error = "Drive not attached.";
            return false;
        }

        if (track < 0 || track >= 77)
        {
            error = "Invalid track.";
            return false;
        }

        if (sector < 0 || sector >= 26)
        {
            error = "Invalid sector.";
            return false;
        }

        return true;
    }

    private ushort ComposeStatus()
    {
        ushort status = 0;
        if (_done)
        {
            status |= 0x800; // bit 11
        }

        if (_error)
        {
            status |= 0x400; // bit 10
        }

        if (_transferReady)
        {
            status |= 0x200; // bit 9
        }

        return status;
    }

    private readonly record struct DriveState(string? Path, RxDensity Density, long SizeBytes);
}

public readonly record struct Rx8eDriveStatus(bool Attached, string? Path, RxDensity Density, long SizeBytes);

public enum RxDensity
{
    Rx01,
    Rx02
}
