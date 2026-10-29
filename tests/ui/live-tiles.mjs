import { chromium } from 'playwright';
const dir = process.argv[2] ?? '.';
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
await page.evaluate(() => { history.pushState({}, '', '/widgets'); dispatchEvent(new PopStateEvent('popstate')); });
await page.waitForTimeout(2500);
const tiles = await page.locator('.owd-widget').count();
const live = await page.locator('.owd-widget img.owd-live').count();
const a = await page.locator('img.owd-live').first().getAttribute('src').catch(() => '');
await page.waitForTimeout(2000);
const b = await page.locator('img.owd-live').first().getAttribute('src').catch(() => '');
console.log('tiles:', tiles, '/ com prévia:', live, '/ mudou em 2 s:', a !== b);
await page.screenshot({ path: `${dir}/tiles-ao-vivo.png` });
for (const n of [2, 3]) {
  const tab = page.getByText(`Página ${n}`, { exact: true });
  if (await tab.count()) { await tab.first().click(); await page.waitForTimeout(1500); await page.screenshot({ path: `${dir}/tiles-pagina-${n}.png` }); console.log(`página ${n}: com prévia`, await page.locator('img.owd-live').count()); }
}
await page.getByText('Página 1', { exact: true }).first().click();
await browser.close();
