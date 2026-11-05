# CLAUDE.md

Regras para o Claude trabalhar neste repositório.

## PRs que dependem de outro PR (stacked PRs)

Quando um PR é aberto em cima do branch de outro PR ainda aberto, ele tem que
virar uma pilha nativa do GitHub. Se não virar, o merge dele cai no branch do PR
de baixo em vez de cair no `main` (foi o que aconteceu com o #23, que entrou no
branch do #20).

- Ligue os PRs como pilha logo depois de criar o de cima:
  `gh stack link <PR de baixo> <PR de cima>` (extensão `github/gh-stack`).
  Pela web também dá: abra o novo PR em cima do PR que ainda está aberto.
- Numa pilha, o merge é feito pelo PR mais alto que estiver pronto. O GitHub
  leva junto todos os PRs abaixo dele para o `main`, de baixo para cima.
- Se aparecer o botão "Rebase stack", rebaseie antes do merge (ou rode
  `gh stack rebase` / `gh stack sync`).
- Enquanto os PRs não estiverem ligados como pilha, nunca faça merge de um PR
  cuja base não seja `main`. Primeiro mude a base dele para `main`, ou faça o
  merge do PR de baixo antes.
- No texto de cada PR da pilha, diga de qual PR ele depende.

Referência: [About stacked PRs](https://docs.github.com/en/pull-requests/get-started/about-stacked-prs).

## Issues e PRs (ligação no board)

Todo PR diz quais issues ele entrega com palavra de fechamento, uma por issue:
`Closes #12, closes #13` (com "closes" repetido; `Closes #12, #13` só liga a primeira). Assim a issue
aparece ligada ao PR no GitHub Projects e fecha sozinha no merge. Não feche issue à mão.

- Issue que o PR só avança, sem terminar (ex.: falta teste manual no aparelho): `Refs #N`.
- Se uma issue precisou ser fechada sem PR, comente nela o PR ou commit que entregou.

## Publicar versão (tags)

A sessão do Claude só consegue empurrar o próprio branch: `git push` de tag volta 403. Não tente
empurrar tag. Para publicar, rode o workflow com a versão (sem o "v") no `main`, só quando o Victor
pedir a publicação daquela versão:

- Manager: workflow **Release** (`release.yml`), input `version` (ex.: `1.4.0`). Cria a tag `v1.4.0`
  e a release. Sem assinatura por enquanto: o SignPath Foundation negou o projeto (nov/2026), então
  a variável `SIGNPATH_ORGANIZATION_ID` fica fora do repositório e a release sai sem assinar. Não
  marque "assinar" nem proponha outro serviço de assinatura sem o Victor pedir.
- SDK: workflow **Publicar SDK no NuGet** (`sdk-publish.yml`), input `version` (ex.: `1.2.0-preview.1`).
  Publica no nuget.org e cria a tag `sdk-v1.2.0-preview.1`.
- Widgets (Discord, Stream Deck Mirror): workflow **Release Widget** (`release.yml`), input `version`.

Pelo MCP do GitHub: `actions_run_trigger` com `method: run_workflow`, `workflow_id: release.yml`,
`ref: main`, `inputs: {"version": "..."}`. Se a sessão não puder rodar, passe ao Victor o link
Actions > workflow > Run workflow, nunca comandos de tag.
