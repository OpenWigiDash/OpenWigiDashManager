using System.Drawing.Imaging;
using OpenWigi.Device.Protocol;
using OpenWigi.Ipc;
using OpenWigi.Service.Device;
using OpenWigi.Service.Sessions;

namespace OpenWigi.Service.Tests;

public class ServiceScreenTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 14, 37, 0);

    public static TheoryData<string, ServiceScreenModel> Screens() => new()
    {
        { "bloqueada-sem-lista", new ServiceScreenModel("Sessão bloqueada", true, Now, "0.4.3", []) },
        { "bloqueada-sem-lista-desligar-monitor", new ServiceScreenModel("Sessão bloqueada", true, Now, "0.4.3", [], MonitorOff: true) },
        {
            "bloqueada-com-sessoes-desligar-monitor", new ServiceScreenModel("Sessão bloqueada", true, Now, "0.4.3",
            [
                new ServiceScreenSession("Victor Perez", WindowsSessionState.Locked, false, Picture()),
                new ServiceScreenSession("Ana Souza", WindowsSessionState.Disconnected, false, null),
                new ServiceScreenSession("convidado", WindowsSessionState.Disconnected, false, null),
                new ServiceScreenSession("d", WindowsSessionState.Disconnected, false, null),
            ], MonitorOff: true)
        },
        {
            "bloqueada-com-sessoes", new ServiceScreenModel("Sessão bloqueada", true, Now, "0.4.3",
            [
                new ServiceScreenSession("Victor Perez", WindowsSessionState.Locked, false, Picture()),
                new ServiceScreenSession("Ana Souza", WindowsSessionState.Disconnected, false, null),
                new ServiceScreenSession("convidado", WindowsSessionState.Disconnected, false, null),
            ])
        },
        {
            "rdp-ativo", new ServiceScreenModel("Nenhum usuário no console", true, Now, "0.4.3",
            [
                new ServiceScreenSession("Victor Perez", WindowsSessionState.Active, true, Picture()),
            ])
        },
        {
            "muitas-sessoes", new ServiceScreenModel("OpenWigiDash não está aberto para o usuário do console", false, Now, "0.4.3",
            [
                new ServiceScreenSession("Victor Perez", WindowsSessionState.Active, false, null),
                new ServiceScreenSession("Ana Souza", WindowsSessionState.Locked, true, null),
                new ServiceScreenSession("Um nome de usuário bem comprido mesmo", WindowsSessionState.Disconnected, false, null),
                new ServiceScreenSession("d", WindowsSessionState.Disconnected, false, null),
                new ServiceScreenSession("e", WindowsSessionState.Disconnected, false, null),
            ])
        },
    };

    /// <summary>Desenha cada variação e grava em PNG (TestResults/service-screen), para conferir o desenho de olho.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void Tela_do_servico_desenha_cada_variacao(string name, ServiceScreenModel model)
    {
        using var bitmap = ServiceScreen.RenderBitmap(model);
        var folder = Path.Combine(AppContext.BaseDirectory, "service-screen");
        Directory.CreateDirectory(folder);
        bitmap.Save(Path.Combine(folder, $"{name}.png"), ImageFormat.Png);

        Assert.Equal(WigiDashProtocol.FrameBytes, ServiceScreen.Render(model).Length);
    }

    [Theory]
    [InlineData(40, 560, true)]
    [InlineData(150, 514, true)] // folga de toque acima do botão
    [InlineData(500, 560, false)]
    [InlineData(150, 300, false)]
    public void Toque_no_botao_desligar_monitor(int x, int y, bool hits)
    {
        Assert.Equal(hits, ServiceScreen.HitsMonitorOff(x, y));
    }

    [Theory]
    [InlineData("Victor Perez", "VP")]
    [InlineData("victo", "V")]
    [InlineData("ana.souza", "AS")]
    [InlineData("", "?")]
    public void Iniciais_de_quem_nao_tem_foto(string name, string initials)
    {
        Assert.Equal(initials, ServiceScreen.Initials(name));
    }

    // Uma "foto" qualquer: degradê opaco.
    private static byte[] Picture()
    {
        var pixels = new byte[UserProfile.PictureBytes];
        for (var y = 0; y < UserProfile.PictureSize; y++)
        for (var x = 0; x < UserProfile.PictureSize; x++)
        {
            var i = (y * UserProfile.PictureSize + x) * 4;
            pixels[i] = (byte)(120 + y);
            pixels[i + 1] = (byte)(80 + x / 2);
            pixels[i + 2] = (byte)(60 + y / 2);
            pixels[i + 3] = 255;
        }
        return pixels;
    }
}
