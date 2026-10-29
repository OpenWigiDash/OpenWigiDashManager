// Abre o assistente "Adicionar widget", passa pelos dois passos e CANCELA (não mexe no layout).
// Uso: node add-widget-dialog.mjs <pasta-das-capturas>
import { chromium } from 'playwright';

const dir = process.argv[2] ?? '.';
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];

await page.getByRole('button', { name: 'Adicionar widget' }).click();
await page.waitForSelector('.owd-pick', { timeout: 5000 });
const cards = await page.locator('.owd-pick').count();
console.log('passo 1: cartões =', cards);
await page.screenshot({ path: `${dir}/add-1.png` });

await page.locator('.owd-pick').first().click();
await page.waitForSelector('.owd-grid-small', { timeout: 5000 });
const preview = await page.locator('.owd-grid-small .owd-drop').getAttribute('class').catch(() => 'sem prévia');
console.log('passo 2: prévia =', preview);
await page.screenshot({ path: `${dir}/add-2.png` });

await page.getByRole('button', { name: 'Cancelar' }).click();
await page.waitForTimeout(500);
console.log('diálogo fechado:', (await page.locator('.owd-grid-small').count()) === 0);
await browser.close();
if (cards === 0)
    process.exit(1);
