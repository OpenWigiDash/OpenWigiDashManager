// Gera os ícones do OpenWigiDash a partir dos desenhos abaixo: SVGs fonte, PNGs e .ico.
// Uso: npm i -D playwright (uma vez) e depois `node scripts/branding/gerar-icones.mjs`.
// O Chromium do Playwright rasteriza cada tamanho direto do vetor, sem reduzir um bitmap grande.
import { mkdir, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const out = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'assets', 'branding');

const K = { red: '#E5243B', cyan: '#2EC4C6', green: '#3DD68C', grey: '#8B93A7', amber: '#F2B544',
  body: '#1B1D24', rim: '#6B7080' };
// Cores da bandeja por tema da barra de tarefas.
const TB = { dark: { fg: '#E8E9ED', dim: '#7C808A' }, light: { fg: '#1B1D24', dim: '#8A8E98' } };
// Os três estados do agente (AgentStatus): dono da tela, outra sessão, sem aparelho ou serviço.
const STATES = [
  { id: 'owner', dot: K.green, live: true },
  { id: 'waiting', dot: K.grey, live: false },
  { id: 'nodevice', dot: K.amber, live: false },
];

const svg = (vb, inner) =>
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${vb} ${vb}" width="${vb}" height="${vb}">${inner.trim()}</svg>\n`;

// Desenho completo, para 40 px ou mais.
const full = () => svg(256, `
<defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2A2D36"/><stop offset="1" stop-color="#15161B"/></linearGradient></defs>
<rect x="116" y="188" width="24" height="26" fill="#2A2D36"/>
<rect x="84" y="210" width="88" height="14" rx="7" fill="#3A3E4A"/>
<rect x="18" y="46" width="220" height="148" rx="20" fill="url(#g)" stroke="#4A4D57" stroke-width="4"/>
<rect x="34" y="62" width="188" height="116" rx="8" fill="#0E0F13"/>
<rect x="44" y="72" width="74" height="96" rx="6" fill="${K.red}"/>
<path d="M58 146A23 23 0 0 1 104 146" fill="none" stroke="#fff" stroke-opacity=".4" stroke-width="7" stroke-linecap="round"/>
<path d="M58 146A23 23 0 0 1 97.26 129.74" fill="none" stroke="#fff" stroke-width="7" stroke-linecap="round"/>
<rect x="126" y="72" width="86" height="44" rx="6" fill="#1F2128"/>
<polyline points="136,104 150,94 162,100 176,84 190,91 202,80" fill="none" stroke="${K.cyan}" stroke-width="6" stroke-linecap="round" stroke-linejoin="round"/>
<rect x="126" y="124" width="86" height="44" rx="6" fill="#1F2128"/>
<rect x="138" y="134" width="62" height="8" rx="4" fill="#2A2D36"/><rect x="138" y="134" width="44" height="8" rx="4" fill="${K.cyan}"/>
<rect x="138" y="150" width="62" height="8" rx="4" fill="#2A2D36"/><rect x="138" y="150" width="26" height="8" rx="4" fill="${K.grey}"/>`);

// Desenho simplificado na grade de 16 px, para 32 px ou menos.
const small = () => svg(16, `
<rect x=".5" y="2.5" width="15" height="10" rx="2" fill="${K.body}" stroke="${K.rim}"/>
<rect x="2" y="4" width="5" height="7" rx=".75" fill="${K.red}"/>
<path d="M8.5 9.75L10.25 7.5L11.75 8.75L13.75 5.5" fill="none" stroke="${K.cyan}" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/>
<path d="M5 14.5h6" stroke="${K.rim}" stroke-linecap="round"/>`);

// Bandeja: contorno na cor da barra, conteúdo colorido só quando a tela é sua, ponto de estado no canto.
const tray = (s, t) => svg(16, `
<mask id="m"><rect width="16" height="16" fill="#fff"/><circle cx="12.5" cy="12.5" r="4.4" fill="#000"/></mask>
<g mask="url(#m)">
<rect x=".5" y="2.5" width="15" height="10" rx="2" fill="none" stroke="${s.live ? t.fg : t.dim}"/>
<rect x="2" y="4" width="5" height="7" rx=".75" fill="${s.live ? K.red : t.dim}"/>
<path d="M8.5 9.75L10.25 7.5L11.75 8.75L13.75 5.5" fill="none" stroke="${s.live ? K.cyan : t.dim}" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/>
<path d="M5 14.5h6" stroke="${s.live ? t.fg : t.dim}" stroke-linecap="round"/>
</g>
<circle cx="12.5" cy="12.5" r="3.1" fill="${s.dot}"/>`);

const APP_SIZES = [16, 20, 24, 32, 40, 48, 64, 256];
const TRAY_SIZES = [16, 20, 24, 32];

// .ico: entradas BMP 32 bits abaixo de 256 (o System.Drawing.Icon lê sem surpresa) e PNG em 256.
function ico(frames) {
  const header = Buffer.alloc(6 + 16 * frames.length);
  header.writeUInt16LE(0, 0); header.writeUInt16LE(1, 2); header.writeUInt16LE(frames.length, 4);
  const blobs = frames.map(f => (f.size >= 256 ? f.png : dib(f.size, f.rgba)));
  let offset = header.length;
  frames.forEach((f, i) => {
    const e = 6 + 16 * i;
    header.writeUInt8(f.size >= 256 ? 0 : f.size, e);
    header.writeUInt8(f.size >= 256 ? 0 : f.size, e + 1);
    header.writeUInt16LE(1, e + 4); header.writeUInt16LE(32, e + 6);
    header.writeUInt32LE(blobs[i].length, e + 8); header.writeUInt32LE(offset, e + 12);
    offset += blobs[i].length;
  });
  return Buffer.concat([header, ...blobs]);
}

function dib(size, rgba) {
  const maskRow = Math.ceil(size / 32) * 4;
  const info = Buffer.alloc(40);
  info.writeUInt32LE(40, 0); info.writeInt32LE(size, 4); info.writeInt32LE(size * 2, 8);
  info.writeUInt16LE(1, 12); info.writeUInt16LE(32, 14);
  info.writeUInt32LE(size * size * 4 + maskRow * size, 20);
  const pixels = Buffer.alloc(size * size * 4);
  const mask = Buffer.alloc(maskRow * size);
  for (let y = 0; y < size; y++) {
    const row = size - 1 - y; // DIB é de baixo para cima
    for (let x = 0; x < size; x++) {
      const s = (y * size + x) * 4, d = (row * size + x) * 4;
      pixels[d] = rgba[s + 2]; pixels[d + 1] = rgba[s + 1]; pixels[d + 2] = rgba[s]; pixels[d + 3] = rgba[s + 3];
      if (rgba[s + 3] === 0) mask[row * maskRow + (x >> 3)] |= 0x80 >> (x & 7);
    }
  }
  return Buffer.concat([info, pixels, mask]);
}

const browser = await chromium.launch();
const page = await browser.newPage();
async function raster(svgText, size) {
  const r = await page.evaluate(async ([text, n]) => {
    const im = new Image();
    im.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(text);
    await im.decode();
    const c = document.createElement('canvas'); c.width = c.height = n;
    const g = c.getContext('2d'); g.drawImage(im, 0, 0, n, n);
    return { rgba: Array.from(g.getImageData(0, 0, n, n).data), png: c.toDataURL('image/png').split(',')[1] };
  }, [svgText, size]);
  return { size, rgba: Uint8Array.from(r.rgba), png: Buffer.from(r.png, 'base64') };
}

async function save(rel, data) {
  const path = join(out, rel);
  await mkdir(dirname(path), { recursive: true });
  await writeFile(path, data);
}

await save('openwigidash.svg', full());
await save('openwigidash-16.svg', small());
const appFrames = [];
for (const size of APP_SIZES) {
  const f = await raster(size <= 32 ? small() : full(), size);
  appFrames.push(f);
  await save(`png/openwigidash-${size}.png`, f.png);
}
for (const size of [512, 1024]) await save(`png/openwigidash-${size}.png`, (await raster(full(), size)).png);
await save('openwigidash.ico', ico(appFrames));

for (const s of STATES) {
  for (const [theme, t] of Object.entries(TB)) {
    const name = `tray/tray-${s.id}-${theme}`;
    await save(`${name}.svg`, tray(s, t));
    const frames = [];
    for (const size of TRAY_SIZES) frames.push(await raster(tray(s, t), size));
    await save(`${name}.ico`, ico(frames));
  }
}
await browser.close();
console.log(`Ícones gerados em ${out}`);
