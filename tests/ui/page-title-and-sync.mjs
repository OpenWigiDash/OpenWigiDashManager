// Renomeia a página pelo título (e devolve o nome), troca de página pela miniatura e confere que
// título, miniatura e a faixa "No aparelho agora" andam juntos. Uso: node page-title-and-sync.mjs <pasta>
import { chromium } from 'playwright';

const dir = process.argv[2] ?? '.';
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
await page.evaluate(() => { history.pushState({}, '', '/'); dispatchEvent(new PopStateEvent('popstate')); });
await page.waitForTimeout(2000);

const title = page.locator('.owd-page-title input');
const original = await title.inputValue();
await title.fill(`${original} (teste)`);
await page.waitForTimeout(1200);
console.log('miniatura com o nome novo:', await page.locator('.owd-page-thumb.selected .owd-page-name').innerText());
await title.fill(original);
await page.waitForTimeout(1200);
console.log('nome devolvido:', await page.locator('.owd-page-thumb.selected .owd-page-name').innerText());

const thumbs = page.locator('.owd-page-thumb:not(.owd-page-add)');
if (await thumbs.count() > 1) {
    await thumbs.nth(1).click();
    await page.waitForTimeout(1500);
    const info = (await page.locator('.owd-info').innerText()).replace(/\s+/g, ' ');
    console.log('título:', await title.inputValue(), '| no aparelho:', info.split('No aparelho agora')[1]?.trim());
    await page.screenshot({ path: `${dir}/sync-pagina.png` });
    await thumbs.nth(0).click();
    await page.waitForTimeout(800);
}
await browser.close();
