// Arrasta o contador de exemplo para um canto livre da página e mede quanto a UI demora a refletir.
// Uso: node drag-and-drop.mjs <captura.png>   (o manager precisa estar aberto em /widgets com a porta CDP)
import { chromium } from 'playwright';

const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
const out = process.argv[2];

const counter = page.locator('.owd-widget', { hasText: 'Contador' });
const before = await counter.getAttribute('style');
console.log('antes:', before);

// Vai para o canto inferior direito, ou para o inferior esquerdo se já estiver lá.
// Grade 10x8 de meias células; o contador (2x2) no canto começa na coluna 9, linha 7.
const atCorner = before.includes('grid-column: 9') && before.includes('grid-row: 7');
const grid = await page.locator('.owd-grid').boundingBox();
const cellW = grid.width / 10, cellH = grid.height / 8;
const target = { x: grid.x + cellW * (atCorner ? 0.5 : 8.5), y: grid.y + cellH * 6.5 };

// Mouse "de verdade" pelo CDP: dispara pointerdown e o arrastar nativo do HTML5.
const from = await counter.boundingBox();
await page.mouse.move(from.x + 20, from.y + 20);
await page.mouse.down();
await page.mouse.move(from.x + 40, from.y + 40, { steps: 5 });
await page.mouse.move(target.x, target.y, { steps: 15 });
await page.waitForTimeout(300);
const drop = await page.locator('.owd-drop').getAttribute('class').catch(() => 'sem prévia');
console.log('prévia do encaixe:', drop);
if (out)
    await page.screenshot({ path: out });

const t0 = Date.now();
await page.mouse.up();
await page.waitForFunction(
    b => [...document.querySelectorAll('.owd-widget')].find(e => e.textContent.includes('Contador'))?.getAttribute('style') !== b,
    before, { timeout: 5000 });
console.log('UI atualizou em', Date.now() - t0, 'ms');
console.log('depois:', await counter.getAttribute('style'));
await browser.close();

if (!drop.includes('ok'))
    process.exit(1);
