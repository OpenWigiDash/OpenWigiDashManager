// Abre a configuração do primeiro Stream Deck Mirror do layout, abre o seletor de cor, tira uma
// captura e fecha sem mudar nada. Uso: node color-picker.mjs <instanceId> <captura.png>
import { chromium } from 'playwright';

const [instanceId, out] = process.argv.slice(2);
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
await page.evaluate(r => { history.pushState({}, '', r); dispatchEvent(new PopStateEvent('popstate')); }, `/widgets/${instanceId}`);
await page.waitForTimeout(1500);
await page.locator('.mud-picker input').first().click();
await page.waitForTimeout(800);
await page.screenshot({ path: out });
await page.keyboard.press('Escape');
await browser.close();
