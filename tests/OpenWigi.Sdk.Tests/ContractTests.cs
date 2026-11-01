using Microsoft.AspNetCore.Components;

[assembly: OpenWigi.Sdk.OpenWigiWidget(typeof(OpenWigi.Sdk.Tests.CounterWidget))]

namespace OpenWigi.Sdk.Tests;

/// <summary>Widget mínimo escrito só contra o contrato: conta toques e muda de cor.</summary>
public sealed class CounterWidget : IWidget
{
    public WidgetManifest Manifest { get; } = new()
    {
        Id = new Guid("6d1f3a52-8c2e-4b8f-9b8e-0f5f6a3c1d20"),
        Name = "Contador",
        Version = new Version(1, 0),
        SupportedSizes = [WidgetSize.Small, WidgetSize.Wide],
        SupportsFullscreen = true,
    };

    public ValueTask<IWidgetInstance> CreateInstanceAsync(IWidgetContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IWidgetInstance>(new CounterInstance(context));
}

public sealed class CounterInstance(IWidgetContext context) : IWidgetInstance
{
    public int Taps { get; private set; }

    public ValueTask<WidgetFrame> RenderAsync(RenderReason reason, CancellationToken cancellationToken) =>
        ValueTask.FromResult(WidgetFrame.Solid(context.PixelSize, (byte)Taps, 0, 0));

    public async ValueTask OnTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        switch (touch.Kind)
        {
            case TouchKind.Tap:
                Taps++;
                await context.Settings.SetJsonAsync("taps", Taps, cancellationToken: cancellationToken);
                await context.PresentAsync(WidgetFrame.Solid(context.PixelSize, (byte)Taps, 0, 0), cancellationToken);
                break;
            case TouchKind.LongPress:
                await context.RequestFullscreenAsync(!context.IsFullscreen, cancellationToken);
                break;
        }
    }

    public Type? SettingsComponent => typeof(CounterSettings);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class CounterSettings : WidgetSettingsComponent<CounterInstance>;

public class ContractTests
{
    // Cópia de OpenWigi.Device.Gestures.GestureKind (PR #20). O SDK não referencia o Device; se um
    // dos dois mudar, este teste lembra de mudar o outro.
    private static readonly string[] GestureKindNames =
    [
        "PressStarted", "PressProgress", "LongPress", "PressEnded", "Tap", "DoubleTap",
        "SwipeLeft", "SwipeRight", "SwipeUp", "SwipeDown",
    ];

    [Fact]
    public void TouchKind_espelha_o_GestureKind_do_dispositivo()
    {
        Assert.Equal(GestureKindNames, Enum.GetNames<TouchKind>());
    }

    [Fact]
    public void Gesto_de_acao_exclui_os_eventos_de_pressao()
    {
        Assert.False(new TouchEvent(TouchKind.PressProgress, 0, 0, TimeSpan.Zero, 0.5).IsGesture);
        Assert.True(new TouchEvent(TouchKind.LongPress, 0, 0, TimeSpan.Zero, 1).IsGesture);
        Assert.True(new TouchEvent(TouchKind.SwipeUp, 0, 0, TimeSpan.Zero).IsSwipe);
    }

    [Fact]
    public void Host_acha_o_widget_pelo_atributo_do_assembly()
    {
        var attribute = Assert.Single(typeof(CounterWidget).Assembly.GetCustomAttributes(typeof(OpenWigiWidgetAttribute), false));
        var widget = (IWidget)Activator.CreateInstance(((OpenWigiWidgetAttribute)attribute).WidgetType)!;
        Assert.Empty(widget.Manifest.Validate());
    }

    [Fact]
    public void Manifest_invalido_lista_os_problemas()
    {
        var manifest = new WidgetManifest
        {
            Id = Guid.Empty,
            Name = " ",
            Version = new Version(1, 0),
            SupportedSizes = [],
            SdkVersion = new Version(1, 0),
        };
        Assert.Equal(4, manifest.Validate().Count);
    }

    [Theory]
    [InlineData(3, 0, true)]
    [InlineData(2, 0, false)]
    [InlineData(3, 99, false)]
    [InlineData(4, 0, false)]
    public void Compatibilidade_pelo_major_e_minor(int major, int minor, bool compatible)
    {
        Assert.Equal(compatible, OpenWigiSdk.IsCompatible(new Version(major, minor)));
    }

    [Fact]
    public async Task Ganchos_opcionais_tem_implementacao_vazia()
    {
        var context = new FakeWidgetContext(WidgetSize.Small);
        IWidget widget = new CounterWidget();
        await widget.LoadAsync(context, default);
        Assert.Null(await widget.GetPreviewAsync(WidgetSize.Small, context.PixelSize, default));

        IWidgetInstance instance = await widget.CreateInstanceAsync(context, default);
        await instance.OnSleepAsync(default);
        await instance.OnWakeAsync(default);
        await instance.OnThemeChangedAsync(default);
        await instance.OnSizeChangedAsync(new PixelSize(100, 60), default);
        await instance.OnActionAsync(Guid.NewGuid(), default);
        await instance.OnRemovedAsync(default);
        await instance.DisposeAsync();
    }

    [Fact]
    public async Task Instancia_renderiza_empurra_quadros_e_entra_em_tela_cheia()
    {
        var context = new FakeWidgetContext(WidgetSize.Wide);
        IWidgetInstance instance = await new CounterWidget().CreateInstanceAsync(context, default);

        var first = await instance.RenderAsync(RenderReason.Initial, default);
        // A moldura padrão não tira nada: o 4x2 desenha nos 404x145 inteiros.
        Assert.Equal(new PixelSize(404, 145), first.Size);

        await instance.OnTouchAsync(new TouchEvent(TouchKind.Tap, 10, 10, TimeSpan.FromMilliseconds(80)), default);
        Assert.Single(context.Presented);
        Assert.Equal("1", context.FakeSettings.Values["taps"].Value);
        Assert.Equal(1, await context.Settings.GetJsonAsync("taps", 0));

        await instance.OnTouchAsync(new TouchEvent(TouchKind.LongPress, 10, 10, TimeSpan.FromMilliseconds(600), 1), default);
        Assert.True(context.IsFullscreen);
        var full = await instance.RenderAsync(RenderReason.FullscreenChanged, default);
        Assert.Equal(WidgetGrid.Screen, full.Size);
    }

    [Fact]
    public async Task Json_invalido_nas_configuracoes_volta_o_padrao()
    {
        var settings = new FakeSettings();
        await settings.SetAsync("conf", "{quebrado");
        Assert.Equal(7, await settings.GetJsonAsync("conf", 7));
        Assert.Equal(7, await settings.GetJsonAsync("ausente", 7));
    }

    [Fact]
    public void Tela_de_configuracoes_e_componente_Razor_com_parametro_Instance()
    {
        var instance = new CounterInstance(new FakeWidgetContext(WidgetSize.Small));
        var component = ((IWidgetInstance)instance).SettingsComponent;

        Assert.NotNull(component);
        Assert.True(typeof(IComponent).IsAssignableFrom(component));
        var parameter = component.GetProperty("Instance")!;
        Assert.NotNull(parameter.GetCustomAttributes(typeof(ParameterAttribute), true).SingleOrDefault());
        Assert.True(parameter.PropertyType.IsInstanceOfType(instance));
    }
}
