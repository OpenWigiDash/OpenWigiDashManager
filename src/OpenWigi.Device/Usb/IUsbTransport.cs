namespace OpenWigi.Device.Usb;

/// <summary>
/// Transporte USB mínimo que o protocolo do WigiDash usa: transferências de controle
/// de classe/interface e escrita bulk no endpoint de dados. Existe para que o
/// protocolo possa ser testado com um transporte falso.
/// </summary>
public interface IUsbTransport : IDisposable
{
    /// <summary>Control IN (bmRequestType 0xA1). Retorna os bytes lidos.</summary>
    int ControlIn(byte request, ushort value, Span<byte> buffer);

    /// <summary>Control OUT (bmRequestType 0x21), com ou sem estágio de dados.</summary>
    void ControlOut(byte request, ushort value, ReadOnlySpan<byte> data);

    /// <summary>Escrita bulk no endpoint OUT 0x01. Retorna os bytes escritos.</summary>
    int BulkWrite(ReadOnlySpan<byte> data);
}
