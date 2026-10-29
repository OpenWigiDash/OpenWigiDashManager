// Fica ouvindo o console do manager e grava cada erro (com hora) num arquivo, para pegar o que
// acontece enquanto alguém usa a janela. Uso: node watch-errors.mjs <arquivo.log>
import { chromium } from 'playwright';
import { appendFileSync } from 'node:fs';

const out = process.argv[2] ?? 'manager-errors.log';
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${process.env.OWD_CDP_PORT ?? 9333}`);
const page = browser.contexts()[0].pages()[0];
const log = text => appendFileSync(out, `${new Date().toISOString()} ${text}\n`);
page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') log(`${m.type()}: ${m.text()}`); });
page.on('pageerror', e => log(`pageerror: ${e.message}\n${e.stack ?? ''}`));
log('ouvindo');
await new Promise(() => {});
