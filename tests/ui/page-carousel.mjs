// Carrossel de páginas: cria uma página de teste, renomeia pelo rótulo, põe um relógio, move, tenta excluir
// (cancela no aviso) e exclui de vez. No fim o layout fica como estava. Uso: node page-carousel.mjs <pasta>
import { chromium } from 'playwright';

const dir = process.argv[2] ?? '.';
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
await page.evaluate(() => { history.pushState({}, '', '/'); dispatchEvent(new PopStateEvent('popstate')); });
await page.waitForTimeout(1500);

const cards = page.locator('.owd-page-card:not(.owd-page-add)');
const names = async () => (await cards.locator('.owd-page-name').allInnerTexts()).map(n => n.trim());
const before = await names();
console.log('antes:', before.join(' | '));

await page.locator('.owd-page-add').click();
await page.waitForTimeout(1000);
const card = () => cards.last();

// Renomear pelo rótulo: duplo clique abre o campo; Enter grava.
await card().locator('.owd-page-name').dblclick();
await card().locator('.owd-page-name-edit input').fill('Teste do carrossel');
await card().locator('.owd-page-name-edit input').press('Enter');
await page.waitForTimeout(800);
console.log('renomeada:', (await names()).at(-1));

// Um relógio na página nova, para a exclusão ter o que avisar.
await page.getByRole('button', { name: 'Adicionar widget' }).click();
await page.locator('.owd-pick', { hasText: 'Relógio' }).click();
await page.waitForTimeout(800);
await page.locator('.mud-dialog').getByRole('button', { name: 'Adicionar' }).click();
await page.waitForTimeout(1200);

await card().getByRole('button', { name: 'Mover Teste do carrossel para a esquerda' }).click();
await page.waitForTimeout(800);
console.log('depois de mover:', (await names()).join(' | '));

const testCard = cards.filter({ hasText: 'Teste do carrossel' });
await testCard.getByRole('button', { name: 'Excluir Teste do carrossel' }).click();
await page.waitForTimeout(600);
console.log('aviso:', (await page.locator('.mud-dialog').innerText()).replace(/\s+/g, ' '));
await page.screenshot({ path: `${dir}/excluir-pagina.png` });
await page.getByRole('button', { name: 'Cancelar' }).click();
await page.waitForTimeout(500);
console.log('cancelou, continua:', (await names()).includes('Teste do carrossel'));

await testCard.getByRole('button', { name: 'Excluir Teste do carrossel' }).click();
await page.waitForTimeout(500);
await page.getByRole('button', { name: 'Excluir com os widgets' }).click();
await page.waitForTimeout(1000);
const after = await names();
console.log('depois:', after.join(' | '), '| igual ao início:', JSON.stringify(after) === JSON.stringify(before));
console.log('barra de erro do Blazor:', await page.locator('#blazor-error-ui').isVisible());
await cards.first().locator('.owd-thumb-grid').click();
await browser.close();
