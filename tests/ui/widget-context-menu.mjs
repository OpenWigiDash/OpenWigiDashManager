// Menu de contexto dos widgets: cria duas páginas de teste, põe um relógio, duplica, copia e cola na outra página,
// move pelo menu e arrasta até a miniatura de outra página. No fim apaga as duas páginas, e o layout fica como
// estava. Uso: node widget-context-menu.mjs <pasta>   (o manager aberto com a porta CDP)
import { chromium } from 'playwright';

const dir = process.argv[2] ?? '.';
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
await page.evaluate(() => { history.pushState({}, '', '/'); dispatchEvent(new PopStateEvent('popstate')); });
await page.waitForTimeout(1500);

const cards = page.locator('.owd-page-card:not(.owd-page-add)');
const card = name => cards.filter({ has: page.locator('.owd-page-name', { hasText: name }) });
const count = async name => card(name).locator('.owd-thumb-widget').count();
const canvasWidgets = page.locator('.owd-canvas .owd-widget');
let failed = false;
const check = (label, ok) => { console.log(ok ? 'ok  ' : 'FALHOU', label); failed ||= !ok; };

async function newPage(name) {
    await page.locator('.owd-page-add').click();
    await page.waitForTimeout(1000);
    await cards.last().locator('.owd-page-name').dblclick();
    await cards.last().locator('.owd-page-name-edit input').fill(name);
    await cards.last().locator('.owd-page-name-edit input').press('Enter');
    await page.waitForTimeout(800);
}

async function menu(widget, item) {
    await widget.click({ button: 'right' });
    await page.waitForTimeout(500);
    await page.locator('.mud-popover-open .mud-menu-item', { hasText: item }).first().click();
    await page.waitForTimeout(1000);
}

await newPage('Teste menu A');
await page.getByRole('button', { name: 'Adicionar widget' }).click();
await page.locator('.owd-pick', { hasText: 'Relógio' }).click();
await page.waitForTimeout(800);
await page.locator('.mud-dialog').getByRole('button', { name: 'Adicionar' }).click();
await page.waitForTimeout(1200);
check('relógio na página A', await count('Teste menu A') === 1);

// Duplicar pelo menu: a cópia fica na mesma página.
await canvasWidgets.first().click({ button: 'right' });
await page.waitForTimeout(500);
await page.screenshot({ path: `${dir}/menu-contexto.png` });
await page.locator('.mud-popover-open .mud-menu-item', { hasText: 'Duplicar' }).first().click();
await page.waitForTimeout(1000);
check('duplicar: 2 widgets na A', await count('Teste menu A') === 2);

// Copiar na A, colar na B pela barra do Canvas.
await menu(canvasWidgets.first(), 'Copiar');
check('barra mostra o copiado', await page.locator('.owd-clipboard').isVisible());
await newPage('Teste menu B');
await page.locator('.owd-clipboard').getByRole('button', { name: 'Colar' }).click();
await page.waitForTimeout(1000);
check('colar: 1 widget na B', await count('Teste menu B') === 1);
check('a A continua com 2', await count('Teste menu A') === 2);

// Mover para página pelo submenu: sai da B e vai para a A.
await canvasWidgets.first().click({ button: 'right' });
await page.waitForTimeout(500);
await page.locator('.mud-popover-open .mud-menu-item', { hasText: 'Mover para página' }).first().hover();
await page.waitForTimeout(600);
await page.screenshot({ path: `${dir}/menu-mover.png` });
await page.locator('.mud-popover-open .mud-menu-item', { hasText: 'Teste menu A' }).first().click();
await page.waitForTimeout(1000);
check('mover: B vazia', await count('Teste menu B') === 0);
check('mover: A com 3', await count('Teste menu A') === 3);

// Arrastar da A até a miniatura da B.
await card('Teste menu A').locator('.owd-thumb-grid').click();
await page.waitForTimeout(1000);
const from = await canvasWidgets.first().boundingBox();
const to = await card('Teste menu B').locator('.owd-thumb-grid').boundingBox();
await page.mouse.move(from.x + 20, from.y + 20);
await page.mouse.down();
await page.mouse.move(from.x + 40, from.y + 40, { steps: 5 });
await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 20 });
await page.waitForTimeout(400);
check('miniatura da B aceita', (await card('Teste menu B').getAttribute('class')).includes('drop-ok'));
await page.screenshot({ path: `${dir}/arrastar-pagina.png` });
await page.mouse.up();
await page.waitForTimeout(1200);
check('arrastar: B com 1', await count('Teste menu B') === 1);
check('arrastar: A com 2', await count('Teste menu A') === 2);

// Limpeza: as duas páginas saem com os widgets.
for (const name of ['Teste menu A', 'Teste menu B']) {
    await card(name).getByRole('button', { name: `Excluir ${name}` }).click();
    await page.waitForTimeout(500);
    await page.getByRole('button', { name: 'Excluir com os widgets' }).click();
    await page.waitForTimeout(1000);
}
console.log('barra de erro do Blazor:', await page.locator('#blazor-error-ui').isVisible());
await cards.first().locator('.owd-thumb-grid').click();
await browser.close();
if (failed)
    process.exit(1);
