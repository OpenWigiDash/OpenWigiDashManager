using OpenWigi.Device.Display;
using OpenWigi.Device.Protocol;

namespace OpenWigi.Device.Tests;

public class ProtocolTests
{
    private readonly FakeUsbTransport _usb = new();
    private readonly WigiDashProtocol _protocol;

    public ProtocolTests() => _protocol = new WigiDashProtocol(_usb);

    [Fact]
    public void Modo_aplicacao_e_bootloader_sao_reconhecidos()
    {
        _usb.Responses[0x00] = "WD\0"u8.ToArray();
        Assert.Equal(DeviceMode.Application, _protocol.GetMode());

        _usb.Responses[0x00] = "BL\0"u8.ToArray();
        Assert.Equal(DeviceMode.Bootloader, _protocol.GetMode());
    }

    [Fact]
    public void Uid_vira_o_mesmo_guid_que_o_manager_oficial_usa()
    {
        // Resposta real do aparelho do Victor: 12 bytes; o GUID usa do segundo em diante.
        _usb.Responses[0x05] = Convert.FromHexString("490037001951323531373132");
        Assert.Equal(new Guid("19003700-3251-3135-3731-320000000000"), _protocol.GetUid());
    }

    [Fact]
    public void Leitura_de_toque_decodifica_posicao_e_estado()
    {
        _usb.Responses[0x33] = [1, 0, 0x67, 0x03, 0x9B, 0x01, 2, 1];
        var sample = _protocol.ReadTouch();
        Assert.Equal(new TouchSample(TouchContact.Down, 871, 411, 2, true), sample);
    }

    [Fact]
    public void Sem_contato_a_posicao_antiga_e_descartada()
    {
        _usb.Responses[0x33] = Convert.FromHexString("00EA67039B010000");
        Assert.Equal(new TouchSample(TouchContact.None, 0, 0, 0, false), _protocol.ReadTouch());
    }

    [Fact]
    public void Config_sobrevive_ida_e_volta_com_crc_valido()
    {
        var original = new DeviceConfig
        {
            Version = 1,
            DefaultBrightness = 99,
            ScreenTimeoutSeconds = 300,
            Nickname = "Victor PC WigiDash",
            Vcom = 0x1234,
            Avdd = 0x5678,
            DisplayOffsetX = 2,
            DisplayOffsetY = 3,
        };
        var raw = original.Serialize();

        Assert.Equal(DeviceConfig.Size, raw.Length);
        Assert.Equal(Crc16.Compute(raw.AsSpan(0, 46)), BitConverter.ToUInt16(raw, 46));
        var parsed = DeviceConfig.Parse(raw);
        Assert.Equal(original with { Original = parsed.Original }, parsed);
    }

    [Fact]
    public void Gravar_config_preserva_bytes_desconhecidos()
    {
        var raw = new byte[DeviceConfig.Size];
        raw[2] = 0xAA; // preenchimento entre campos: o aparelho manda algo aqui
        raw[3] = 0xBB;
        var config = DeviceConfig.Parse(raw) with { Nickname = "novo" };

        var written = config.Serialize();
        Assert.Equal(0xAA, written[2]);
        Assert.Equal(0xBB, written[3]);
    }

    [Fact]
    public void Escrita_de_regiao_manda_offset_tamanho_e_depois_os_pixels()
    {
        var pixels = new byte[400];
        _protocol.WriteRegion(page: 2, region: 0, byteOffset: 2032, pixels);

        var setup = _usb.Calls[0];
        Assert.Equal(("out", (byte)0x61, (ushort)0x0200), (setup.Kind, setup.Request, setup.Value));
        Assert.Equal(2032, BitConverter.ToInt32(setup.Data, 0));
        Assert.Equal(400, BitConverter.ToInt32(setup.Data, 4));
        Assert.Equal("bulk", _usb.Calls[1].Kind);
    }

    [Fact]
    public void Escrita_parcial_aborta_e_falha()
    {
        _usb.BulkResult = length => length / 2;
        Assert.Throws<IOException>(() => _protocol.WriteRegion(0, 0, 0, new byte[100]));
        Assert.Equal((byte)0x63, _usb.Calls[^1].Request);
    }

    [Theory]
    [InlineData(0, Transition.None, 0x30)]
    [InlineData(1, Transition.WipeEast, 0x21)]
    [InlineData(2, Transition.WipeWest, 0x26)]
    public void Troca_de_pagina_codifica_pagina_e_transicao(int page, Transition transition, int expected)
    {
        _protocol.ShowPage(page, transition);
        Assert.Equal((byte)0x70, _usb.Calls[0].Request);
        Assert.Equal(expected, _usb.Calls[0].Value);
    }

    [Fact]
    public void Brilho_e_limitado_a_0_100()
    {
        _protocol.SetBrightness(150);
        Assert.Equal([100], _usb.Calls[0].Data);
    }

    [Fact]
    public void Rgb565_empacota_bgra_em_little_endian()
    {
        byte[] bgra = [0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0xFF]; // vermelho, azul
        var output = new byte[4];
        Rgb565.FromBgra32(bgra, width: 2, height: 1, stride: 8, output);
        Assert.Equal([0x00, 0xF8, 0x1F, 0x00], output);
    }
}
