// Tela inicial (editor): faixa de estado, miniaturas das páginas e o switch "Mostrar nomes".
// Volta o switch e a página como estavam. Uso: node editor-home.mjs <pasta-das-capturas>
import { chromium } from 'playwright';
const dir = process.argv[2] ?? '.';
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
await page.evaluate(() => { history.pushState({}, '', '/'); dispatchEvent(new PopStateEvent('popstate')); });
await page.waitForTimeout(3000);
console.log('menu Painel some:', (await page.getByRole('link', { name: 'Painel' }).count()) === 0);
console.log('faixa de estado:', await page.locator('.owd-info').innerText().then(t => t.replace(/\s+/g, ' ')));
console.log('miniaturas de página:', await page.locator('.owd-page-thumb:not(.owd-page-add)').count(),
  '/ com prévia:', await page.locator('.owd-page-thumb img.owd-live').count());
await page.screenshot({ path: `${dir}/home-nomes.png`, fullPage: true });

const sw = page.locator('.mud-switch', { hasText: 'Mostrar nomes' }).locator('input');
const before = await sw.isChecked();
if (before) await sw.click();
await page.waitForTimeout(800);
console.log('rótulos visíveis sem nomes:', await page.locator('.owd-widget-label:not(.hidden)').count());
await page.screenshot({ path: `${dir}/home-sem-nomes.png`, fullPage: true });
if (before) await sw.click();
await page.waitForTimeout(400);

const thumbs = page.locator('.owd-page-thumb:not(.owd-page-add)');
if (await thumbs.count() > 1) {
  await thumbs.nth(1).click(); await page.waitForTimeout(1200);
  console.log('trocou para:', await page.locator('.owd-page-thumb.selected .owd-page-name').innerText());
  await thumbs.nth(0).click(); await page.waitForTimeout(800);
}
await browser.close();
