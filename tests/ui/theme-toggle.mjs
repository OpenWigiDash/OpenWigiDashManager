// Alterna o tema em Ajustes e captura cada estado nas três telas, para achar mistura de claro e escuro.
// Termina no mesmo tema em que começou. Uso: node theme-toggle.mjs <pasta-das-capturas>
import { chromium } from 'playwright';

const dir = process.argv[2] ?? '.';
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
const go = async route => {
    await page.evaluate(r => { history.pushState({}, '', r); dispatchEvent(new PopStateEvent('popstate')); }, route);
    await page.waitForTimeout(1200);
};
const colors = () => page.evaluate(() => ({
    body: getComputedStyle(document.body).backgroundColor,
    main: getComputedStyle(document.querySelector('.mud-main-content')).backgroundColor,
    drawer: getComputedStyle(document.querySelector('.mud-drawer')).backgroundColor,
    paper: getComputedStyle(document.querySelector('.mud-paper') ?? document.body).backgroundColor,
    dark: getComputedStyle(document.documentElement).getPropertyValue('--mud-palette-background').trim(),
}));

const toggle = async () => {
    await go('/ajustes');
    await page.locator('.mud-switch', { hasText: 'Tema escuro' }).locator('input').click();
    await page.waitForTimeout(800);
};

for (const step of ['inicial', 'trocado', 'voltou']) {
    for (const route of ['/', '/widgets', '/ajustes']) {
        await go(route);
        console.log(step, route, JSON.stringify(await colors()));
        await page.screenshot({ path: `${dir}/tema-${step}${route.replaceAll('/', '_') || '_'}.png` });
    }
    if (step !== 'voltou')
        await toggle();
}
await browser.close();
