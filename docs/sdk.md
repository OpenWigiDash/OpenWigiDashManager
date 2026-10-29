# OpenWigi.Sdk — contrato dos widgets (v2.0)

Pacote `victorperez2911.OpenWigi.Sdk` no nuget.org (assembly `OpenWigi.Sdk`, `net10.0-windows`). Substitui o `WigiDashWidgetFramework` (v1, net472) sem
compatibilidade binária: plugin antigo precisa ser portado.

## Peças

| Tipo | Papel | No v1 era |
|---|---|---|
| `[assembly: OpenWigiWidget(typeof(X))]` | O host acha a entrada do plugin sem varrer o assembly | varredura por `IWidgetObject` |
| `IWidget` + `WidgetManifest` | Ficha (id, nome, versão, tamanhos, tela cheia) e fábrica de instâncias | `IWidgetObject` / `IWidgetBase` |
| `IWidgetInstance` | Widget na tela: quadros, toques, repouso, tela cheia, ações, remoção | `IWidgetInstance` + `WithRemoval` + `WithFullscreen` |
| `IWidgetContext` | O que o host oferece a uma instância | `IWidgetManager` (global) |
| `WidgetSettingsComponent<T>` | Tela de configurações em Razor (MudBlazor) | `UserControl` WPF |

## Grade

Células de **200x145** com **4 px** de espaçamento; 5x4 células fecham os 1016x592 da tela.
`WidgetSize(colunas, linhas)` vai de 1x1 a 5x4 e `PixelSize` faz a conta
(`2x1` = 404x145, `2x2` = 404x294).

## Quadros

`WidgetFrame` é um buffer cru em `Bgra32` (o que System.Drawing, SkiaSharp e WPF geram) ou `Rgb565`
(nativo da tela, vai sem conversão), com `Stride` opcional e região suja opcional.

- **Puxado:** o host chama `RenderAsync(reason)` e recebe o quadro inteiro (`Initial`, `Requested`,
  `Resumed`, `FullscreenChanged`, `ThemeChanged`). Widget estático só implementa isso.
- **Empurrado:** a instância chama `context.PresentAsync(frame)` quando tem novidade (captura, animação).
  O quadro é sempre completo; `Dirty` diz o que mudou para o host mandar só esse pedaço pelo USB.
  O `ValueTask` completa quando o host aceitou: quem gera mais rápido que o USB espera ali.
- `context.RequestRender()` pede um `RenderAsync(Requested)`; pedidos seguidos se juntam.
- Depois de entregue, o buffer é do host: não reescreva o mesmo array.

## Toques

`TouchEvent(Kind, X, Y, Duration, Progress)`, com X/Y relativos ao widget. `TouchKind` tem os mesmos
nomes e ordem do `GestureKind` do `OpenWigi.Device` (um teste guarda isso):

`PressStarted` → `PressProgress` (0..1, para desenhar o anel do long-press) → `LongPress` (dedo ainda
na tela) → `PressEnded` (fecha todo `PressStarted`). Além disso `Tap`, `DoubleTap` (logo depois do
`Tap`) e `SwipeLeft/Right/Up/Down`. `IsGesture` separa os gestos "de ação" (o antigo `ClickEvent`) dos
eventos de pressão.

## Ciclo de vida

1. Host carrega o assembly, lê o atributo, valida `Manifest.Validate()` e chama `IWidget.LoadAsync(host)`.
2. Para cada widget no layout: `CreateInstanceAsync(context)` e logo `RenderAsync(Initial)`.
3. Durante a vida: `OnTouchAsync`, `OnSleepAsync`/`OnWakeAsync` (+ `RenderAsync(Resumed)`),
   `OnFullscreenChangedAsync` (+ `RenderAsync(FullscreenChanged)`), `OnThemeChangedAsync`, `OnActionAsync`.
4. Pessoa tirou o widget: `OnRemovedAsync` e depois `DisposeAsync`. Descarregar o plugin só chama `DisposeAsync`.

O host nunca chama dois métodos da mesma instância ao mesmo tempo. Todos os ganchos, exceto
`RenderAsync` e `DisposeAsync`, têm implementação vazia.

## Contexto do host (`IWidgetContext`)

- `Settings`: chave/valor em texto, por instância, no perfil do usuário dono do layout; `cloneable:
  false` para o que não deve ir junto ao duplicar. `GetJsonAsync`/`SetJsonAsync` para objetos.
- `Files`: `ImportAsync` (copia arquivo escolhido), `CreateAsync` (reserva caminho), `GetPathAsync`,
  `DeleteAsync`. Somem com a remoção do widget.
- `Actions`: gatilhos que o widget dispara (`RegisterTriggerAsync`/`FireTriggerAsync`), ações que ele
  executa (`RegisterActionAsync` → `OnActionAsync`), ações que a pessoa ligou à instância
  (`GetBoundActionsAsync(setId)`) e `InvokeAsync` para rodar qualquer ação da central.
- `RequestFullscreenAsync(bool)`, `IsFullscreen`, `PixelSize` (muda em tela cheia), `IsAsleep`.
- `Logger` (`Microsoft.Extensions.Logging`), `Theme`, `Stopping`, e via `Host`: `ResourceDirectory`,
  `LoggerFactory`, `Time` (`TimeProvider`, para testes).

## Tela de configurações

`IWidgetInstance.SettingsComponent` devolve o tipo de um componente Razor, normalmente derivado de
`WidgetSettingsComponent<MinhaInstancia>`. O host renderiza com o parâmetro `Instance` e o
`IWidgetContext` em cascata, já com MudBlazor carregado e com o tema do manager.

## Assemblies compartilhados

O host carrega uma cópia só de `OpenWigi.Sdk`, `Microsoft.AspNetCore.Components*`,
`Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions` e
`MudBlazor` (9.x) — lista em `OpenWigiSdk.SharedAssemblies`. O plugin compila contra eles mas não leva
cópia na pasta:

```xml
<PackageReference Include="victorperez2911.OpenWigi.Sdk" Version="2.0.0-preview.1" ExcludeAssets="runtime" />
<PackageReference Include="MudBlazor" Version="9.*" ExcludeAssets="runtime" />
```

## Versão

`OpenWigiSdk.Version` = 2.0. O host carrega widget com o mesmo major e minor menor ou igual ao dele.

## Exemplo mínimo

```csharp
[assembly: OpenWigiWidget(typeof(Contador))]

public sealed class Contador : IWidget
{
    public WidgetManifest Manifest { get; } = new()
    {
        Id = new("6d1f3a52-8c2e-4b8f-9b8e-0f5f6a3c1d20"),
        Name = "Contador",
        Version = new(1, 0),
        SupportedSizes = [WidgetSize.Small, WidgetSize.Wide],
    };

    public ValueTask<IWidgetInstance> CreateInstanceAsync(IWidgetContext ctx, CancellationToken ct) =>
        ValueTask.FromResult<IWidgetInstance>(new ContadorInstancia(ctx));
}

public sealed class ContadorInstancia(IWidgetContext ctx) : IWidgetInstance
{
    private int _toques;

    public ValueTask<WidgetFrame> RenderAsync(RenderReason reason, CancellationToken ct) =>
        ValueTask.FromResult(WidgetFrame.Solid(ctx.PixelSize, (byte)_toques, 0, 0));

    public async ValueTask OnTouchAsync(TouchEvent toque, CancellationToken ct)
    {
        if (toque.Kind != TouchKind.Tap) return;
        _toques++;
        await ctx.Settings.SetJsonAsync("toques", _toques, cancellationToken: ct);
        ctx.RequestRender();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

O mesmo widget, com teste, está em `tests/OpenWigi.Sdk.Tests/ContractTests.cs`.
