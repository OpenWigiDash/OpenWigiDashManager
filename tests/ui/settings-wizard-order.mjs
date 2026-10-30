// Configurações do plugin no painel, assistente com prévia e ordem das páginas (volta tudo como estava).
// Uso: node settings-wizard-order.mjs <pasta-das-capturas>
import { chromium } from 'playwright';
const dir = process.argv[2] ?? '.';
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
await page.evaluate(() => { history.pushState({}, '', '/'); dispatchEvent(new PopStateEvent('popstate')); });
await page.waitForTimeout(2000);

await page.locator('.owd-widget').first().click({ position: { x: 30, y: 30 } });
await page.waitForTimeout(1200);
console.log('configurações do widget no painel:', await page.locator('.owd-widget-settings').count() > 0);
await page.screenshot({ path: `${dir}/config-inline.png`, fullPage: true });

await page.getByRole('button', { name: 'Adicionar widget' }).click();
await page.waitForSelector('.owd-pick');
await page.locator('.owd-pick', { hasText: 'Relógio' }).click();
await page.waitForTimeout(1500);
console.log('assistente: widgets da página ao vivo:', await page.locator('.owd-grid-small .owd-occupied img.owd-live').count(),
  '| prévia do novo:', await page.locator('.owd-drop-preview img').count());
await page.screenshot({ path: `${dir}/assistente-previa.png` });
await page.getByRole('button', { name: 'Cancelar' }).click();
await page.waitForTimeout(500);

const names = async () => (await page.locator('.owd-page-thumb:not(.owd-page-add) .owd-page-name').allInnerTexts()).map(n => n.trim());
const before = await names();
await page.getByRole('button', { name: 'Mover página para a direita' }).click();
await page.waitForTimeout(800);
const moved = await names();
console.log('ordem:', before.join(' | '), '->', moved.join(' | '), '| selecionada:', (await page.locator('.owd-page-thumb.selected .owd-page-name').innerText()).trim());
await page.getByRole('button', { name: 'Mover página para a esquerda' }).click();
await page.waitForTimeout(800);
console.log('voltou:', (await names()).join(' | '));
const broke = await page.locator('#blazor-error-ui').isVisible();
console.log('barra de erro do Blazor:', broke);
await browser.close();
