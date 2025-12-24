using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OlivePetrel;

/// <summary>
/// Minimal TNFS client for UDP transport (port 16384 by default).
/// Implements MOUNT/UMOUNT and exposes a send helper for future commands.
/// Based on docs/tnfs-protocol.md (July 15, 2020).
/// </summary>
public sealed class TnfsClient : IAsyncDisposable
{
    private readonly UdpClient _udp;
    private readonly IPEndPoint _remote;
    private readonly TimeSpan _receiveTimeout;

    private ushort _connectionId;
    private byte _sequence;
    private ushort _serverVersion;
    private ushort _minRetryMs;
    private bool _mounted;

    public const ushort O_RDONLY = 0x0001;
    public const ushort O_WRONLY = 0x0002;
    public const ushort O_RDWR = 0x0003;
    public const ushort O_APPEND = 0x0008;
    public const ushort O_CREAT = 0x0100;
    public const ushort O_TRUNC = 0x0200;
    public const ushort O_EXCL = 0x0400;

    public TnfsClient(string host, int port = 16384, TimeSpan? receiveTimeout = null)
    {
        _remote = new IPEndPoint(Dns.GetHostAddresses(host)[0], port);
        _udp = new UdpClient();
        _udp.Connect(_remote);
        _receiveTimeout = receiveTimeout ?? TimeSpan.FromSeconds(2);
    }

    public bool IsMounted => _mounted;
    public ushort ConnectionId => _connectionId;
    public ushort ServerVersion => _serverVersion;
    public ushort MinRetryMilliseconds => _minRetryMs;

    public async Task<TnfsMountResult> MountAsync(
        byte versionMajor,
        byte versionMinor,
        string mountPath,
        string? userId = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var version = (ushort)((versionMajor << 8) | versionMinor);
        var writer = new ArrayBufferWriter<byte>(64);
        WriteHeader(writer, connectionId: 0, command: TnfsCommand.Mount);
        WriteUInt16(writer, version);
        WriteCString(writer, mountPath);
        WriteCString(writer, userId ?? string.Empty);
        WriteCString(writer, password ?? string.Empty);

        var response = await SendAndReceiveAsync(writer.WrittenMemory, cancellationToken).ConfigureAwait(false);

        if (response.Length < 9)
        {
            throw new InvalidOperationException("TNFS mount response too short.");
        }

        var status = response.Span[4];
        if (status != 0)
        {
            throw new TnfsException($"TNFS mount failed with status 0x{status:X2}", status);
        }

        _connectionId = ReadUInt16(response.Span);
        _serverVersion = ReadUInt16(response.Span[5..]);
        _minRetryMs = ReadUInt16(response.Span[7..]);
        _mounted = true;
        return new TnfsMountResult(_connectionId, _serverVersion, _minRetryMs);
    }

    public async Task UmountAsync(CancellationToken cancellationToken = default)
    {
        EnsureMounted();
        var writer = new ArrayBufferWriter<byte>(4);
        WriteHeader(writer, _connectionId, TnfsCommand.Umount);
        var response = await SendAndReceiveAsync(writer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        var status = response.Length >= 5 ? response.Span[4] : byte.MaxValue;
        if (status != 0)
        {
            throw new TnfsException($"TNFS umount failed with status 0x{status:X2}", status);
        }

        _mounted = false;
        _connectionId = 0;
    }

    /// <summary>
    /// Sends a raw TNFS command with the current connection/session.
    /// Caller owns parsing the response payload.
    /// </summary>
    public async Task<ReadOnlyMemory<byte>> SendCommandAsync(
        TnfsCommand command,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (command != TnfsCommand.Mount)
        {
            EnsureMounted();
        }

        var writer = new ArrayBufferWriter<byte>(4 + payload.Length);
        WriteHeader(writer, _connectionId, command);
        var span = writer.GetSpan(payload.Length);
        payload.Span.CopyTo(span);
        writer.Advance(payload.Length);
        return await SendAndReceiveAsync(writer.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte> OpenDirAsync(string path, CancellationToken cancellationToken = default)
    {
        var writer = new ArrayBufferWriter<byte>(4 + Encoding.ASCII.GetByteCount(path) + 1);
        WriteHeader(writer, _connectionId, TnfsCommand.OpenDir);
        WriteCString(writer, path);

        var response = await SendAndReceiveAsync(writer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        ValidateStatus(response, TnfsCommand.OpenDir);
        if (response.Length < 6)
        {
            throw new InvalidOperationException("TNFS OPENDIR response too short.");
        }

        return response.Span[5];
    }

    public async Task<string?> ReadDirEntryAsync(byte handle, CancellationToken cancellationToken = default)
    {
        var writer = new ArrayBufferWriter<byte>(5);
        WriteHeader(writer, _connectionId, TnfsCommand.ReadDir);
        writer.GetSpan(1)[0] = handle;
        writer.Advance(1);

        var response = await SendAndReceiveAsync(writer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        var status = GetStatus(response);
        if (status == TnfsStatus.Eof)
        {
            return null;
        }

        ValidateStatus(response, TnfsCommand.ReadDir);
        if (response.Length < 6)
        {
            throw new InvalidOperationException("TNFS READDIR response too short.");
        }

        var span = response.Span[5..];
        var terminator = span.IndexOf((byte)0);
        if (terminator < 0)
        {
            throw new InvalidOperationException("TNFS READDIR response missing terminator.");
        }

        return Encoding.ASCII.GetString(span[..terminator]);
    }

    public async Task CloseDirAsync(byte handle, CancellationToken cancellationToken = default)
    {
        var writer = new ArrayBufferWriter<byte>(5);
        WriteHeader(writer, _connectionId, TnfsCommand.CloseDir);
        writer.GetSpan(1)[0] = handle;
        writer.Advance(1);

        var response = await SendAndReceiveAsync(writer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        ValidateStatus(response, TnfsCommand.CloseDir);
    }

    public async Task<TnfsStat> StatAsync(string path, CancellationToken cancellationToken = default)
    {
        var writer = new ArrayBufferWriter<byte>(4 + Encoding.ASCII.GetByteCount(path) + 1);
        WriteHeader(writer, _connectionId, TnfsCommand.Stat);
        WriteCString(writer, path);

        var response = await SendAndReceiveAsync(writer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        ValidateStatus(response, TnfsCommand.Stat);
        if (response.Length < 27)
        {
            throw new InvalidOperationException("TNFS STAT response too short.");
        }

        var span = response.Span;
        var offset = 5;
        var mode = ReadUInt16(span[offset..]); offset += 2;
        var uid = ReadUInt16(span[offset..]); offset += 2;
        var gid = ReadUInt16(span[offset..]); offset += 2;
        var size = ReadUInt32(span[offset..]); offset += 4;
        var atime = ReadUInt32(span[offset..]); offset += 4;
        var mtime = ReadUInt32(span[offset..]); offset += 4;
        var ctime = ReadUInt32(span[offset..]); offset += 4;

        // Skip uid/gid strings if present
        var uidStrEnd = span[offset..].IndexOf((byte)0);
        if (uidStrEnd >= 0)
        {
            offset += uidStrEnd + 1;
            var gidStrEnd = span[offset..].IndexOf((byte)0);
            if (gidStrEnd >= 0)
            {
                offset += gidStrEnd + 1;
            }
        }

        return new TnfsStat(mode, uid, gid, size, atime, mtime, ctime);
    }

    public async Task<byte> OpenFileAsync(string path, ushort flags, ushort mode = 0, CancellationToken cancellationToken = default)
    {
        var writer = new ArrayBufferWriter<byte>(8 + Encoding.ASCII.GetByteCount(path) + 1);
        WriteHeader(writer, _connectionId, TnfsCommand.Open);
        WriteUInt16(writer, flags);
        WriteUInt16(writer, mode);
        WriteCString(writer, path);

        var response = await SendAndReceiveAsync(writer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        ValidateStatus(response, TnfsCommand.Open);
        if (response.Length < 6)
        {
            throw new InvalidOperationException("TNFS OPEN response too short.");
        }

        return response.Span[5];
    }

    public async Task<TnfsReadResult> ReadFileAsync(byte handle, int requestedBytes, CancellationToken cancellationToken = default)
    {
        if (requestedBytes <= 0 || requestedBytes > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedBytes), "Must be between 1 and 65535.");
        }

        var writer = new ArrayBufferWriter<byte>(7);
        WriteHeader(writer, _connectionId, TnfsCommand.Read);
        writer.GetSpan(1)[0] = handle;
        writer.Advance(1);
        WriteUInt16(writer, (ushort)requestedBytes);

        var response = await SendAndReceiveAsync(writer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        var status = GetStatus(response);
        if (status == TnfsStatus.Eof)
        {
            return TnfsReadResult.Eof;
        }

        ValidateStatus(response, TnfsCommand.Read);
        if (response.Length < 7)
        {
            throw new InvalidOperationException("TNFS READ response too short.");
        }

        var length = ReadUInt16(response.Span[5..]);
        if (response.Length < 7 + length)
        {
            throw new InvalidOperationException("TNFS READ response length mismatch.");
        }

        return new TnfsReadResult(false, length, response.Slice(7, length));
    }

    public async Task CloseFileAsync(byte handle, CancellationToken cancellationToken = default)
    {
        var writer = new ArrayBufferWriter<byte>(5);
        WriteHeader(writer, _connectionId, TnfsCommand.Close);
        writer.GetSpan(1)[0] = handle;
        writer.Advance(1);

        var response = await SendAndReceiveAsync(writer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        ValidateStatus(response, TnfsCommand.Close);
    }

    public async ValueTask DisposeAsync()
    {
        _udp.Dispose();
        await Task.CompletedTask;
    }

    private async Task<ReadOnlyMemory<byte>> SendAndReceiveAsync(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken)
    {
        await _udp.SendAsync(request.ToArray(), request.Length).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_receiveTimeout);

        var result = await _udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
        return result.Buffer;
    }

    private void WriteHeader(IBufferWriter<byte> writer, ushort connectionId, TnfsCommand command)
    {
        WriteUInt16(writer, connectionId);
        writer.GetSpan(1)[0] = _sequence++;
        writer.Advance(1);
        writer.GetSpan(1)[0] = (byte)command;
        writer.Advance(1);
    }

    private static void WriteCString(IBufferWriter<byte> writer, string value)
    {
        var byteCount = Encoding.ASCII.GetByteCount(value);
        var span = writer.GetSpan(byteCount + 1);
        Encoding.ASCII.GetBytes(value, span);
        span[byteCount] = 0;
        writer.Advance(byteCount + 1);
    }

    private static void WriteUInt16(IBufferWriter<byte> writer, ushort value)
    {
        var span = writer.GetSpan(2);
        span[0] = (byte)(value & 0xFF);
        span[1] = (byte)(value >> 8);
        writer.Advance(2);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> span)
    {
        if (span.Length < 2)
        {
            return 0;
        }

        return (ushort)(span[0] | (span[1] << 8));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> span)
    {
        if (span.Length < 4)
        {
            return 0;
        }

        return (uint)(span[0] | (span[1] << 8) | (span[2] << 16) | (span[3] << 24));
    }

    private static TnfsStatus GetStatus(ReadOnlyMemory<byte> response)
    {
        if (response.Length < 5)
        {
            return TnfsStatus.Unknown;
        }

        return (TnfsStatus)response.Span[4];
    }

    private static void ValidateStatus(ReadOnlyMemory<byte> response, TnfsCommand command)
    {
        var status = GetStatus(response);
        if (status != TnfsStatus.Ok)
        {
            throw new TnfsException($"TNFS {command} failed with status 0x{(byte)status:X2}", (byte)status);
        }
    }

    private void EnsureMounted()
    {
        if (!_mounted)
        {
            throw new InvalidOperationException("TNFS session not mounted.");
        }
    }
}

public readonly record struct TnfsMountResult(ushort ConnectionId, ushort ServerVersion, ushort MinRetryMilliseconds);

public readonly record struct TnfsStat(
    ushort Mode,
    ushort Uid,
    ushort Gid,
    uint Size,
    uint AccessTimeSeconds,
    uint ModifiedTimeSeconds,
    uint ChangeTimeSeconds)
{
    public bool IsDirectory => (Mode & 0xF000) == 0x4000;
}

public readonly record struct TnfsReadResult(bool IsEof, int BytesRead, ReadOnlyMemory<byte> Data)
{
    public static TnfsReadResult Eof => new(true, 0, ReadOnlyMemory<byte>.Empty);
}

public enum TnfsCommand : byte
{
    Mount = 0x00,
    Umount = 0x01,
    OpenDir = 0x10,
    ReadDir = 0x11,
    CloseDir = 0x12,
    TellDir = 0x15,
    SeekDir = 0x16,
    OpenDirX = 0x17,
    ReadDirX = 0x18,
    Open = 0x29,
    Read = 0x21,
    Write = 0x22,
    Close = 0x23,
    Stat = 0x24,
    Lseek = 0x25,
    Chmod = 0x26,
    Unlink = 0x27,
    Size = 0x30,
    Free = 0x31
}

public sealed class TnfsException : Exception
{
    public TnfsException(string message, byte statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public byte StatusCode { get; }
}

public enum TnfsStatus : byte
{
    Ok = 0x00,
    Eof = 0x21,
    Unknown = 0xFF
}
