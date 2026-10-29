// Confere o brilho mostrado em Ajustes e a tecla Delete no editor de widgets. Não muda nada:
// a confirmação do Delete é CANCELADA. Uso: node brightness-and-delete-key.mjs <pasta-das-capturas>
import { chromium } from 'playwright';

const dir = process.argv[2] ?? '.';
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
const go = path => page.evaluate(p => { history.pushState({}, '', p); dispatchEvent(new PopStateEvent('popstate')); }, path);

await go('/ajustes');
await page.waitForTimeout(1500);
const brightness = await page.locator('.mud-slider input[type=range]').inputValue();
console.log('brilho em Ajustes:', brightness);
await page.screenshot({ path: `${dir}/ajustes-brilho.png` });

await go('/widgets');
await page.waitForTimeout(1500);
await page.locator('.owd-widget').first().click({ position: { x: 20, y: 20 } });
await page.waitForTimeout(400);

// Delete digitando o nome não pode abrir a confirmação.
const name = page.locator('.mud-input-control', { has: page.locator('label', { hasText: /^Nome$/ }) }).locator('input');
await name.click();
await name.press('End');
await page.keyboard.press('Delete');
await page.waitForTimeout(400);
const askedWhileTyping = await page.getByText('desta página?').count();
console.log('Delete no campo de nome pediu confirmação:', askedWhileTyping > 0);

// Fora do campo: clica no widget de novo e aperta Delete.
await page.locator('.owd-widget').first().click({ position: { x: 20, y: 20 } });
await page.keyboard.press('Delete');
await page.waitForTimeout(600);
const asked = await page.getByText('desta página?').count();
console.log('Delete com o widget selecionado pediu confirmação:', asked > 0);
await page.screenshot({ path: `${dir}/delete-confirmar.png` });
if (asked)
    await page.getByRole('button', { name: 'Cancelar' }).click();
await page.waitForTimeout(400);
console.log('widget continua:', (await page.locator('.owd-widget').count()) > 0);
await browser.close();
if (!asked || askedWhileTyping)
    process.exit(1);
