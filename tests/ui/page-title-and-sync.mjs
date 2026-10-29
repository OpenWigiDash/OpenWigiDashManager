// Fluxo de renomear página (duplo clique / caneta, Enter, Esc, ✓), nome repetido recusado, troca de
// página pela miniatura com título e "No aparelho agora" juntos, e só uma miniatura vermelha.
// Devolve o nome original. Uso: node page-title-and-sync.mjs <pasta-das-capturas>
import { chromium } from 'playwright';

const dir = process.argv[2] ?? '.';
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
await page.evaluate(() => { history.pushState({}, '', '/'); dispatchEvent(new PopStateEvent('popstate')); });
await page.waitForTimeout(2000);

const title = page.locator('.owd-page-title .mud-typography');
const field = page.locator('.owd-page-title-edit input');
const selectedThumb = page.locator('.owd-page-thumb.selected .owd-page-name');
const names = (await page.locator('.owd-page-thumb:not(.owd-page-add) .owd-page-name').allInnerTexts()).map(n => n.trim());
const original = (await title.innerText()).trim();
console.log('páginas:', names.join(' | '), '| título:', original);

// Duplo clique abre o campo; nome de outra página é recusado; Esc fecha sem gravar.
await title.dblclick();
await page.waitForTimeout(400);
console.log('duplo clique abriu o campo:', await field.isVisible());
await page.screenshot({ path: `${dir}/renomear-editando.png` });
await field.fill(names.find(n => n !== original));
await field.press('Enter');
await page.waitForTimeout(500);
console.log('erro:', (await page.locator('.owd-page-title-edit .mud-input-error').allInnerTexts()).join(' '));
await page.screenshot({ path: `${dir}/renomear-repetido.png` });
await field.press('Escape');
await page.waitForTimeout(400);
console.log('Esc voltou ao título sem gravar:', (await title.innerText()).trim() === original);

// Caneta + Enter grava; caneta + ✓ devolve o nome.
await page.getByRole('button', { name: 'Renomear página' }).click();
await field.fill(`${original} (teste)`);
await field.press('Enter');
await page.waitForTimeout(800);
console.log('gravou e a miniatura acompanhou:', await selectedThumb.innerText());
await page.getByRole('button', { name: 'Renomear página' }).click();
await field.fill(original);
await page.getByRole('button', { name: 'Salvar nome' }).click();
await page.waitForTimeout(800);
console.log('nome devolvido:', (await title.innerText()).trim());

const thumbs = page.locator('.owd-page-thumb:not(.owd-page-add)');
if (await thumbs.count() > 1) {
    await thumbs.nth(1).click();
    await page.waitForTimeout(1500);
    const info = (await page.locator('.owd-info').innerText()).replace(/\s+/g, ' ');
    console.log('título:', (await title.innerText()).trim(), '| no aparelho:', info.split('No aparelho agora')[1]?.trim());
    await thumbs.nth(0).click();
    await page.waitForTimeout(800);
    await thumbs.nth(1).hover();
    await page.waitForTimeout(300);
    const red = await page.locator('.owd-page-thumb').evaluateAll(els => els.filter(e => getComputedStyle(e).borderTopColor === 'rgb(229, 36, 59)').length);
    console.log('miniaturas vermelhas com o mouse em outra:', red);
}
await browser.close();
