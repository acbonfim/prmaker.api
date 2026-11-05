import { open, shot, sleep } from './lib.mjs';
const { browser, page, logs } = await open({ width: 1440, height: 960 });
await page.evaluateOnNewDocument(() => { try { for (const k of Object.keys(localStorage)) {} } catch {} });
await page.goto('http://localhost:4200/auth/workbench?card=77434', { waitUntil: 'networkidle2' });
await sleep(3000);
// fecha o guia de primeiros passos / tour se aparecerem
for (const sel of ['.setup-guide button[aria-label*="Fechar"]', 'button[mat-dialog-close]', '.driver-popover-close-btn']) {
  const b = await page.$(sel); if (b) { await b.click().catch(() => {}); await sleep(500); }
}
await page.keyboard.press('Escape'); await sleep(800);
await shot(page, '01-bancada');
console.log((await page.evaluate(() => document.querySelector('app-work-item-list')?.innerText || 'SEM LISTA')).slice(0, 1200));
console.log(logs.join('\n'));
await browser.close();
