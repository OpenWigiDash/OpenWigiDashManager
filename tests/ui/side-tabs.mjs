// Painel do widget em abas (Geral / Configurações). Só navega. Uso: node side-tabs.mjs <pasta>
import { chromium } from 'playwright';
const dir = process.argv[2];
const browser = await chromium.connectOverCDP('http://127.0.0.1:9333');
const page = browser.contexts()[0].pages()[0];
await page.waitForTimeout(1500);
await page.locator('.owd-widget').first().click({ position: { x: 30, y: 30 } });
await page.waitForTimeout(1000);
console.log('abas:', (await page.locator('.owd-side-tabs .mud-tab').allInnerTexts()).map(t => t.trim()).join(' | '));
await page.screenshot({ path: `${dir}/abas-geral.png` });
await page.locator('.owd-side-tabs .mud-tab', { hasText: 'Configurações' }).evaluate(e => e.click());
await page.waitForTimeout(1000);
console.log('plugin na aba:', await page.locator('.owd-widget-settings').count() > 0, '| erro:', await page.locator('#blazor-error-ui').isVisible());
await page.screenshot({ path: `${dir}/abas-config.png` });
await page.locator('.owd-side-tabs .mud-tab', { hasText: 'Geral' }).evaluate(e => e.click());
await browser.close();
