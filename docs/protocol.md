# Protocolo USB do WigiDash

Documentação escrita por nós a partir da análise do manager oficial (1.2.9519) e de testes no
aparelho. Nenhum código da G.Skill faz parte deste repositório.

## Identificação

| Item | Valor |
| --- | --- |
| VID / PID | `28DA` / `EF01` |
| Driver | WinUSB, interface de dispositivo `{D876A186-7B31-4804-8115-79A87E8941BD}` |
| Tela | 1016 × 592, RGB565 little-endian, linha a linha de cima para baixo |
| Páginas no hardware | 3 (0, 1, 2) |
| Endpoints | OUT bulk `0x01` (pixels), IN bulk `0x81` (não usado) |
| Firmware testado | `WigiDash-1.0_20241006_1755` |

O WinUSB abre o aparelho em modo exclusivo: um segundo processo recebe "Acesso negado". É por isso
que o manager oficial congela a tela quando outro usuário do Windows entra.

## Transferências de controle

Todas são de classe com destino interface, índice 0:

- **OUT**: `bmRequestType = 0x21`
- **IN**: `bmRequestType = 0xA1`

Quando o comando é sobre uma página e uma região, `wValue = (página << 8) | região`.
Números com mais de um byte são little-endian.

| bRequest | Dir. | Dados | Significado |
| --- | --- | --- | --- |
| `0x00` | IN | 3 bytes | Modo: `"WD\0"` aplicação, `"BL\0"` bootloader |
| `0x04` | IN | até 64 bytes | Versão do firmware, ASCII terminado em zero |
| `0x05` | IN | 12 bytes | UID do microcontrolador. Os bytes 1 a 11, completados com zero até 16, formam o GUID do aparelho (o mesmo que o manager oficial usa) |
| `0x06` | OUT | `wValue` = modo | Reset: 0 aplicação, 1 bootloader, 2 bootloader do sistema |
| `0x10` | IN | 48 bytes | Lê a config (abaixo) |
| `0x11` | OUT | 48 bytes | Aplica a config na RAM |
| `0x12` | OUT | — | Acorda a tela e zera o contador de timeout |
| `0x13` | OUT | — | Põe a tela para dormir |
| `0x14` | OUT | — | Grava a config na flash |
| `0x15` | OUT | — | Volta a config para o padrão de fábrica |
| `0x33` | IN | 8 bytes | Toque e estado (abaixo) |
| `0x50` | IN | 1 byte | Brilho atual, 0 a 100 |
| `0x51` | OUT | 1 byte | Muda o brilho atual (não persiste) |
| `0x61` | OUT | 8 bytes | Início de escrita de pixels: offset (u32) e tamanho (u32) em bytes. Os pixels seguem pelo endpoint bulk `0x01` |
| `0x63` | OUT | — | Aborta uma escrita em andamento |
| `0x70` | OUT | `wValue` | Troca de tela: `1` boas-vindas; `0x20 | página | (transição << 2)` mostra uma página |
| `0x91` | OUT | 20 bytes | Declara uma região de desenho na página (abaixo) |
| `0x92` | OUT | — | Remove uma região |

O comando `0x01` (ID de dispositivo) não é atendido por este firmware: ele devolve a resposta do
comando anterior. O manager oficial também não usa.

### Transições (`0x70`)

| Valor | Efeito |
| --- | --- |
| 0 | Wipe para leste |
| 1 | Wipe para oeste |
| 2 | Wipe para norte |
| 3 | Wipe para sul |
| 4 | Sem animação |

### Config (48 bytes)

| Offset | Tamanho | Campo |
| --- | --- | --- |
| 0 | 1 | Versão do layout (1) |
| 1 | 1 | Brilho padrão, 0 a 100 |
| 2 | 2 | Preenchimento (preservar como veio) |
| 4 | 4 | Timeout de tela em segundos (0 = nunca) |
| 8 | 32 | Apelido, ASCII terminado em zero |
| 40 | 2 | VCOM do painel |
| 42 | 2 | AVDD do painel |
| 44 | 1 | Offset X da imagem |
| 45 | 1 | Offset Y da imagem |
| 46 | 2 | CRC-16/CCITT (polinômio `0x1021`, semente `0xFFFF`) dos bytes 0 a 45 |

### Toque e estado (`0x33`, 8 bytes)

| Offset | Campo |
| --- | --- |
| 0 | Contato: 0 nenhum, 1 dedo encostado (Down), 2 dedo saiu (Up) |
| 2 | X (i16) — com contato 0 fica a última posição, que deve ser ignorada |
| 4 | Y (i16) |
| 6 | Tela atual (neste firmware ficou sempre 0 nos testes) |
| 7 | 1 quando a tela está dormindo |

O firmware só reporta contato e posição. Tap, swipe e long-press são calculados no PC, lendo esse
comando a cada ~33 ms (`OpenWigi.Device.Gestures.GestureRecognizer`).

### Região de desenho (`0x91`, 20 bytes)

| Offset | Tamanho | Campo |
| --- | --- | --- |
| 0 | 2 | X |
| 2 | 2 | Y |
| 4 | 2 | Largura |
| 6 | 2 | Altura |
| 8 | 2 | Cor base (RGB565), 0 |
| 12 | 4 | Endereço de desenho, 0 |
| 16 | 3 | Flags (trava, invalidar, usar cache), 0 |

Compomos o quadro inteiro no PC, então basta uma região 0 cobrindo a tela toda em cada página.

## Enviar um quadro

1. `0x91` na página, região 0, `0, 0, 1016, 592` (uma vez por página e conexão).
2. `0x61` com offset 0 e tamanho 1 203 744, depois os pixels pelo bulk `0x01`.
3. `0x70` com `0x20 | página | (4 << 2)` para mostrar sem animação.

Para atualizar só uma faixa de linhas, use offset `linha × 2032` e mande apenas essas linhas.
Medido no aparelho: quadro inteiro em ~90 ms, faixa de 100 linhas em ~15 ms.

## Bootloader e firmware

Em modo bootloader (`0x00` devolve `"BL\0"`) o firmware ocupa 7 setores de 128 KB, gravados com
apagar, enviar dados, gravar com CRC32 e verificar. Está fora do escopo até haver aprovação
explícita, porque um erro deixa o aparelho inutilizável.
