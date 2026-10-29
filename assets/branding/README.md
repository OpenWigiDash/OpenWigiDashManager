# Ícone do OpenWigiDash

O ícone é o próprio painel visto de frente: tela larga, um widget vermelho em destaque e um
gráfico ciano de sensor, nas cores do tema (`OpenWigiDashTheme`). Não usa formas, nomes ou cores
da G.Skill.

| Arquivo | Uso |
| --- | --- |
| `openwigidash.svg` | Fonte vetorial do desenho completo (40 px ou mais) |
| `openwigidash-16.svg` | Fonte do desenho simplificado na grade de 16 px (32 px ou menos) |
| `openwigidash.ico` | Ícone do app: 16, 20, 24, 32, 40, 48, 64 e 256 px. Vai em `<ApplicationIcon>` e no instalador (Adicionar ou remover programas, menu Iniciar, barra de tarefas) |
| `png/openwigidash-*.png` | Os mesmos tamanhos em PNG, mais 512 e 1024 para loja, site e README |
| `tray/tray-<estado>-<tema>.ico` | Bandeja do sistema, 16, 20, 24 e 32 px |
| `tray/tray-<estado>-<tema>.svg` | Fonte de cada ícone da bandeja |

## Bandeja

O ponto no canto segue o `AgentStatus` do agente. O conteúdo da tela só fica colorido quando a
tela é sua; nos outros estados o desenho fica apagado.

| Estado | Arquivo | Ponto |
| --- | --- | --- |
| `Owner`: mostrando o seu painel | `tray-owner-*` | verde `#3DD68C` |
| `Waiting`: tela em outra sessão | `tray-waiting-*` | cinza `#8B93A7` |
| `NoDevice` ou sem serviço | `tray-nodevice-*` | âmbar `#F2B544` |

Use `-dark` com a barra de tarefas escura e `-light` com a clara. O Windows diz qual é em
`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`, valor `SystemUsesLightTheme`
(1 = clara); vale trocar o ícone quando o usuário muda o tema (`SystemEvents.UserPreferenceChanged`).

## Gerar de novo

Os desenhos ficam em [`scripts/branding/gerar-icones.mjs`](../../scripts/branding/gerar-icones.mjs).
Mude lá e rode:

```powershell
npm i --no-save playwright
npx playwright install chromium
node scripts/branding/gerar-icones.mjs
```

Cada tamanho é rasterizado direto do vetor. Os `.ico` levam BMP de 32 bits abaixo de 256 px e PNG
em 256 px, o formato que o Windows e o `System.Drawing.Icon` leem sem surpresa.
