# Contribuindo

## Verificação de plugins

Plugins são pacotes do nuget.org (`packageType` `OpenWigiDashPlugin`; ver [docs/sdk.md](docs/sdk.md)). Qualquer
um pode publicar, e a Loja do manager mostra cada pacote com um selo:

| Selo | Quem | Na Loja |
| --- | --- | --- |
| **Oficial** | Pacotes das contas do projeto no nuget.org (`PluginFeed.OfficialOwners`) | Instala direto |
| **Verificado** | Pacotes em [`plugins/verified.json`](plugins/verified.json), com o dono conferido no nuget.org | Instala direto |
| **Comunidade** | Todo o resto | Pede confirmação: o código não foi revisado |

### Como pedir

Abra uma issue com o modelo **Pedir verificação de plugin**. Um mantenedor revisa e, se aprovar, a entrada entra
em `plugins/verified.json` por PR neste repositório.

### Checklist de revisão

- [ ] Código-fonte público, e o pacote no nuget.org é o que o workflow do repositório gera a partir dele
      (publicação por Trusted Publishing, sem pacote enviado à mão).
- [ ] Não baixa nem executa código em tempo de execução (DLLs, scripts, atualizações próprias).
- [ ] Acessa só o que o widget precisa (rede, arquivos, processos), e isso está explicado no pedido.
- [ ] Nada de coleta de dados do usuário sem aviso claro.
- [ ] A conta dona do pacote no nuget.org é a mesma do pedido (é ela que vai no campo `owner`).

### Formato da lista

```json
{
  "version": 1,
  "plugins": [
    {
      "id": "SuaConta.OpenWigiDash.MeuPlugin",
      "owner": "SuaConta",
      "repository": "https://github.com/sua-conta/meu-plugin",
      "blockedVersions": ["1.0.0", "[1.1.0, 1.2.0)"],
      "reason": { "1.0.0": "Trava ao abrir as configurações", "[1.1.0, 1.2.0)": "Vaza o token do Discord no log" }
    }
  ]
}
```

- `owner` é obrigatório para o selo. Uma entrada **sem** `owner` só bloqueia versões; serve para qualquer pacote,
  verificado ou não.
- `blockedVersions` aceita versões exatas e faixas do NuGet. Versão bloqueada não instala nem aparece como
  atualização, e quem já a tem instalada vê um aviso na Loja e na tela de widgets. O nuget.org não apaga versões,
  então é assim que se tira uma versão com problema de circulação.
- O formato é validado no CI pelo [schema](plugins/verified.schema.json) e por um teste que lê o arquivo.
- O manager baixa a lista de `raw.githubusercontent.com` (o repositório é público) e guarda uma cópia para uso offline.
