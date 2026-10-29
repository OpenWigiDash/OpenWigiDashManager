using System.Buffers.Binary;
using System.Text;
using OpenWigi.Device.Usb;

namespace OpenWigi.Device.Protocol;

/// <summary>
/// Comandos do WigiDash, um por método, sem estado nem threads. Não é thread-safe:
/// quem usa (<see cref="WigiDashDevice"/>) serializa o acesso.
/// </summary>
public sealed class WigiDashProtocol(IUsbTransport transport)
{
    public const int Width = 1016;
    public const int Height = 592;
    public const int BytesPerPixel = 2;
    public const int FrameBytes = Width * Height * BytesPerPixel;
    public const int PageCount = 3;

    public DeviceMode GetMode()
    {
        Span<byte> buffer = stackalloc byte[3];
        transport.ControlIn(Commands.Mode, 0, buffer);
        return buffer switch
        {
            [(byte)'W', (byte)'D', 0] => DeviceMode.Application,
            [(byte)'B', (byte)'L', 0] => DeviceMode.Bootloader,
            _ => DeviceMode.Unknown,
        };
    }

    public string GetFirmware()
    {
        Span<byte> buffer = stackalloc byte[64];
        var read = transport.ControlIn(Commands.Firmware, 0, buffer);
        var text = buffer[..Math.Min(read, buffer.Length)];
        var end = text.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end >= 0 ? text[..end] : text);
    }

    /// <summary>UID de 12 bytes do microcontrolador, completado com zeros para virar um GUID estável.</summary>
    public Guid GetUid()
    {
        Span<byte> buffer = stackalloc byte[13];
        transport.ControlIn(Commands.Uid, 0, buffer);
        Span<byte> guid = stackalloc byte[16];
        buffer[1..].CopyTo(guid);
        return new Guid(guid);
    }

    public DeviceInfo GetInfo() => new(GetUid(), GetFirmware(), GetMode());

    public DeviceConfig GetConfig()
    {
        Span<byte> buffer = stackalloc byte[DeviceConfig.Size];
        transport.ControlIn(Commands.ConfigGet, 0, buffer);
        return DeviceConfig.Parse(buffer);
    }

    /// <summary>Aplica a config na RAM do aparelho. Use <see cref="StoreConfig"/> para persistir.</summary>
    public void SetConfig(DeviceConfig config) => transport.ControlOut(Commands.ConfigSet, 0, config.Serialize());

    public void StoreConfig() => transport.ControlOut(Commands.ConfigStore, 0, default);

    public void ResetConfig() => transport.ControlOut(Commands.ConfigReset, 0, default);

    public int GetBrightness()
    {
        Span<byte> buffer = stackalloc byte[1];
        transport.ControlIn(Commands.BrightnessGet, 0, buffer);
        return buffer[0];
    }

    public void SetBrightness(int percent) =>
        transport.ControlOut(Commands.BrightnessSet, 0, [(byte)Math.Clamp(percent, 0, 100)]);

    /// <summary>Acorda a tela e zera o contador de timeout.</summary>
    public void Wake() => transport.ControlOut(Commands.Wake, 0, default);

    public void Sleep() => transport.ControlOut(Commands.Sleep, 0, default);

    public void Reset(ResetMode mode) => transport.ControlOut(Commands.Reset, (ushort)mode, default);

    public TouchSample ReadTouch()
    {
        Span<byte> buffer = stackalloc byte[8];
        transport.ControlIn(Commands.TouchState, 0, buffer);
        var contact = buffer[0] is >= 1 and <= 2 ? (TouchContact)buffer[0] : TouchContact.None;
        return new TouchSample(
            contact,
            contact == TouchContact.None ? 0 : BinaryPrimitives.ReadInt16LittleEndian(buffer[2..]),
            contact == TouchContact.None ? 0 : BinaryPrimitives.ReadInt16LittleEndian(buffer[4..]),
            buffer[6],
            buffer[7] != 0);
    }

    /// <summary>
    /// Declara uma região de desenho numa página. A região 0 cobrindo a tela toda é o
    /// suficiente: compomos o quadro no PC e mandamos tudo para ela.
    /// </summary>
    public void AddRegion(int page, int region, int x, int y, int width, int height)
    {
        Span<byte> data = stackalloc byte[20];
        data.Clear();
        BinaryPrimitives.WriteInt16LittleEndian(data, (short)x);
        BinaryPrimitives.WriteInt16LittleEndian(data[2..], (short)y);
        BinaryPrimitives.WriteInt16LittleEndian(data[4..], (short)width);
        BinaryPrimitives.WriteInt16LittleEndian(data[6..], (short)height);
        transport.ControlOut(Commands.RegionAdd, PageRegion(page, region), data);
    }

    public void RemoveRegion(int page, int region) =>
        transport.ControlOut(Commands.RegionRemove, PageRegion(page, region), default);

    /// <summary>
    /// Escreve pixels RGB565 (little-endian, linha a linha) numa região. <paramref name="byteOffset"/>
    /// é a posição dentro da região, o que permite mandar só uma faixa de linhas.
    /// </summary>
    public void WriteRegion(int page, int region, int byteOffset, ReadOnlySpan<byte> pixels)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header, byteOffset);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], pixels.Length);
        transport.ControlOut(Commands.RegionWrite, PageRegion(page, region), header);
        try
        {
            var written = transport.BulkWrite(pixels);
            if (written != pixels.Length)
                throw new IOException($"Escrita parcial do quadro: {written} de {pixels.Length} bytes.");
        }
        catch
        {
            TryAbortWrite();
            throw;
        }
    }

    /// <summary>Mostra uma das páginas do hardware com a animação pedida.</summary>
    public void ShowPage(int page, Transition transition = Transition.None)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(page, PageCount);
        transport.ControlOut(Commands.Screen, (ushort)(0x20 | page | ((int)transition << 2)), default);
    }

    /// <summary>Volta para a tela de boas-vindas do firmware.</summary>
    public void ShowWelcome() => transport.ControlOut(Commands.Screen, 1, default);

    private void TryAbortWrite()
    {
        try
        {
            transport.ControlOut(Commands.RegionWriteAbort, 0, default);
        }
        catch (IOException)
        {
            // O erro original é o que importa.
        }
    }

    private static ushort PageRegion(int page, int region)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(page, PageCount);
        return (ushort)((page << 8) | (byte)region);
    }
}
