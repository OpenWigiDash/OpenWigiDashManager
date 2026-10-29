using OpenWigi.Device.Usb;

namespace OpenWigi.Device.Tests;

/// <summary>Grava as chamadas e responde control IN com bytes programados por bRequest.</summary>
internal sealed class FakeUsbTransport : IUsbTransport
{
    public List<(string Kind, byte Request, ushort Value, byte[] Data)> Calls { get; } = [];
    public Dictionary<byte, byte[]> Responses { get; } = [];
    public Func<int, int>? BulkResult { get; set; }

    public int ControlIn(byte request, ushort value, Span<byte> buffer)
    {
        Calls.Add(("in", request, value, []));
        if (!Responses.TryGetValue(request, out var response))
            return 0;
        var length = Math.Min(response.Length, buffer.Length);
        response.AsSpan(0, length).CopyTo(buffer);
        return length;
    }

    public void ControlOut(byte request, ushort value, ReadOnlySpan<byte> data) =>
        Calls.Add(("out", request, value, data.ToArray()));

    public int BulkWrite(ReadOnlySpan<byte> data)
    {
        Calls.Add(("bulk", 0, 0, data.ToArray()));
        return BulkResult?.Invoke(data.Length) ?? data.Length;
    }

    public void Dispose()
    {
    }
}
