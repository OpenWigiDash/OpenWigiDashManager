using System.IO.Pipelines;
using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;
using OpenWigi.Ipc;

namespace OpenWigi.Service.Tests;

public class IpcConnectionTests
{
    public static TheoryData<IpcMessage> Messages() =>
    [
        new Hello(IpcConnection.ProtocolVersion, "victo"),
        new ShowPage(1, Transition.WipeWest),
        new SetBrightness(42),
        new WakeDisplay(),
        new SleepDisplay(),
        new Welcome(2, new DeviceStatus(true, Guid.NewGuid(), "WigiDash-1.0")),
        new Ownership(true, "Sessão ativa no console"),
        DeviceStatus.Disconnected,
        new Gesture(new GestureEvent(GestureKind.LongPress, 10, 20, TimeSpan.FromMilliseconds(600), 1)),
        new IpcError("falhou"),
        new SubscribePreview(true),
        new EmulatedTouch(TouchContact.Down, 500, 300),
        new SelectDevice(DeviceKind.Virtual),
        new DeviceStatus(true, Guid.NewGuid(), "virtual", DeviceKind.Virtual, UsbPresent: true),
    ];

    [Theory]
    [MemberData(nameof(Messages))]
    public void Mensagens_sobrevivem_a_codificacao(IpcMessage message)
    {
        Assert.Equal(message, IpcConnection.Decode(IpcConnection.Encode(message)));
    }

    [Fact]
    public async Task Quadro_inteiro_passa_pelo_canal()
    {
        var pixels = new byte[WigiDashProtocol.FrameBytes];
        Random.Shared.NextBytes(pixels);

        var pipe = new Pipe();
        await using var writer = new IpcConnection(pipe.Writer.AsStream());
        await using var reader = new IpcConnection(pipe.Reader.AsStream());

        var send = writer.SendAsync(new Frame(1, 2032, pixels));
        var received = Assert.IsType<Frame>(await reader.ReceiveAsync());
        await send;

        Assert.Equal((1, 2032), (received.Page, received.ByteOffset));
        Assert.Equal(pixels, received.Pixels);
    }

    [Fact]
    public async Task Canal_fechado_devolve_null()
    {
        var pipe = new Pipe();
        await using var reader = new IpcConnection(pipe.Reader.AsStream());
        await pipe.Writer.CompleteAsync();
        Assert.Null(await reader.ReceiveAsync());
    }
}
