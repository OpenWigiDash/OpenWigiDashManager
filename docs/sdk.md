# OpenWigi.Sdk — contrato dos widgets (v2.0)

Pacote `victorperez2911.OpenWigi.Sdk` no nuget.org (assembly `OpenWigi.Sdk`, `net10.0-windows`). Substitui o `WigiDashWidgetFramework` (v1, net472) sem
compatibilidade binária: plugin antigo precisa ser portado.

## Peças

| Tipo | Papel | No v1 era |
|---|---|---|
| `[assembly: OpenWigiWidget(typeof(X))]` | O host acha a entrada do plugin sem varrer o assembly | varredura por `IWidgetObject` |
| `IWidget` + `WidgetManifest` | Ficha (id, nome, versão, tamanhos, tela cheia) e fábrica de instâncias | `IWidgetObject` / `IWidgetBase` |
| `IWidget.GetPreviewAsync` | Ícone do widget no assistente "Adicionar widget", pedido no menor tamanho suportado; sem ele o manager usa um genérico | `WidgetThumbnail` |
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

O plugin não declara tema: os componentes Mud herdam as cores do manager e trocam entre claro e
escuro junto com ele. Para seguir o tema, monte a tela com componentes Mud e, em CSS próprio, use as
variáveis `--mud-palette-*` (ex.: `var(--mud-palette-text-secondary)`) no lugar de cores fixas.
`WidgetTheme` (em `IWidgetContext.Theme`) é outra coisa: as cores para desenhar no visor do aparelho.

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

## Publicar no nuget.org

Plugin da comunidade é um pacote do nuget.org com `packageType` `OpenWigiDashPlugin`. O pacote do SDK já
traz o que monta esse pacote: basta ligar `OpenWigiDashPlugin` no `.csproj` e rodar `dotnet pack`.

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <PackageId>SeuNome.OpenWigiDash.Discord</PackageId>
    <Version>1.0.0</Version>
    <Authors>SeuNome</Authors>
    <Description>Mostra quem está falando no canal de voz.</Description>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <OpenWigiDashPlugin>true</OpenWigiDashPlugin>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="victorperez2911.OpenWigi.Sdk" Version="2.0.0-preview.2" ExcludeAssets="runtime" />
    <PackageReference Include="MudBlazor" Version="9.*" ExcludeAssets="runtime" />
    <None Include="README.md" Pack="true" PackagePath="\" />
  </ItemGroup>
</Project>
```

`dotnet pack -c Release` gera `SeuNome.OpenWigiDash.Discord.1.0.0.nupkg` com:

| Onde | O quê |
| --- | --- |
| `.nuspec` | `packageType` `OpenWigiDashPlugin`, tag `openwigidash-plugin`, dependência de `victorperez2911.OpenWigi.Sdk` |
| `plugin/` | a saída do `dotnet publish`, sem os assemblies compartilhados; vira `Widgets\<id do pacote>` na instalação |
| `plugin/plugin.json` | `{ "entry": "<sua dll>" }`, gerado se o projeto não tiver um (a pasta instalada tem o nome do pacote, não da DLL) |

O pacote não tem `lib/`: um plugin não é biblioteca e não pode ser referenciado por outro projeto.

- **Id**: `<Autor>.OpenWigiDash.<Nome>`.
- **Versão**: SemVer. Enquanto o SDK for preview, o plugin também precisa ser (`1.0.0-preview.1`): o
  NuGet recusa versão estável que depende de preview (NU5104). O Manager oferece a versão mais nova
  cuja dependência do SDK tem limite inferior compatível com o host (mesmo major, minor menor ou igual; ver [Versão](#versão)).
- **`plugin.json` instalado**: o instalador acrescenta `"package": { "id": "...", "version": "..." }` para
  saber de onde o plugin veio. O host ignora esse campo.
- **Conferir o pacote**: `scripts/Test-PluginPackage.ps1 -Project <seu.csproj>`.

Publicação pelo GitHub Actions com Trusted Publishing (sem chave guardada), no padrão de
`.github/workflows/sdk-publish.yml`:

```yaml
permissions:
  contents: read
  id-token: write
steps:
  - uses: actions/checkout@v4
  - uses: actions/setup-dotnet@v4
    with:
      dotnet-version: 10.0.x
  - run: dotnet pack -c Release -o nupkgs /p:Version=${{ github.ref_name }}
  - uses: NuGet/login@v1
    id: nuget
    with:
      user: ${{ vars.NUGET_USER }}
  - run: dotnet nuget push nupkgs/*.nupkg --api-key ${{ steps.nuget.outputs.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json
```
