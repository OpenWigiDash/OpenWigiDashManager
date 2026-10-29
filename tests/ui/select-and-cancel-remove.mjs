// Seleciona o primeiro widget da grade, captura o painel (nome editável), clica em "Tirar" e CANCELA
// a confirmação. Não muda o layout. Uso: node select-and-cancel-remove.mjs <pasta-das-capturas>
import { chromium } from 'playwright';

const dir = process.argv[2] ?? '.';
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
await page.evaluate(() => { history.pushState({}, '', '/widgets'); dispatchEvent(new PopStateEvent('popstate')); });
await page.waitForTimeout(1500);

await page.locator('.owd-widget').first().click({ position: { x: 20, y: 20 } });
await page.waitForTimeout(600);
const name = await page.locator('.mud-input-control', { has: page.locator('label', { hasText: /^Nome$/ }) }).locator('input').inputValue();
console.log('nome no painel:', name);
await page.screenshot({ path: `${dir}/selecionado.png` });

await page.getByRole('button', { name: 'Tirar' }).click();
await page.waitForTimeout(600);
const asked = await page.getByText('desta página?').count();
console.log('pediu confirmação:', asked > 0);
await page.screenshot({ path: `${dir}/confirmar.png` });
await page.getByRole('button', { name: 'Cancelar' }).click();
await page.waitForTimeout(500);
console.log('widget continua:', (await page.locator('.owd-widget').count()) > 0);
await browser.close();
if (!asked)
    process.exit(1);
