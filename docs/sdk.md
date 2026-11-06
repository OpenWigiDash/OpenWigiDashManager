# OpenWigi.Sdk — contrato dos widgets (v3.2)

Pacote `OpenWigiDash.Sdk` no nuget.org (assembly `OpenWigi.Sdk`, `net10.0-windows`). Substitui o `WigiDashWidgetFramework` (v1, net472) sem
compatibilidade binária: plugin antigo precisa ser portado.

## Peças

| Tipo | Papel | No v1 era |
|---|---|---|
| `[assembly: OpenWigiWidget(typeof(X))]` | O host acha a entrada do plugin sem varrer o assembly | varredura por `IWidgetObject` |
| `IWidget` + `WidgetManifest` | Ficha (id, nome, versão, tamanhos, tela cheia) e fábrica de instâncias | `IWidgetObject` / `IWidgetBase` |
| `IWidget.GetPreviewAsync` | Ícone do widget no assistente "Adicionar widget", pedido no menor tamanho suportado e já no tamanho em pixels da área de conteúdo; sem ele o manager usa um genérico | `WidgetThumbnail` |
| `IWidgetInstance` | Widget na tela: quadros, toques, repouso, tela cheia, ações, remoção | `IWidgetInstance` + `WithRemoval` + `WithFullscreen` |
| `IWidgetContext` | O que o host oferece a uma instância | `IWidgetManager` (global) |
| `WidgetSettingsComponent<T>` | Tela de configurações em Razor (MudBlazor) | `UserControl` WPF |
| `[assembly: OpenWigiPluginSettings(typeof(X))]` + `PluginSettingsComponent` | Configurações do plugin inteiro, uma vez só na página do plugin | — |

## Grade

**10 colunas por 8 linhas de meias células**, com **4 px** entre elas e a mesma folga entre a grade e a
borda da tela (5 px nas laterais, 4 px no topo e no rodapé), dentro dos 1016x592 da tela
(`WidgetGrid.Area` é a parte coberta pelas células). As colunas têm 97 px. As linhas alternam 69 px
(pares) e 70 px (ímpares), então duas linhas somam sempre 143 px em qualquer posição.

| `WidgetSize` | Meias células | Células em pixels |
|---|---|---|
| `Mini` | 1x1 | 97x69 (ou 97x70 numa linha ímpar) |
| `Small` | 2x2 | 198x143 |
| `Wide` | 4x2 | 400x143 |
| `Large` | 4x4 | 400x290 |
| `FullScreen` | 10x8 | 1006x584 (a tela inteira é o modo tela cheia, não um tamanho da grade) |

**O 1x1 é para botão com ícone.** Com 97x69 (87x59 com a moldura completa) não cabe texto legível.
Ofereça `Mini` só nos widgets que são um toque num ícone (mutar, microfone, atalho); widgets com texto,
lista ou número começam em `Small`.

Qualquer tamanho de 1x1 a 10x8 vale (ex.: `new WidgetSize(3, 2)` = 299x143). `WidgetSize.CellSpan` dá
os pixels das células começando numa linha par; `WidgetGrid.CellBounds(coluna, linha, tamanho)` dá o
retângulo exato. Os valores de `WidgetGrid` são `static readonly`, não `const`, para não ficarem
copiados dentro do plugin. Não conte com esses números no plugin: desenhe sempre em `context.PixelSize`.

## Moldura e área de conteúdo

Cada página tem uma moldura (`TileFrame`), escolhida pela pessoa no manager entre valores fixos:

| Opção | Valores |
|---|---|
| `Spacing` (respiro em volta do tile, além dos 4 px da grade) | 0, 1, 2 ou 3 px |
| `Border` | 0, 1, 2, 3 ou 4 px |
| `Rounded` | cantos retos ou arredondados (raio fixo de 12 px, menor em tile pequeno) |
| `BorderColor`, `Background` | cores escolhidas pela pessoa |

O padrão (`TileFrame.Default`) é o visual de antes da moldura: sem respiro, sem borda, fundo preto e
cantos arredondados, com o conteúdo ocupando a célula inteira. `TileFrame.Framed` é a moldura completa
sugerida (3 px de respiro, borda de 2 px).

**Quem desenha o quê:** o host pinta o fundo da página, a borda e o canto arredondado. O widget
desenha só a área de conteúdo, que é a célula menos `Spacing + Border` de cada lado:
`context.PixelSize`. Desenhe o quadro inteiro nessa área, sem canto arredondado próprio e sem
margem para a moldura. O host corta o canto do conteúdo, então deixe uns pixels livres nos cantos
se tiver texto ali. `WidgetTheme.CornerRadius` é só para painéis dentro do widget.

**Teste todos os tamanhos:** a folga (`TileFrame.Inset`) vai de 0 a 7 px (`TileFrame.InsetOptions`),
então um `Small` pode receber de 184x129 a 198x143. Use `TileFrame.ContentSize(celula)` para gerar os
tamanhos nos testes. Em tela cheia não há moldura e `PixelSize` é a tela inteira.

**Quando o tamanho muda:** a pessoa troca a moldura da página, ou move um widget de número ímpar de
linhas para uma linha de outra paridade. O host chama `OnSizeChangedAsync(novoTamanho)` e depois
`RenderAsync(SizeChanged)`. Quem guarda bitmap em cache do tamanho antigo deve refazer aí.
`context.Frame` diz a moldura atual, se o widget quiser combinar cores com ela.

## Quadros

`WidgetFrame` é um buffer cru em `Bgra32` (o que System.Drawing, SkiaSharp e WPF geram) ou `Rgb565`
(nativo da tela, vai sem conversão), com `Stride` opcional e região suja opcional.

- **Puxado:** o host chama `RenderAsync(reason)` e recebe o quadro inteiro (`Initial`, `Requested`,
  `Resumed`, `FullscreenChanged`, `ThemeChanged`, `SizeChanged`). Widget estático só implementa isso.
- **Empurrado:** a instância chama `context.PresentAsync(frame)` quando tem novidade (captura, animação).
  O quadro é sempre completo; `Dirty` diz o que mudou para o host mandar só esse pedaço pelo USB.
  O `ValueTask` completa quando o host aceitou: quem gera mais rápido que o USB espera ali.
- `context.RequestRender()` pede um `RenderAsync(Requested)`; pedidos seguidos se juntam.
- Depois de entregue, o buffer é do host: não reescreva o mesmo array.

## Toques

`TouchEvent(Kind, X, Y, Duration, Progress)`, com X/Y relativos à área de conteúdo e sempre dentro
dela: um toque na moldura ou no espaço até o widget vizinho chega como a beirada mais perto. `TouchKind` tem os mesmos
nomes e ordem do `GestureKind` do `OpenWigi.Device` (um teste guarda isso):

`PressStarted` → `PressProgress` (0..1, para desenhar o anel do long-press) → `LongPress` (dedo ainda
na tela) → `PressEnded` (fecha todo `PressStarted`). Além disso `Tap`, `DoubleTap` (logo depois do
`Tap`) e `SwipeLeft/Right/Up/Down`. `IsGesture` separa os gestos "de ação" (o antigo `ClickEvent`) dos
eventos de pressão.

## Ciclo de vida

1. Host carrega o assembly, lê o atributo, valida `Manifest.Validate()` e chama `IWidget.LoadAsync(host)`.
2. Para cada widget no layout: `CreateInstanceAsync(context)` e logo `RenderAsync(Initial)`.
3. Durante a vida: `OnTouchAsync`, `OnSleepAsync`/`OnWakeAsync` (+ `RenderAsync(Resumed)`),
   `OnFullscreenChangedAsync` (+ `RenderAsync(FullscreenChanged)`), `OnSizeChangedAsync` (+ `RenderAsync(SizeChanged)`),
   `OnThemeChangedAsync`, `OnActionAsync`.
4. Pessoa tirou o widget: `OnRemovedAsync` e depois `DisposeAsync`. Descarregar o plugin só chama `DisposeAsync`.

O host nunca chama dois métodos da mesma instância ao mesmo tempo. Todos os ganchos, exceto
`RenderAsync` e `DisposeAsync`, têm implementação vazia.

## Contexto do host (`IWidgetContext`)

- `Settings`: chave/valor em texto, por instância, no perfil do usuário dono do layout; `cloneable:
  false` para o que não deve ir junto ao duplicar (Duplicar, ou Copiar e Colar, no editor: a cópia é uma
  instância nova, que já abre com as configurações clonáveis da original). `GetJsonAsync`/`SetJsonAsync`
  para objetos. Mover o widget para outra página não é cópia: a instância e tudo dela vão junto.
- `Files`: `ImportAsync` (copia arquivo escolhido), `CreateAsync` (reserva caminho), `GetPathAsync`,
  `DeleteAsync`. Somem com a remoção do widget. Ao duplicar, a cópia ganha os próprios arquivos (copiados).
- `Actions`: gatilhos que o widget dispara (`RegisterTriggerAsync`/`FireTriggerAsync`), ações que ele
  executa (`RegisterActionAsync` → `OnActionAsync`), ações que a pessoa ligou à instância
  (`GetBoundActionsAsync(setId)`) e `InvokeAsync` para rodar qualquer ação da central.
- `RequestFullscreenAsync(bool)`, `IsFullscreen`, `PixelSize` (área de conteúdo; muda em tela cheia e com a moldura), `Frame`, `IsAsleep`.
- `Logger` (`Microsoft.Extensions.Logging`), `Theme`, `Stopping`, e via `Host`: `ResourceDirectory`,
  `LoggerFactory`, `Time` (`TimeProvider`, para testes).
- `OpenPluginSettingsAsync()`: abre a página do plugin no manager, na aba Configurações (ver
  [Configurações do plugin](#configurações-do-plugin)). Devolve `false` se o plugin não tem essa tela.

## Tela de configurações

`IWidgetInstance.SettingsComponent` devolve o tipo de um componente Razor, normalmente derivado de
`WidgetSettingsComponent<MinhaInstancia>`. O host renderiza com o parâmetro `Instance` e o
`IWidgetContext` em cascata, já com MudBlazor carregado e com o tema do manager.

O plugin não declara tema: os componentes Mud herdam as cores do manager e trocam entre claro e
escuro junto com ele. Para seguir o tema, monte a tela com componentes Mud e, em CSS próprio, use as
variáveis `--mud-palette-*` (ex.: `var(--mud-palette-text-secondary)`) no lugar de cores fixas.
`WidgetTheme` (em `IWidgetContext.Theme`) é outra coisa: as cores para desenhar no visor do aparelho.

## Configurações do plugin

O que vale para todos os widgets do plugin (uma conta, uma conexão, uma chave de API) não entra na tela de cada
widget: o manager mostra uma vez só, na página do plugin (Loja de plugins > Detalhes ou Configurar, aba
Configurações). Declare o componente no assembly, ao lado dos widgets:

```csharp
[assembly: OpenWigiWidget(typeof(MeuWidget))]
[assembly: OpenWigiPluginSettings(typeof(MinhaConta))]
```

```razor
@inherits PluginSettingsComponent

<MudTextField T="string" @bind-Value="_chave" Label="Chave da API" Variant="Variant.Outlined" />
<MudButton OnClick="Salvar">Salvar</MudButton>
```

- Um por assembly. O componente deriva de `PluginSettingsComponent`, que recebe o `IWidgetHost` do plugin em
  cascata (`Host`: pasta, log, relógio). Não há instância: a tela abre mesmo sem nenhum widget do plugin no layout.
- Guarde por conta própria, fora das configurações da instância (essas são por widget e vão junto ao duplicar),
  por exemplo em `%LOCALAPPDATA%\OpenWigiDash\WidgetData\<plugin>`.
- A tela do widget continua sabendo quando falta configurar o plugin (ex.: conta não autorizada). Mostre o aviso
  e um botão que chama `Context.OpenPluginSettingsAsync()`: o manager leva a pessoa à página do plugin. Quando o
  plugin declara a tela, o manager também põe um atalho "Configurações do plugin" no alto da aba Configurações do
  widget.

## Biblioteca de ícones

O SDK traz uma biblioteca de ícones para todos os widgets usarem, separada em categorias (Áudio, Câmera e
imagem, Dispositivos, Mídia, Sistema, Comunicação, Lugares e transporte, Símbolos e setas). Ela cresce a cada
versão do SDK; as chaves que já saíram nunca mudam, então guarde a chave nas configurações.

- `WidgetIcons.All`, `Categories`, `Find(chave)`, `InCategory(categoria)` e `Search(texto)` (sem diferenciar
  acentos nem maiúsculas).
- `WidgetIcon`: `Key`, `Name`, `Category`, `Glyph` (caractere da fonte de ícones do Windows), `Accent` (segundo
  glifo opcional, pequeno no canto, para ícones combinados como `webcam-mic`), `Tags` (sinônimos para a busca) e
  `PathData` (vetor SVG 24x24, que vale no lugar do glifo quando existe; reservado para ícones que a fonte não tem).
- `WidgetIcons.FontFamily` é a fonte para CSS; `FontFamilyNames` dá as fontes em ordem para desenhar no tile
  (Segoe Fluent Icons no Windows 11, Segoe MDL2 Assets no Windows 10, com os mesmos códigos).

**Deixar a pessoa escolher.** O manager oferece a janela de escolha, com uma aba por categoria e busca.
Na tela de configurações:

```razor
@inject IWidgetIconPicker IconPicker

<MudButton OnClick="Pick"><WidgetIconView IconKey="@_icon" Size="28" /> Trocar ícone</MudButton>

@code {
    private string? _icon;

    private async Task Pick()
    {
        var choice = await IconPicker.PickAsync(new WidgetIconPickerOptions
        {
            Title = "Ícone do botão",
            Current = _icon,
            Categories = ["audio", "devices"], // opcional: só estas abas
        });
        if (choice is null)
            return; // fechou sem escolher
        _icon = choice.Key; // null = "Automático", se você passou Options.Automatic
        await Context.Settings.SetAsync("icon", _icon ?? "");
    }
}
```

A janela abre na raiz do manager, por cima do canvas. Não monte um overlay próprio dentro do painel de
configurações: ele fica atrás dos cantos dos widgets. `Automatic` mostra a opção "Automático" com a prévia do
ícone que o widget usaria sozinho, `Hint` põe um texto embaixo e `Extra` junta ícones do próprio plugin (o
plugin é quem desenha esses no tile, porque `WidgetIcons.Find` não os conhece).

**Desenhar no tile.** Meça o contorno do glifo e escale pelo maior lado, porque a caixa da fonte tem muita
sobra em volta do desenho (com System.Drawing: `GraphicsPath.AddString`, `GetBounds` e uma `Matrix`). Para
ícone combinado, `WidgetIconLayout.Place(icone, caixa, tamanho)` dá a caixa e o tamanho do principal e o
círculo do destaque: pinte o círculo com a cor de fundo e desenhe o `Accent` centrado nele. Assim o tile
fica igual à janela de escolha.

## Assemblies compartilhados

O host carrega uma cópia só de `OpenWigi.Sdk`, `Microsoft.AspNetCore.Components*`,
`Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions` e
`MudBlazor` (9.x) — lista em `OpenWigiSdk.SharedAssemblies`. O plugin compila contra eles mas não leva
cópia na pasta:

```xml
<PackageReference Include="OpenWigiDash.Sdk" Version="3.2.0-preview.1" ExcludeAssets="runtime" />
<PackageReference Include="MudBlazor" Version="9.*" ExcludeAssets="runtime" />
```

## Versão

`OpenWigiSdk.Version` = 3.2. O host carrega widget com o mesmo major e minor menor ou igual ao dele.

**3.2:** configurações do plugin inteiro na página do plugin (`OpenWigiPluginSettingsAttribute`,
`PluginSettingsComponent`, `IWidgetContext.OpenPluginSettingsAsync`). Plugin feito para 3.0 ou 3.1 continua
carregando; plugin compilado contra 3.2 pede o manager com SDK 3.2.

**3.1:** biblioteca de ícones (`WidgetIcons`, `IWidgetIconPicker`, `WidgetIconView`, `WidgetIconLayout`). Plugin
feito para 3.0 continua carregando; plugin compilado contra 3.1 pede o manager com SDK 3.1.

**De 2.0 para 3.0:** a grade passou a 10x8 meias células e `PixelSize` passou a ser a área de conteúdo.
Para portar: troque `WidgetSize.PixelSize` por `context.PixelSize` (ou `CellSpan` só para posição),
`GetPreviewAsync(size, ct)` por `GetPreviewAsync(size, pixelSize, ct)` e desenhe nesse `pixelSize`,
tire o canto arredondado que o widget pintava por conta própria e recompile contra o SDK 3.0.
`Small`, `Wide`, `Large` e `FullScreen` continuam com os mesmos pixels de célula.

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

## Testar sem o aparelho

Com **Ajustes > Aparelho ativo > WigiDash virtual** no manager, o widget roda como se o WigiDash estivesse
ligado. A tela aparece na prévia, onde o mouse vira toque. Ver
[multiusuario.md](multiusuario.md#sem-o-aparelho-wigidash-virtual).

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
    <PackageIcon>icon.png</PackageIcon>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <OpenWigiDashPlugin>true</OpenWigiDashPlugin>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="OpenWigiDash.Sdk" Version="3.2.0-preview.1" ExcludeAssets="runtime" />
    <PackageReference Include="MudBlazor" Version="9.*" ExcludeAssets="runtime" />
    <None Include="README.md" Pack="true" PackagePath="\" />
    <None Include="icon.png" Pack="true" PackagePath="\" />
  </ItemGroup>
</Project>
```

`dotnet pack -c Release` gera `SeuNome.OpenWigiDash.Discord.1.0.0.nupkg` com:

| Onde | O quê |
| --- | --- |
| `.nuspec` | `packageType` `OpenWigiDashPlugin`, tag `openwigidash-plugin`, dependência de `OpenWigiDash.Sdk` |
| `plugin/` | a saída do `dotnet publish`, sem os assemblies compartilhados; vira `Widgets\<id do pacote>` na instalação |
| `plugin/plugin.json` | `{ "entry": "<sua dll>" }`, gerado se o projeto não tiver um (a pasta instalada tem o nome do pacote, não da DLL) |

O pacote não tem `lib/`: um plugin não é biblioteca e não pode ser referenciado por outro projeto.

- **Id**: `<Autor>.OpenWigiDash.<Nome>`. O prefixo `OpenWigiDash.` é dos plugins oficiais (publicados pela Org OpenWigiDash).
- **Ícone**: PNG quadrado de 128x128 ou 256x256 (até 1 MB), com o desenho do próprio widget, em
  `PackageIcon`. É o ícone que aparece no nuget.org e na Loja do Manager. Sem ícone, os dois mostram o
  genérico.
  - Plugins oficiais (publicados pela Org `OpenWigiDash`) levam o logo do OpenWigiDash como selo no canto
    inferior direito: cerca de 1/3 da largura do ícone, por cima do desenho, com uma borda fina da cor do
    fundo para destacar. Use `assets/branding/png/openwigidash-256.png` (ou o `.svg`) como fonte do selo.
  - Plugins da comunidade não usam o logo, para não parecer oficiais. Também não use logos de marcas de
    terceiros (Discord, Elgato etc.): faça um desenho próprio.
- **Versão**: SemVer. Enquanto o SDK for preview, o plugin também precisa ser (`1.0.0-preview.1`): o
  NuGet recusa versão estável que depende de preview (NU5104). O Manager oferece a versão mais nova
  cuja dependência do SDK tem limite inferior compatível com o host (mesmo major, minor menor ou igual; ver [Versão](#versão)).
- **`plugin.json` instalado**: o instalador acrescenta `"package": { "id": "...", "version": "..." }` para
  saber de onde o plugin veio. O host ignora esse campo.
- **Conferir o pacote**: `scripts/Test-PluginPackage.ps1 -Project <seu.csproj>`.

Publicação pelo GitHub Actions com Trusted Publishing (sem chave guardada), no padrão de
`.github/workflows/sdk-publish.yml`:

```yaml
on:
  push:
    tags: ['v*']   # tag v1.2.0-preview.1 publica a versão 1.2.0-preview.1
permissions:
  contents: read
  id-token: write
jobs:
  publish:
    runs-on: windows-latest
    defaults:
      run:
        shell: pwsh
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - run: |
          $version = "${{ github.ref_name }}".TrimStart('v')
          dotnet pack -c Release -o nupkgs /p:Version=$version
      - uses: NuGet/login@v1
        id: nuget
        with:
          user: ${{ vars.NUGET_USER }}
      # No runner Windows o curinga (nupkgs/*.nupkg) não chega expandido ao dotnet: um push por arquivo.
      - run: |
          Get-ChildItem nupkgs -Filter *.nupkg | ForEach-Object {
            dotnet nuget push $_.FullName --api-key "${{ steps.nuget.outputs.NUGET_API_KEY }}" --source https://api.nuget.org/v3/index.json --skip-duplicate
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          }
```

Antes da primeira publicação, crie no nuget.org (seu usuário > Trusted Publishing) uma política com o
repositório, o nome do arquivo do workflow e o id exato do pacote em "Glob Patterns", e crie a variável
`NUGET_USER` (Settings > Secrets and variables > Actions > Variables) com o seu usuário do nuget.org.
