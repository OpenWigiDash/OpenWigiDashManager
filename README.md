# OpenWigiDashManager

Gerenciador alternativo, em .NET 10, para o painel USB WigiDash da G.SKILL. O manager oficial foi
descontinuado; este projeto recria o que ele faz e resolve três limitações:

1. Roda em .NET 10, com plugins modernos e UI em Blazor dentro do WPF.
2. Funciona com vários usuários do Windows: a tela não congela quando outra pessoa entra.
3. Reconhece long-press (e double tap) e entrega aos plugins.

O plano e as fases estão nos [milestones](../../milestones). O protocolo USB está em
[docs/protocol.md](docs/protocol.md).

## Instalar

Baixe o `OpenWigiDash-Setup.exe` da [última Release](../../releases) e rode. Ele instala o serviço
`OpenWigiDash`, o manager (tray + janela) e os widgets que vêm junto, e coloca o manager para abrir ao
entrar no Windows. Não precisa do .NET instalado; o WebView2 Runtime é instalado se faltar.

Feche o manager oficial da G.SKILL antes de usar: só um programa abre o aparelho por vez.

Para atualizar, rode o setup da versão nova por cima. Para remover, use Configurações > Aplicativos >
"OpenWigiDash". O que fica em cada pasta, como atualizar e como alternar com o serviço de desenvolvimento
no mesmo PC estão em [docs/instalador.md](docs/instalador.md).

## Sem o aparelho: WigiDash virtual

Em **Ajustes > Aparelho ativo** dá para trocar o WigiDash de verdade (USB) por um **WigiDash virtual**.
A tela dele aparece na prévia do manager ("Destacar prévia") e o mouse nela vira toque: clicar dá tap,
segurar dá long-press e arrastar dá swipe. A troca vale na hora, sem reiniciar o serviço.

Serve para experimentar o manager e os widgets sem ter o aparelho, para usar pelo RDP e para desenvolver.
Detalhes em [docs/multiusuario.md](docs/multiusuario.md#sem-o-aparelho-wigidash-virtual).

## Manager

As telas:
- **Widgets e páginas** (a tela inicial): estado do aparelho, grade 5x4 com cada widget desenhando ao vivo, e embaixo as páginas em miniatura. Dá para adicionar, mover, redimensionar e tirar widgets, e criar e renomear páginas. Com um widget selecionado, as configurações do plugin (em Razor) ficam no painel ao lado, e o que mudar já aparece no tile. "Destacar prévia" abre a tela do aparelho numa janela própria, onde o mouse vira toque.
- **Ajustes:** aparelho ativo (USB ou virtual), brilho, iniciar com o Windows e tema claro ou escuro.

Fechar a janela só esconde; o painel continua rodando pelo tray.

| Opção | Efeito |
| --- | --- |
| `--tray` | Começa só no tray. É o que o "Abrir ao entrar no Windows" usa |
| `--open /widgets` | Abre direto numa tela |
| `--plugins <pasta>` | Pasta de plugins alternativa |
| `--preview` | Abre a prévia destacada |

## Plugins

Cada plugin é um pacote no nuget.org (`packageType` `OpenWigiDashPlugin`), publicado por Trusted
Publishing quando o repositório do plugin recebe uma tag de versão. Os plugins oficiais:

| Plugin | Pacote |
| --- | --- |
| [Discord](https://github.com/victorperez2911/WigiDash-DiscordWidGet/tree/net10): 15 widgets para mute, deafen, canais, volume, soundboard e mais | `victorperez2911.OpenWigiDash.Discord` |
| [Stream Deck Mirror](https://github.com/victorperez2911/WigiDash-StreamDeckMirror-WidGet/tree/net10): espelha um Virtual Device do Stream Deck | `victorperez2911.OpenWigiDash.StreamDeckMirror` |

A loja de plugins dentro do manager ([#47](../../issues/47)) e o `owd plugin` para instalar pela linha de
comando ([#46](../../issues/46)) ainda estão planejados. Por enquanto, instale com o `deploy.ps1` do
repositório do plugin ou extraia o zip da release dele em `%LOCALAPPDATA%\OpenWigiDash\Widgets\`.
Feche o manager pelo tray antes de trocar a DLL.

### Escrever um plugin

O contrato é o pacote [`victorperez2911.OpenWigi.Sdk`](https://www.nuget.org/packages/victorperez2911.OpenWigi.Sdk)
(hoje 2.0.0-preview.3, `net10.0-windows`). A referência para autores é [docs/sdk.md](docs/sdk.md): widgets,
quadros, toques, configurações em Razor, como montar e publicar o pacote, e o ícone (PNG quadrado com o
desenho do widget; os plugins oficiais levam o selo do OpenWigiDash no canto). Os widgets de exemplo estão
em `samples/OpenWigi.Samples.Clock`.

## Desenvolver

Precisa de Windows e do SDK do .NET 10.

```powershell
dotnet build
dotnet test
```

### Serviço de desenvolvimento

O manager precisa do serviço `OpenWigiDash` rodando. No PC de desenvolvimento, em vez do instalador:

```powershell
scripts\Setup-DevService.ps1             # uma vez, como administrador
scripts\Deploy-DevService.ps1            # sem admin: publica, troca os arquivos e reinicia o serviço
scripts\Deploy-Widget.ps1                # instala os widgets de exemplo para o seu usuário
dotnet run --project src/OpenWigi.Manager
```

O serviço de dev e o instalado têm o mesmo nome, então é um ou outro
([docs/instalador.md](docs/instalador.md#no-pc-de-desenvolvimento)). O resto (serviço no console, RDP,
outro usuário) está em [docs/multiusuario.md](docs/multiusuario.md).

### Sem o aparelho físico

Tudo funciona com o WigiDash virtual, inclusive gestos, troca de usuário e a prévia:

- `scripts\Deploy-DevService.ps1 -Virtual` grava o virtual como aparelho do serviço de dev;
- ou rode o serviço no console: `dotnet run --project src/OpenWigi.Service -- --OpenWigi:Device=Virtual`;
- ou, com o serviço já rodando, troque em **Ajustes > Aparelho ativo**.

Depois abra a prévia no manager (ou `--preview`) e use o mouse como dedo. Para testar o instalador num
Windows limpo, `scripts\Test-InstallerSandbox.ps1 -Build` instala no Windows Sandbox, liga o virtual lá
dentro e abre o manager com a prévia ([docs/instalador.md](docs/instalador.md#testar-num-windows-limpo-windows-sandbox)).

### Com o aparelho

Feche o manager oficial antes. O `owd` fala direto com o aparelho:

```powershell
dotnet run --project src/OpenWigi.Cli -- info
dotnet run --project src/OpenWigi.Cli -- draw pattern
dotnet run --project src/OpenWigi.Cli -- touch --long 600
```

`owd` sem argumentos lista todos os comandos.

`scripts/Test-Device.ps1` compila, libera o aparelho do manager oficial (`-StopOfficial`), roda o
`owd` no hardware e guarda o log em `.diagnostics/device-check/`. Com `-Touch` registra os toques por
alguns segundos; com `-RestoreOfficial` religa o manager oficial no fim. No VS Code: tarefas
`device check`, `device check + touch` e `device check & restore official`, e o launch
`owd touch (aparelho real)` para depurar.

### Testes da UI

`scripts\Test-ManagerUi.ps1` abre o manager com a porta de depuração do WebView2 e roda os scripts
Playwright de `tests/ui`. Precisa do serviço rodando e dos widgets de exemplo instalados, e mexe no
layout do seu usuário.

## Publicar

| Tag | O que sai |
| --- | --- |
| `vX.Y.Z` | Release do GitHub com o `OpenWigiDash-Setup.exe` e o MSI (`scripts\Build-Installer.ps1` faz o mesmo localmente) |
| `sdk-vX.Y.Z` | O pacote `victorperez2911.OpenWigi.Sdk` no nuget.org, por Trusted Publishing |

## Estrutura

| Projeto | O que é |
| --- | --- |
| `src/OpenWigi.Device` | WinUSB por P/Invoke, protocolo do aparelho e reconhecimento de gestos |
| `src/OpenWigi.Cli` | `owd`, ferramenta de diagnóstico |
| `src/OpenWigi.Ipc` | Mensagens e canal (named pipe) entre o serviço e os agentes |
| `src/OpenWigi.Service` | Serviço Windows dono do USB (ou do WigiDash virtual); decide qual sessão desenha ([docs/multiusuario.md](docs/multiusuario.md)) |
| `src/OpenWigi.Sdk` | Contrato dos widgets (.NET 10), publicado no nuget.org; ver [docs/sdk.md](docs/sdk.md) |
| `src/OpenWigi.Host` | Host de plugins: carrega widgets, layout, compositor e roteamento de toques ([docs/host.md](docs/host.md)) |
| `src/OpenWigi.Manager` | **O manager**: app WPF + Blazor (MudBlazor) no tray de cada usuário. É o agente (desenha os widgets quando tem a tela) e a janela de configuração |
| `src/OpenWigi.Agent` | Agente de console, sem janela, para depurar o serviço e o host |
| `installer` | Instalador WiX: `OpenWigiDash.msi` e o bundle `OpenWigiDash-Setup.exe` ([docs/instalador.md](docs/instalador.md)) |
| `samples/OpenWigi.Samples.Clock` | Widgets de exemplo: relógio e contador com long-press |
| `tests/OpenWigi.*.Tests` | Testes do protocolo e dos gestos, do serviço e do IPC, do host e do SDK, com transporte e relógio falsos |
| `tests/ui` | Testes da UI do manager (Playwright pelo WebView2) |
