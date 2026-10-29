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
| `tests/OpenWigi.Device.Tests` | Testes do protocolo e dos gestos, com transporte e relógio falsos |

## Usar o `owd`

Feche o manager oficial antes: só um programa abre o aparelho por vez.

```powershell
dotnet run --project src/OpenWigi.Cli -- info
dotnet run --project src/OpenWigi.Cli -- draw pattern
dotnet run --project src/OpenWigi.Cli -- touch --long 600
```

`owd` sem argumentos lista todos os comandos.

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
