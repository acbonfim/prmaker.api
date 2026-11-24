import { open, shot, sleep } from './lib.mjs';
const epic = process.argv[2];
const { browser, page, logs } = await open({ width: 1680, height: 1000 });
await page.goto(`http://localhost:4399/auth/desk?epic=${epic}`, { waitUntil: 'networkidle2' });
await sleep(2500);
const box = async sel => { const el = await page.$(sel); return el ? el.boundingBox() : null; };
// cartão 90007 (lane Padrão, coluna New) → célula Doing da coluna Active na mesma lane
const cards = await page.$$('[class*="dvb-card"]');
let src = null;
for (const c of cards) { const t = await c.evaluate(e => e.textContent); if (t.includes('90007') && !src) src = c; }
const sb = await src.boundingBox();
const cells = await page.$$eval('[cdkdroplist], .cdk-drop-list', els => els.map(e => { const r = e.getBoundingClientRect(); return { x: r.x, y: r.y, w: r.width, h: r.height, id: e.id, cls: e.className }; }));
console.log('drop lists', cells.length, JSON.stringify(cells.slice(0, 10)));
// alvo: 2ª lista da 1ª lane (Active Doing) — a que fica à direita do cartão na mesma faixa
const target = cells.filter(c => Math.abs(c.y - sb.y) < 60 && c.x > sb.x + 50).sort((a, b) => a.x - b.x)[0];
console.log('alvo', JSON.stringify(target));
await page.mouse.move(sb.x + 40, sb.y + 15); await page.mouse.down();
for (let i = 1; i <= 20; i++) { await page.mouse.move(sb.x + 40 + (target.x + 40 - sb.x - 40) * i / 20, sb.y + 15 + (target.y + 30 - sb.y - 15) * i / 20); await sleep(30); }
await sleep(300); await shot(page, '02-arrastando'); await page.mouse.up(); await sleep(2000);
await shot(page, '03-depois-arraste');
const log = await (await fetch('http://127.0.0.1:8199/log')).json();
console.log('ultimas escritas', JSON.stringify(log.slice(-2).map(e => [e.m, e.p.slice(-30), e.body])).slice(0, 600));
// abre o painel do 90002 pelo Board do time
await page.evaluate(() => [...document.querySelectorAll('button')].find(b => b.textContent.trim() === 'Board do time')?.click());
await sleep(2000); await shot(page, '04-board-do-time');
const t = await page.$$('[class*="dvb-card"]');
for (const c of t) { const tx = await c.evaluate(e => e.textContent); if (tx.includes('90002')) { const tt = await c.$('[class*="title"]') ?? c; await tt.click(); break; } }
await sleep(2500); await shot(page, '05-painel-card');
console.log('LOGS', logs.filter(l => !l.includes('NG0303')).join('\n'));
await browser.close();
