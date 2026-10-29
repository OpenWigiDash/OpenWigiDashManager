// Abre cada tela do manager e lista erros do console e a barra "Algo deu errado".
// Uso: node console-errors.mjs   (manager aberto com a porta CDP do WebView2)
import { chromium } from 'playwright';

const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
const errors = [];
page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errors.push(`${m.type()}: ${m.text()}`); });
page.on('pageerror', e => errors.push(`pageerror: ${e.message}`));

let failed = false;
for (const route of ['/', '/widgets', '/ajustes']) {
    await page.evaluate(r => { history.pushState({}, '', r); dispatchEvent(new PopStateEvent('popstate')); }, route);
    await page.waitForTimeout(2500);
    const banner = await page.locator('#blazor-error-ui').isVisible();
    console.log(`${route}: barra de erro ${banner ? 'VISÍVEL' : 'escondida'}`);
    failed ||= banner;
}
console.log(errors.length ? errors.join('\n') : 'sem erros no console');
await browser.close();
if (failed || errors.some(e => e.startsWith('error') || e.startsWith('pageerror')))
    process.exit(1);
