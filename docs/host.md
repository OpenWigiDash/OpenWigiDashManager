# Host de plugins

`OpenWigi.Host` roda dentro do agente de cada usuário. Carrega os widgets, monta a tela e entrega os
toques. Quem fala com o aparelho é o serviço; o host só manda pixels pelo `IDisplaySink`.

## Onde ficam as coisas

| O quê | Onde |
| --- | --- |
| Plugins | `%LOCALAPPDATA%\OpenWigiDash\Widgets\<pasta>\` (uma pasta por plugin) |
| Layout | `%APPDATA%\OpenWigiDash\layout.json` |
| Configurações e arquivos de cada instância | `%APPDATA%\OpenWigiDash\instances\<id>\` |

Tudo fica no perfil do usuário do Windows, então cada pessoa da casa tem o próprio painel.

## Carregamento

- A DLL de entrada é a de `plugin.json` (`{ "entry": "Meu.dll" }`). Sem esse arquivo, vale a DLL com o nome da pasta.
- Os widgets vêm do atributo `[assembly: OpenWigiWidget(typeof(...))]`.
- Cada plugin ganha um `AssemblyLoadContext` colecionável. O contrato, o Blazor e o MudBlazor vêm do host (`OpenWigiSdk.SharedAssemblies`); o resto das dependências vem da pasta do plugin.
- Plugin quebrado vira um item em `PluginErrors` e não impede os outros de carregar.

`scripts\Deploy-Widget.ps1 [-Project caminho.csproj]` compila e instala um widget, com backup do anterior.

## Tela

- **Grade:** 5x4 células de 200x145 com 4 px de espaço.
- **Primeira execução:** sem layout, o host põe um de cada widget instalado na primeira posição livre.
- **Páginas do hardware:** o compositor usa as páginas 0 e 1. A página que está na tela vive numa delas, e a próxima é montada inteira na outra antes da troca. A página 2 é do serviço.
- **Envio:** a cada ~33 ms vão ao aparelho só as linhas que mudaram. Quadro empurrado com região suja manda menos ainda.
- **Repouso:** widget fora da página visível, ou com a tela em outra sessão, recebe `OnSleepAsync`. Na volta recebe `OnWakeAsync` e `RenderAsync(Resumed)`.

## Toques

| Gesto | Para onde vai |
| --- | --- |
| Swipe para os lados, fora de tela cheia, com mais de uma página | Troca de página (para a esquerda = próxima) |
| `PressStarted` | Widget sob o dedo, que fica marcado como alvo |
| `PressProgress`, `LongPress`, `PressEnded` | O alvo marcado no `PressStarted`, mesmo que o dedo saia dele |
| Tap, double tap, swipe vertical | Widget sob o ponto |
| Qualquer gesto em tela cheia | O widget em tela cheia |

As coordenadas chegam relativas ao widget.

## Ainda não

A central de ações e gatilhos (fase 7) só registra em log, e a tela de configurações em Razor chega na
fase 5.
