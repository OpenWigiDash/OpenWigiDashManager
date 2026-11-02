# Host de plugins

`OpenWigi.Host` roda dentro do agente de cada usuário. Carrega os widgets, monta a tela e entrega os
toques. Quem fala com o aparelho é o serviço; o host só manda pixels pelo `IDisplaySink`.

## Onde ficam as coisas

| O quê | Onde |
| --- | --- |
| Plugins | `%LOCALAPPDATA%\OpenWigiDash\Widgets\<pasta>\` (uma pasta por plugin) |
| Layout | `%APPDATA%\OpenWigiDash\layout.json` |
| Configurações e arquivos de cada instância | `%APPDATA%\OpenWigiDash\instances\<id>\` |
| Relatórios de erro do manager | `%LOCALAPPDATA%\OpenWigiDash\logs\erro-AAAAMMDD-HHMMSS.txt` |

Tudo fica no perfil do usuário do Windows, então cada pessoa da casa tem o próprio painel.

## Carregamento

- A DLL de entrada é a de `plugin.json` (`{ "entry": "Meu.dll" }`). Sem esse arquivo, vale a DLL com o nome da pasta.
- Os widgets vêm do atributo `[assembly: OpenWigiWidget(typeof(...))]`.
- Cada plugin ganha um `AssemblyLoadContext` colecionável. O contrato, o Blazor e o MudBlazor vêm do host (`OpenWigiSdk.SharedAssemblies`); o resto das dependências vem da pasta do plugin.
- Plugin quebrado vira um item em `PluginErrors` e não impede os outros de carregar.

`scripts\Deploy-Widget.ps1 [-Project caminho.csproj]` compila e instala um widget, com backup do anterior.

## Tela

- **Grade:** 10x8 meias células com 4 px de espaço entre elas e a mesma folga até a borda da tela (5 px nas laterais, 4 px em cima e embaixo; colunas de 97 px, linhas de 69 e 70 px alternadas). Toque na folga da borda vai para o widget da borda. O `layout.json` da grade 5x4 é convertido na primeira abertura (posição e tamanho dobram, nada muda de lugar) e ganha `"grid": 2`.
- **Moldura:** cada página tem a sua (`LayoutPage.Frame`): respiro, borda, cantos e cores. O compositor pinta fundo, borda e canto arredondado (com suavização) ao montar a página, e corta de novo o canto do conteúdo depois de cada quadro do widget. Tela cheia fica sem moldura. Trocar a moldura chama `OnSizeChangedAsync` nos widgets cuja área mudou e redesenha a página.
- **Primeira execução:** sem layout, o host põe um de cada widget instalado na primeira posição livre.

## Quando algo dá errado

O manager não pode sumir sem dizer nada. O que dá para contornar é contornado, e a pessoa fica sabendo:

- **Layout que não abre** (de uma versão mais nova do OpenWigiDash, ou com defeito): vai para `backups\ilegivel-AAAAMMDD-HHMMSS.json`, o host começa com um layout vazio e `DashboardHost.LayoutProblem` diz o que houve e onde ficou o arquivo. O manager mostra isso numa janela ao abrir e num aviso no topo da janela principal.
- **Widget fora da grade** no layout: sai do layout; o resto abre.
- **Widget que não sobe** (erro ao criar, tamanho que não cabe mais): a posição fica vazia; os outros seguem.
- **Plugin com defeito** (`plugin.json` ruim, DLL que não carrega): vira um item em `PluginErrors`.
- **Erro numa tela do manager:** a tela mostra o erro com "Tentar de novo"; o resto da janela segue.
- **Qualquer outro erro inesperado:** o relatório vai para a pasta de logs, um balão na bandeja avisa (um por minuto, no máximo) e o manager continua rodando.
- **Erro que impede abrir, ou que o .NET não deixa segurar** (numa thread de fundo): janela de erro com a mensagem e o caminho do relatório, e o manager fecha.
- **Páginas do hardware:** o compositor usa as páginas 0 e 1. A página que está na tela vive numa delas, e a próxima é montada inteira na outra antes da troca. A página 2 é do serviço.
- **Envio:** a cada ~33 ms vão ao aparelho só as linhas que mudaram, uma escrita por faixa: dois widgets longe um do outro não levam as linhas do meio. Faixas a menos de 8 linhas uma da outra vão juntas. Quadro empurrado com região suja manda menos ainda.
- **Repouso:** widget fora da página visível, ou com a tela em outra sessão, recebe `OnSleepAsync`. Na volta recebe `OnWakeAsync` e `RenderAsync(Resumed)`.

## Toques

| Gesto | Para onde vai |
| --- | --- |
| Swipe para os lados, fora de tela cheia, com mais de uma página | Troca de página (para a esquerda = próxima) |
| `PressStarted` | Widget sob o dedo, que fica marcado como alvo. Vale a célula com a moldura e até 2 px além dela (metade do espaço da grade); se dois servem, o mais perto |
| `PressProgress`, `LongPress`, `PressEnded` | O alvo marcado no `PressStarted`, mesmo que o dedo saia dele |
| Tap, swipe vertical | Widget sob o ponto, pela mesma regra |
| Double tap | Só se o toque anterior foi no mesmo widget; dois botões pequenos vizinhos recebem cada um o seu tap e nenhum double tap |

X/Y chegam relativos à área de conteúdo e presos dentro dela.
| Qualquer gesto em tela cheia | O widget em tela cheia |

As coordenadas chegam relativas ao widget.

## Ainda não

A central de ações e gatilhos (fase 7) só registra em log, e a tela de configurações em Razor chega na
fase 5.
