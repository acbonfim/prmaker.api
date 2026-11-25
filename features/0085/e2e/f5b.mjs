import { open, shot, sleep } from './lib.mjs';
const card = '77434', item = 48;
const { browser, page, logs } = await open({ width: 1440, height: 960 });
await page.setRequestInterception(true);
const H = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*' };
const now = Date.now(), at = (m) => new Date(now - m * 60000).toISOString().slice(0, 19);
const FILES = [
  { id: 'f1', number: 1, name: 'chamado-77434.md', kind: 'ticket', contentType: 'text/markdown', size: 2310, createdAt: at(4), taskNumber: 49, source: 'item' },
  { id: 'f2', number: 2, name: 'correcao-oee-turno-noite.sql', kind: 'script', contentType: 'application/sql', size: 1840, createdAt: at(12), taskNumber: 50, source: 'item' },
  { id: 'f3', number: 3, name: 'rollback-oee-turno-noite.sql', kind: 'script', contentType: 'application/sql', size: 960, createdAt: at(12), taskNumber: 50, source: 'item' },
  { id: '9a1', name: 'analise-77434.md', kind: 'analysis', contentType: 'text/markdown', size: 8120, createdAt: at(90), source: 'plan', contentUrl: 'ExecutionPlan/p1/artifacts/9a1/content' },
  { id: 'f4', number: 4, name: 'antes-depois-turno-c.csv', kind: 'data', contentType: 'text/csv', size: 4420, createdAt: at(20), taskNumber: 51, source: 'item' },
  { id: 'f5', number: 5, name: 'print-painel-oee.png', kind: 'attachment', contentType: 'image/png', size: 182300, createdAt: at(30), source: 'item' }
];
const TICKET = `# Chamado — Correção do OEE do turno da noite (card 77434)\n\n**Cliente:** Gerdau · **Ambiente:** gerdau-prod\n\n## O que fazer\n1. Rodar \`correcao-oee-turno-noite.sql\` no banco do cliente.\n2. Conferir o painel do OEE do turno C.\n\n## Como desfazer\nRodar \`rollback-oee-turno-noite.sql\`.`;
page.on('request', (r) => {
  const u = r.url();
  if (r.method() === 'OPTIONS' && /WorkItems\/\d+\/files|Azure\/card/.test(u)) return r.respond({ status: 204, headers: H });
  if (r.method() === 'GET' && /WorkItems\/(48|21|17)\/files$/.test(u)) return r.respond({ status: 200, contentType: 'application/json', headers: H, body: JSON.stringify(FILES) });
  if (r.method() === 'GET' && /files\/f1\/content$/.test(u)) return r.respond({ status: 200, contentType: 'text/markdown', headers: H, body: TICKET });
  if (r.method() === 'GET' && /\/files$/.test(u) && /WorkItems\/\d+\/files$/.test(u)) return r.respond({ status: 200, contentType: 'application/json', headers: H, body: '[]' });
  if (/Azure\/card\/\d+\/full/.test(u)) return r.respond({ status: 200, contentType: 'application/json', headers: H, body: JSON.stringify({ id: +card, rev: 1, fields: { 'System.WorkItemType': 'Bug', 'System.Title': 'Cálculo do indicador OEE errado no turno da noite', 'System.State': 'Active' }, comments: [], history: [], alerts: {} }) });
  r.continue();
});
await page.goto(`http://localhost:4215/auth/workbench?card=${card}&item=${item}`, { waitUntil: 'networkidle2' });
await sleep(4000);
const tab = await page.evaluateHandle(() => [...document.querySelectorAll('.wi-tab')].find((b) => b.textContent.includes('Arquivos')));
await tab.click(); await sleep(900);
const dlg = await page.$('.wi-dialog-panel .mat-mdc-dialog-surface');
await shot(page, 'f5-files', dlg);
// Copiar chamado → abrir chamado
const openBtn = await page.$('.wif__ticket button');
await openBtn?.click(); await sleep(1200);
await shot(page, 'f5-files-ticket');
await page.keyboard.press('Escape'); await sleep(500);
await page.keyboard.press('Escape'); await sleep(700);
const chip = await page.$('.wie__files');
if (chip) {
  await chip.evaluate((el) => el.scrollIntoView({ block: 'center' })); await sleep(400);
  await shot(page, 'f5-files-session', await page.$('app-work-item-execution .wie__head'));
  await chip.click(); await sleep(1000);
  await shot(page, 'f5-files-dialog', await page.$('.wi-dialog-panel .mat-mdc-dialog-surface'));
  await page.keyboard.press('Escape'); await sleep(400);
} else console.log('sem chip');
const info = await page.evaluate(() => ({ tab: [...document.querySelectorAll('.wi-tab')].map((b) => b.textContent.trim()), groups: [...document.querySelectorAll('.wif__group-title')].map((h) => h.textContent.trim()) }));
console.log(JSON.stringify(info));
console.log(logs.join('\n'));
await browser.close();
