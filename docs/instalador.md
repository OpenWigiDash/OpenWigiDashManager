# Instalador

`OpenWigiDash-Setup.exe` (WiX 6, em `installer/`) é o que vai na Release. Ele instala o WebView2 Runtime
se faltar (no Windows 11 já vem) e depois o `OpenWigiDash.msi`, que tem o resto. Tudo é self-contained
win-x64: não precisa do .NET instalado.

## O que fica na máquina

| O quê | Onde |
| --- | --- |
| Serviço `OpenWigiDash` (automático, LocalService, reinicia se cair) | `C:\Program Files\OpenWigiDash\Service` |
| Manager (tray + janela) | `C:\Program Files\OpenWigiDash\Manager` |
| Widgets que vêm junto | `C:\Program Files\OpenWigiDash\Widgets` |
| Dados do serviço (ex.: `device.txt`, o aparelho ativo), com Modify para o LocalService | `C:\ProgramData\OpenWigiDash\Service` |
| Origem `OpenWigiDash` do Log de Eventos | Aplicativo |
| Atalho | Menu Iniciar (fixar na barra de tarefas a partir dele) |
| Início com o Windows de todos os usuários | `HKLM\...\Run`, valor `OpenWigiDash`, com `--tray --autostart` |
| Adicionar ou remover programas | "OpenWigiDash", com o ícone do app |

Por usuário, nada muda em relação ao dev: layout e ajustes em `%APPDATA%\OpenWigiDash`, widgets do
usuário em `%LOCALAPPDATA%\OpenWigiDash\Widgets`. Um widget com o mesmo nome de pasta lá ganha do que
veio com o instalador. Desinstalar não apaga nada disso.

O "Abrir ao entrar no Windows" de Ajustes, com o app instalado, fica guardado no `manager.json` do
usuário: o manager abre pela entrada da máquina e sai na hora se o usuário desligou. Sem instalador
(dev), continua sendo a chave `Run` do usuário.

## Atualizar e remover

A versão vem da tag (`v0.2.0` vira 0.2.0). Instalar uma versão nova por cima faz upgrade: o MSI fecha
o manager em todas as sessões, para o serviço, troca os arquivos e inicia o serviço de novo. O manager
volta no próximo login ou pelo atalho. Instalar uma versão mais velha por cima é recusado.

Para voltar a uma versão anterior, desinstale o OpenWigiDash em Configurações > Aplicativos e rode o
`OpenWigiDash-Setup.exe` da versão desejada, baixado da página de Releases. Layout, ajustes e plugins do
usuário ficam no perfil e voltam junto. Um plugin feito para um SDK mais novo do que o dessa versão
aparece no canvas como "Não funciona nesta versão".

## Gerar

```powershell
scripts\Build-Installer.ps1 -Version 0.1.0
```

Sai em `.artifacts\installer\`. No GitHub, o workflow `Release` faz o mesmo a cada tag `vX.Y.Z` e cria a
Release com os dois arquivos; em PR que mexe no instalador ele só compila e guarda como artefato.

## Testar num Windows limpo (Windows Sandbox)

```powershell
scripts\Test-InstallerSandbox.ps1 -Build              # gera, instala no Sandbox e abre o manager
scripts\Test-InstallerSandbox.ps1 -Uninstall          # instala, confere, desinstala e confere de novo
```

Abre o Windows Sandbox (um Windows limpo, descartado ao fechar) e roda lá dentro
`scripts\sandbox\Install-InSandbox.ps1`: instala o `OpenWigiDash-Setup.exe` em modo silencioso e confere
serviço, pastas, atalho, `Run`, a entrada em Aplicativos e o WebView2. O Sandbox não repassa USB, então
o serviço instalado lá passa a usar o WigiDash virtual (`appsettings.local.json` com `Device = Virtual` e
`AllowRemoteSessions`, porque a sessão do Sandbox chega como remota) e o manager abre com a prévia, onde
o mouse vira toque. O resultado volta para `.artifacts\sandbox\logs\resultado.txt`, com os logs do
instalador e os eventos do serviço ao lado. Este PC não é tocado.

Precisa do Windows Pro ou Enterprise com "Área Restrita do Windows" ligada em "Ativar ou desativar
recursos do Windows".

## No PC de desenvolvimento

O serviço de dev (`scripts\Setup-DevService.ps1`) e o instalado têm o mesmo nome, `OpenWigiDash`, e só um
processo abre o USB. Então é um ou outro:

- **Ir para o instalado:** num PowerShell como admin, `scripts\Setup-DevService.ps1 -Remove` (ou
  `scripts\Install-Service.ps1 -Uninstall`, se usou esse). Depois rode o `OpenWigiDash-Setup.exe`. Se o
  serviço de dev ainda existir, o instalador para e diz isso.
- **Voltar ao dev:** desinstale o OpenWigiDash em Configurações > Aplicativos e rode
  `scripts\Setup-DevService.ps1` de novo. Enquanto o instalado existir, os scripts do serviço de dev
  avisam e não mexem nele.

O manager abre uma vez só por sessão (o de dev e o instalado usam o mesmo mutex), então os dois nunca
desenham juntos. Com o instalado, o login abre o instalado; para depurar o manager, feche o do tray antes
do `dotnet run`.
