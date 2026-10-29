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
