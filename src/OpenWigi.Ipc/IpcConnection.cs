using System.Buffers.Binary;
using System.Text;
using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;

namespace OpenWigi.Ipc;

/// <summary>
/// Uma ponta do canal (named pipe ou qualquer <see cref="Stream"/>). Cada mensagem vai como
/// <c>u32 tamanho | u8 tipo | corpo</c>, little-endian. Escritas são serializadas internamente;
/// leituras devem vir de um único laço.
/// </summary>
public sealed class IpcConnection(Stream stream) : IAsyncDisposable
{
    public const string PipeName = "OpenWigiDash.v1";
    public const ushort ProtocolVersion = 1;
    private const int MaxMessageBytes = 4 * 1024 * 1024;

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private enum Kind : byte
    {
        Hello = 1,
        Frame = 2,
        ShowPage = 3,
        SetBrightness = 4,
        Wake = 5,
        Sleep = 6,
        Welcome = 64,
        Ownership = 65,
        DeviceStatus = 66,
        Gesture = 67,
        Error = 127,
    }

    public async Task SendAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        var body = Encode(message);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Lê a próxima mensagem; devolve null quando a outra ponta fecha o canal.</summary>
    public async Task<IpcMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var header = new byte[4];
        if (!await ReadExactlyOrEndAsync(header, cancellationToken).ConfigureAwait(false))
            return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxMessageBytes)
            throw new InvalidDataException($"Mensagem IPC com tamanho inválido: {length}.");

        var body = new byte[length];
        if (!await ReadExactlyOrEndAsync(body, cancellationToken).ConfigureAwait(false))
            return null;
        return Decode(body);
    }

    public async ValueTask DisposeAsync()
    {
        await stream.DisposeAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }

    internal static byte[] Encode(IpcMessage message)
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer, Encoding.UTF8);
        switch (message)
        {
            case Hello m:
                w.Write((byte)Kind.Hello);
                w.Write(m.ProtocolVersion);
                w.Write(m.UserName);
                break;
            case Frame m:
                w.Write((byte)Kind.Frame);
                w.Write((byte)m.Page);
                w.Write(m.ByteOffset);
                w.Write(m.Pixels.Length);
                w.Write(m.Pixels);
                break;
            case ShowPage m:
                w.Write((byte)Kind.ShowPage);
                w.Write((byte)m.Page);
                w.Write((byte)m.Transition);
                break;
            case SetBrightness m:
                w.Write((byte)Kind.SetBrightness);
                w.Write(m.Percent);
                break;
            case WakeDisplay:
                w.Write((byte)Kind.Wake);
                break;
            case SleepDisplay:
                w.Write((byte)Kind.Sleep);
                break;
            case Welcome m:
                w.Write((byte)Kind.Welcome);
                w.Write(m.SessionId);
                WriteStatus(w, m.Device);
                break;
            case Ownership m:
                w.Write((byte)Kind.Ownership);
                w.Write(m.IsOwner);
                w.Write(m.Reason);
                break;
            case DeviceStatus m:
                w.Write((byte)Kind.DeviceStatus);
                WriteStatus(w, m);
                break;
            case Gesture m:
                w.Write((byte)Kind.Gesture);
                w.Write((byte)m.Event.Kind);
                w.Write(m.Event.X);
                w.Write(m.Event.Y);
                w.Write((int)m.Event.Duration.TotalMilliseconds);
                w.Write(m.Event.Progress);
                break;
            case IpcError m:
                w.Write((byte)Kind.Error);
                w.Write(m.Message);
                break;
            default:
                throw new ArgumentException($"Mensagem sem codificação: {message.GetType().Name}", nameof(message));
        }
        w.Flush();
        return buffer.ToArray();
    }

    internal static IpcMessage Decode(byte[] body)
    {
        using var r = new BinaryReader(new MemoryStream(body), Encoding.UTF8);
        var kind = (Kind)r.ReadByte();
        return kind switch
        {
            Kind.Hello => new Hello(r.ReadUInt16(), r.ReadString()),
            Kind.Frame => new Frame(r.ReadByte(), r.ReadInt32(), r.ReadBytes(r.ReadInt32())),
            Kind.ShowPage => new ShowPage(r.ReadByte(), (Transition)r.ReadByte()),
            Kind.SetBrightness => new SetBrightness(r.ReadInt32()),
            Kind.Wake => new WakeDisplay(),
            Kind.Sleep => new SleepDisplay(),
            Kind.Welcome => new Welcome(r.ReadInt32(), ReadStatus(r)),
            Kind.Ownership => new Ownership(r.ReadBoolean(), r.ReadString()),
            Kind.DeviceStatus => ReadStatus(r),
            Kind.Gesture => new Gesture(new GestureEvent((GestureKind)r.ReadByte(), r.ReadInt32(), r.ReadInt32(),
                TimeSpan.FromMilliseconds(r.ReadInt32()), r.ReadDouble())),
            Kind.Error => new IpcError(r.ReadString()),
            _ => throw new InvalidDataException($"Tipo de mensagem IPC desconhecido: {(byte)kind}."),
        };
    }

    private static void WriteStatus(BinaryWriter w, DeviceStatus status)
    {
        w.Write(status.Connected);
        w.Write(status.DeviceId.ToByteArray());
        w.Write(status.Firmware);
    }

    private static DeviceStatus ReadStatus(BinaryReader r) =>
        new(r.ReadBoolean(), new Guid(r.ReadBytes(16)), r.ReadString());

    private async Task<bool> ReadExactlyOrEndAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (total == 0)
                    return false;
                throw new EndOfStreamException("Canal IPC fechado no meio de uma mensagem.");
            }
            total += read;
        }
        return true;
    }
}
