# Vários usuários do Windows

O WinUSB abre o WigiDash em modo exclusivo. No manager oficial, quem abriu primeiro fica com o
aparelho até fechar, e a troca de usuário congela a tela. Aqui o aparelho é sempre do serviço
`OpenWigiDash`, e cada usuário roda um agente que só desenha quando o serviço deixa.

## Quem fica com a tela

| Situação | Quem desenha |
| --- | --- |
| Usuário no console, desbloqueado, com o agente aberto | O agente desse usuário |
| Sessão do console bloqueada (Win+L) | O serviço: relógio e "Sessão bloqueada" |
| Usuário no console sem o agente (ou tela de login) | O serviço |
| Sessão remota (RDP) | Ninguém, a não ser com `OpenWigi:AllowRemoteSessions = true` |
| Dois agentes na mesma sessão | O mais recente |

O serviço reavalia ao receber os avisos de sessão do Windows (logon, logoff, lock, unlock,
troca de console) e, por garantia, a cada segundo. Quem perde a tela recebe `Ownership(false)`;
quem ganha recebe `Ownership(true)` e manda o quadro inteiro de novo. A página 2 do hardware é
reservada para a tela do serviço.

## Canal

Named pipe `\\.\pipe\OpenWigiDash.v1`, aberto para usuários autenticados da máquina e negado
pela rede. A sessão do agente é descoberta pelo processo do outro lado do pipe, não pelo que ele
declara. Formato das mensagens em `src/OpenWigi.Ipc/IpcConnection.cs`.

## Serviço de desenvolvimento (admin uma vez só)

`scripts\Setup-DevService.ps1`, uma vez num PowerShell como administrador:

- cria `C:\OpenWigiDash\dev-service` com escrita para o seu usuário;
- registra o serviço `OpenWigiDash` rodando como **LocalService**, uma conta sem privilégios, porque a pasta é gravável;
- dá ao seu usuário o direito de iniciar e parar o serviço;
- registra a origem `OpenWigiDash` no Log de Eventos.

Depois, sem admin, `scripts\Deploy-DevService.ps1 [-AllowRemoteSessions] [-StopOfficial]` publica,
para o serviço, troca os arquivos e inicia de novo. Os ajustes da máquina ficam em
`appsettings.local.json` na pasta do serviço. Os logs vão para o Log de Eventos, em Aplicativo,
origem `OpenWigiDash`.

Com o OpenWigiDash instalado pelo instalador, o serviço de dev não pode existir junto (mesmo nome):
ver [instalador.md](instalador.md#no-pc-de-desenvolvimento).

`scripts\Install-Service.ps1` continua existindo para instalar como LocalSystem em Program Files,
mais perto do que o instalador vai fazer. Mas cada atualização com ele pede admin.

## Testar a troca de usuário

1. Serviço instalado (acima) e atualizado: `scripts\Deploy-DevService.ps1 -StopOfficial`.
2. No seu usuário: `dotnet run --project src\OpenWigi.Agent`. A tela fica com a sua cor e o seu nome.
3. Win+L: a tela vira o relógio "Sessão bloqueada". Desbloqueie: volta a sua tela.
4. Troque de usuário (sem sair do seu) e rode o agente no outro usuário: a tela muda de cor e de nome.
5. Volte para o seu usuário: a sua tela volta, sem replugar o cabo.
6. Toque, arraste e segure na tela: os gestos aparecem no console do agente do usuário ativo.

Para remover: `scripts\Install-Service.ps1 -Uninstall`.

## Prévia e toque emulado (útil pelo RDP)

`OpenWigiAgent --preview` abre uma janela com a página que está no aparelho, a cópia que o serviço
guarda do que escreveu (o WigiDash não devolve a imagem). O mouse na janela vira toque e passa
pelo mesmo reconhecedor de gestos: clicar dá tap, segurar o botão dá long-press e arrastar dá swipe.
A prévia e o toque emulado só valem para o dono da tela, então outro usuário não vê nem mexe na
tela de quem está no console. Pelo RDP, rode o serviço com `--OpenWigi:AllowRemoteSessions=true`.

## Sem o aparelho (WigiDash virtual)

Com `OpenWigi:Device = Virtual` o serviço não abre o WinUSB e usa um WigiDash de mentira, como o
"Virtual WigiDash" do manager oficial. Ele se diz conectado, aceita os quadros (que aparecem na prévia
do manager e no `OpenWigiAgent --preview`) e passa o mouse da prévia pelo mesmo reconhecedor de gestos do
aparelho real: clicar dá tap, segurar dá long-press e arrastar dá swipe. O brilho escolhido fica guardado.

- Serviço de dev: `scripts\Deploy-DevService.ps1 -Virtual` (sem `-Virtual` volta para o aparelho).
- No console, com o serviço instalado parado: `dotnet run --project src\OpenWigi.Service -- --OpenWigi:Device=Virtual`.

Dá para usar com o manager oficial aberto, porque nada disputa o aparelho. Serve para mexer no host e no
manager e para testar plugins: basta pôr o plugin na pasta de widgets e abrir a prévia.

## Testar com outro usuário sem instalar nada nele

`scripts\Deploy-DevManager.ps1` (sem admin) publica o manager em `C:\OpenWigiDash\dev-manager`, copia os
widgets do usuário atual para `C:\OpenWigiDash\dev-widgets` e cria `C:\OpenWigiDash\Abrir OpenWigiDash (teste).cmd`.
No outro usuário do Windows basta abrir esse atalho: o serviço de dev já é da máquina, e layout e configurações
ficam no `%APPDATA%` de cada um. Depois é trocar de usuário no console (Win+L, "Trocar usuário") e ver a tela
do aparelho passar de um para o outro.
