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

    private void EnsureMounted()
    {
        if (!_mounted)
        {
            throw new InvalidOperationException("TNFS session not mounted.");
        }
    }
}

public readonly record struct TnfsMountResult(ushort ConnectionId, ushort ServerVersion, ushort MinRetryMilliseconds);

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
    Open = 0x20,
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
