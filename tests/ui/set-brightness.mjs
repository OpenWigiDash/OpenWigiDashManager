// Lê o brilho mostrado em Ajustes e, se passar um valor, muda pelo cursor.
// Uso: node set-brightness.mjs [0-100]
import { chromium } from 'playwright';

const value = process.argv[2];
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
await page.evaluate(() => { history.pushState({}, '', '/ajustes'); dispatchEvent(new PopStateEvent('popstate')); });
await page.waitForTimeout(1500);
const slider = page.locator('.mud-slider input[type=range]');
console.log('brilho em Ajustes:', await slider.inputValue());
if (value) {
    await slider.fill(value);
    await page.waitForTimeout(500);
    console.log('mudado para:', await slider.inputValue());
}
await browser.close();
