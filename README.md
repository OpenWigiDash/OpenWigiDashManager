# OpenWigiDashManager

Gerenciador alternativo, em .NET 10, para o painel USB WigiDash. O manager oficial foi
descontinuado; este projeto recria o que ele faz e resolve três limitações:

1. Roda em .NET 10, com plugins modernos e UI em Blazor dentro do WPF.
2. Funciona com vários usuários do Windows: a tela não congela quando outra pessoa entra.
3. Reconhece long-press (e double tap) e entrega aos plugins.

O plano e as fases estão nos [milestones](../../milestones). O protocolo USB está em
[docs/protocol.md](docs/protocol.md).

## Estrutura

| Projeto | O que é |
| --- | --- |
| `src/OpenWigi.Device` | WinUSB por P/Invoke, protocolo do aparelho e reconhecimento de gestos |
| `src/OpenWigi.Cli` | `owd`, ferramenta de diagnóstico |
| `src/OpenWigi.Ipc` | Mensagens e canal (named pipe) entre o serviço e os agentes |
| `src/OpenWigi.Service` | Serviço Windows dono do USB; decide qual sessão desenha ([docs/multiusuario.md](docs/multiusuario.md)) |
| `src/OpenWigi.Sdk` | Contrato dos widgets (.NET 10), ver [docs/sdk.md](docs/sdk.md) |
| `src/OpenWigi.Host` | Host de plugins: carrega widgets, layout, compositor e roteamento de toques ([docs/host.md](docs/host.md)) |
| `src/OpenWigi.Manager` | **O manager**: app WPF + Blazor (MudBlazor) no tray de cada usuário. É o agente (desenha os widgets quando tem a tela) e a janela de configuração |
| `src/OpenWigi.Agent` | Agente de console, sem janela, para depurar o serviço e o host |
| `samples/OpenWigi.Samples.Clock` | Widgets de exemplo: relógio e contador com long-press |
| `tests/OpenWigi.Device.Tests` | Testes do protocolo e dos gestos, com transporte e relógio falsos |
| `tests/OpenWigi.Service.Tests` | Testes da política de posse, do broker e do canal IPC |

## Usar o `owd`

Feche o manager oficial antes: só um programa abre o aparelho por vez.

```powershell
dotnet run --project src/OpenWigi.Cli -- info
dotnet run --project src/OpenWigi.Cli -- draw pattern
dotnet run --project src/OpenWigi.Cli -- touch --long 600
```

`owd` sem argumentos lista todos os comandos.

## Manager

`dotnet run --project src/OpenWigi.Manager` abre a janela e põe o ícone no tray. Fechar a janela só
esconde; o painel continua rodando. Ele precisa do serviço `OpenWigiDash` rodando
([docs/multiusuario.md](docs/multiusuario.md)).

| Opção | Efeito |
| --- | --- |
| `--tray` | Começa só no tray. É o que o "Abrir ao entrar no Windows" usa |
| `--open /widgets` | Abre direto numa tela |
| `--plugins <pasta>` | Pasta de plugins alternativa |
| `--preview` | Abre a prévia destacada |

As telas:
- **Painel:** prévia ao vivo do aparelho. O mouse vira toque: clique, segurar e arrastar.
- **Widgets e páginas:** grade 5x4 para adicionar, mover e tirar widgets, e criar e renomear páginas. Também abre a tela de configurações do plugin, em Razor.
- **Ajustes:** brilho, iniciar com o Windows e tema claro ou escuro.

## Inner loop

`scripts/Test-Device.ps1` compila, libera o aparelho do manager oficial (`-StopOfficial`), roda o
`owd` no hardware e guarda o log em `.diagnostics/device-check/`. Com `-Touch` registra os toques por
alguns segundos; com `-RestoreOfficial` religa o manager oficial no fim. No VS Code: tarefas
`device check`, `device check + touch` e `device check & restore official`, e o launch
`owd touch (aparelho real)` para depurar.

## Build

```powershell
dotnet build
dotnet test
```
