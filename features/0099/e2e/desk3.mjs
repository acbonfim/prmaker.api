import { open, shot, sleep } from './lib.mjs';
const epic = process.argv[2];
const { browser, page, logs } = await open({ width: 1680, height: 1000 });
await page.goto(`http://localhost:4399/auth/desk?epic=${epic}&card=90002`, { waitUntil: 'networkidle2' });
await sleep(3000); await shot(page, '06-painel-identidade');
await page.evaluate(() => [...document.querySelectorAll('button')].find(b => b.getAttribute('aria-label')?.includes('Fechar') || b.textContent.trim() === 'close')?.click());
await page.goto(`http://localhost:4399/auth/desk?epic=${epic}`, { waitUntil: 'networkidle2' }); await sleep(2500);
await page.evaluate(() => [...document.querySelectorAll('button')].find(b => b.textContent.includes('Novo card'))?.click());
await sleep(2500); await shot(page, '07-novo-card');
// tipo Bug + título + tag nova
const dlg = await page.$('mat-dialog-container');
const txt = await dlg.evaluate(e => e.innerText.slice(0, 600)); console.log('DIALOG', txt.replace(/\n+/g, ' | '));
await page.evaluate(() => [...document.querySelectorAll('button')].find(b => b.textContent.trim() === 'Itens')?.click());
await sleep(500);
console.log('LOGS', logs.filter(l => !l.includes('NG0303')).join('\n'));
await browser.close();
