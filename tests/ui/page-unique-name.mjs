// Tenta dar a uma página o nome de outra (tem que recusar) e confere uma só miniatura selecionada.
// Não grava nada. Uso: node page-unique-name.mjs <pasta>
import { chromium } from 'playwright';
const dir = process.argv[2] ?? '.';
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
await page.evaluate(() => { history.pushState({}, '', '/'); dispatchEvent(new PopStateEvent('popstate')); });
await page.waitForTimeout(2000);
const names = await page.locator('.owd-page-thumb:not(.owd-page-add) .owd-page-name').allInnerTexts();
console.log('páginas:', names.map(n => n.trim()).join(' | '));
const title = page.locator('.owd-page-title input');
const original = await title.inputValue();
await title.fill(names[1].trim());
await page.waitForTimeout(1200);
console.log('erro mostrado:', await page.locator('.owd-page-title .mud-input-helper-text, .owd-page-title .mud-input-error').allInnerTexts());
await page.screenshot({ path: `${dir}/nome-repetido.png` });
console.log('miniatura continua:', await page.locator('.owd-page-thumb.selected .owd-page-name').innerText());
await title.fill(original);
await page.waitForTimeout(1000);
// passa o mouse em outra miniatura: só uma pode ficar vermelha
await page.locator('.owd-page-thumb:not(.owd-page-add)').nth(1).hover();
await page.waitForTimeout(300);
const red = await page.locator('.owd-page-thumb').evaluateAll(els => els.filter(e => getComputedStyle(e).borderTopColor === 'rgb(229, 36, 59)').length);
console.log('miniaturas com borda vermelha (mouse em outra):', red);
await browser.close();
