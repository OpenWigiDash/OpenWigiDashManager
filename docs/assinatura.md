# Assinatura do instalador (SignPath Foundation)

A release é assinada de graça pelo [SignPath Foundation](https://signpath.org), que assina projetos open
source. O certificado é deles: no Windows, o publicador aparece como **SignPath Foundation**. A chave fica
num HSM do SignPath; o repositório nunca vê certificado nem senha, só um token de API.

As regras do programa estão nos [termos](https://signpath.org/terms). As que pesam aqui: licença aprovada
pela OSI (o projeto é [MIT](../LICENSE)), MFA no GitHub e no SignPath para todo mundo do time, a seção
[Code signing policy](../README.md#code-signing-policy) no README e na página da release, e uma política de
privacidade que lista o que o programa manda para a rede sem o usuário pedir.

## O que é assinado

O workflow [Release](../.github/workflows/release.yml) faz três pedidos ao SignPath, cada um com aprovação
manual no site:

| Rodada | O quê | Artifact configuration |
| --- | --- | --- |
| 1 | `OpenWigiDash.msi`, e antes os `OpenWigi*.exe` e `OpenWigi*.dll` dentro dele | [`msi`](../.signpath/artifact-configurations/msi.xml) |
| 2 | O motor do Burn, separado do `OpenWigiDash-Setup.exe` com `wix burn detach` | [`engine`](../.signpath/artifact-configurations/engine.xml) |
| 3 | O `OpenWigiDash-Setup.exe`, já com o motor assinado de volta (`wix burn reattach`) | [`setup`](../.signpath/artifact-configurations/setup.xml) |

Entre a rodada 1 e a 2, o bundle é gerado de novo em volta do MSI assinado
(`scripts\Build-Installer.ps1 -MsiPath ...`). A ordem importa: o bundle guarda o hash do MSI, e a
assinatura do Setup.exe só vale se o motor dentro dele também estiver assinado.

Bibliotecas de terceiros (runtime do .NET, MudBlazor, bootstrapper do WebView2) não são assinadas pelo
projeto. Ficam com a assinatura dos autores, quando têm.

Sem a variável `SIGNPATH_ORGANIZATION_ID` no repositório, o workflow pula tudo isso e a release sai sem
assinatura, como antes.

## Configurar (uma vez)

1. **Inscrição.** Com a v0.x já publicada, pedir em [signpath.org](https://signpath.org) (o texto do
   pedido está em [Pedido](#pedido)). Eles avaliam e respondem por e-mail.
2. **MFA.** Ligado no GitHub e na conta do SignPath.
3. **No SignPath**, depois de aprovado:
   - instalar o [SignPath GitHub App](https://github.com/apps/signpath) no repositório;
   - no projeto (slug `OpenWigiDashManager`), ligar o Trusted Build System "GitHub.com";
   - criar as três artifact configurations com os slugs `msi`, `engine` e `setup`, colando o XML de
     `.signpath/artifact-configurations/`;
   - conferir o slug da signing policy de release (o padrão aqui é `release-signing`) e se você está como
     approver;
   - criar um usuário de CI (ou token) com permissão de submitter nessa policy e gerar o API token.
4. **No GitHub**, em Settings > Secrets and variables > Actions:
   - secret `SIGNPATH_API_TOKEN` com o token;
   - variável `SIGNPATH_ORGANIZATION_ID` com o id da organização no SignPath;
   - se os slugs forem outros, variáveis `SIGNPATH_PROJECT_SLUG` e `SIGNPATH_SIGNING_POLICY_SLUG`.
5. **Testar** antes de uma tag: Actions > Release > Run workflow, com "assinar" marcado. Aprovar os três
   pedidos no SignPath e baixar o artefato `OpenWigiDash-0.0.N` do run. Se o SignPath recusar a rodada 1
   por não achar os arquivos dentro do MSI, ajustar os `include path` de `msi.xml` (lá e no SignPath).

## Lançar uma versão assinada

Igual a antes: tag `vX.Y.Z`. O job para em cada rodada esperando a aprovação no site do SignPath (até uma
hora por rodada). A página da release ganha o crédito ao SignPath e o link para a política de assinatura.

## Pedido

Rascunho em inglês para o formulário do SignPath Foundation:

- **Project name:** OpenWigiDash Manager
- **Repository:** https://github.com/victorperez2911/OpenWigiDashManager
- **License:** MIT
- **Description:** Open source replacement for the discontinued G.SKILL WigiDash Manager, the software for
  the WigiDash USB touch panel. It runs a Windows service that owns the device over WinUSB, a tray app
  (WPF + Blazor) to arrange widgets and pages, and loads widget plugins published on nuget.org. It is a
  clean reimplementation: no G.SKILL code or binaries are included.
- **What will be signed:** the Windows installer built by GitHub Actions on GitHub-hosted runners: the
  project's own `OpenWigi*.exe`/`OpenWigi*.dll` inside the MSI, the MSI, and the WiX Burn bootstrapper
  `OpenWigiDash-Setup.exe` (including its engine).
- **Third-party components:** the self-contained .NET runtime and the Microsoft Edge WebView2 bootstrapper
  (both Microsoft-signed, shipped unmodified) and MudBlazor (MIT). They are not signed by the project.
  Question for the reviewers: is shipping the WebView2 bootstrapper inside the bundle acceptable as a
  system library?
- **Releases:** https://github.com/victorperez2911/OpenWigiDashManager/releases
- **Code signing policy:** https://github.com/victorperez2911/OpenWigiDashManager#code-signing-policy
- **Team:** Victor Perez (author, reviewer and approver), MFA enabled on GitHub and SignPath.
