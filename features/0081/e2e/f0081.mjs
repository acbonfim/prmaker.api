import { open, sleep, shot } from './lib.mjs';
import { execSync } from 'child_process';
const out = {}; const step = async (name, fn) => { try { await fn(); } catch (e) { out[name + '_ERR'] = e.message.slice(0, 200); } };
const text = (page, sel) => page.$eval(sel, e => e.innerText).catch(() => null);
const tourInfo = page => page.evaluate(() => {
  const p = document.querySelector('.driver-popover');
  return p ? { title: p.querySelector('.driver-popover-title')?.innerText, progress: p.querySelector('.driver-popover-progress-text')?.innerText,
    skip: !!p.querySelector('.prmake-tour__skip'), next: p.querySelector('.driver-popover-next-btn')?.innerText,
    target: document.querySelector('.driver-active-element')?.getAttribute('data-tour') } : null;
});
const next = async page => { await page.click('.driver-popover-next-btn'); await sleep(700); };

// ── A. Desktop, primeira vez, vindo da Home com card: tour inteiro ────────────────────────────────
{
  const { browser, page, logs } = await open({ width: 1500, height: 960 });
  await page.goto('http://localhost:4200/auth/register?card=77881', { waitUntil: 'networkidle2', timeout: 60000 });
  await sleep(4500);
  out.A_tour = [];
  await step('A', async () => {
    for (let i = 0; i < 25; i++) {
      const t = await tourInfo(page);
      if (!t) break;
      out.A_tour.push(`${t.progress} [${t.target ?? '-'}] ${t.title} | skip=${t.skip} | ${t.next}`);
      if (t.target === 'plan-run') await shot(page, '30-tour-icone-claude');
      if (t.target === 'case-compact') await shot(page, '31-tour-especialista');
      if (i === 0) await shot(page, '29-tour-boas-vindas');
      if (t.next?.includes('Concluir')) { await shot(page, '32-tour-fim'); await next(page); break; }
      await next(page);
    }
    out.A_afterDone = await tourInfo(page);
    out.A_storage = await page.evaluate(() => Object.keys(localStorage).filter(k => k.startsWith('prmake.tour')).map(k => k + '=' + localStorage.getItem(k)));
    await page.reload({ waitUntil: 'networkidle2' }); await sleep(5000);
    out.A_afterReload = await tourInfo(page);
  });

  // ── B. Ícone do Claude no cabeçalho + popover ─────────────────────────────────────────────────
  await step('B', async () => {
    out.B_oldBar = !!(await page.$('.plan-req--running, .plan-req--claimed'));
    out.B_icon = await page.$eval('.plan__run', e => e.className).catch(() => null);
    out.B_anim = await page.$eval('.plan__run-ring', e => getComputedStyle(e).animationName).catch(() => null);
    await shot(page, '33-cabecalho-claude-rodando', await page.$('.plan__header'));
    await page.click('.plan__run'); await sleep(700);
    out.B_popover = await text(page, '.run-pop');
    await shot(page, '34-popover-claude');
    // atividade nova chega ao vivo com o popover aberto
    execSync(`curl -sS -X POST http://127.0.0.1:5083/api/v1/ExecutionQueue/$(cat ../rid-run.txt)/heartbeat -H "x-api-key: $(cat ../worker.txt)" -H 'Content-Type: application/json' -d '{"pid":4242,"activity":{"label":"Rodando os testes do upload","tool":"Bash","at":"${new Date().toISOString()}"},"recent":[{"label":"Lendo src/app/upload/upload.service.ts","tool":"Read","at":"${new Date().toISOString()}"},{"label":"Rodando os testes do upload","tool":"Bash","at":"${new Date().toISOString()}"}]}'`);
    await sleep(2500);
    out.B_popoverLive = (await text(page, '.run-pop__now'));
    await page.keyboard.press('Escape'); await sleep(400);
    out.B_closed = !(await page.$('.run-pop'));
  });

  // ── C. Conversa compacta: escreve daqui, modal abre sem recarregar e mantém o rascunho ──────────
  await step('C', async () => {
    out.C_compact = !!(await page.$('app-case-compact .ck'));
    out.C_timelineInColumn = !!(await page.$('.pr-timeline app-card-timeline'));
    await page.click('.ck__box'); await page.keyboard.type('Pergunta rápida pela versão compacta: a branch de QA aceita PNG?');
    await page.keyboard.press('Enter'); await sleep(1800);
    out.C_lastMine = await page.evaluate(() => [...document.querySelectorAll('.km--mine .km__text')].at(-1)?.innerText);
    await shot(page, '35-especialista-compacto', await page.$('.pr-timeline'));
    // rascunho no compacto → aparece no modal
    await page.click('.ck__box'); await page.keyboard.type('rascunho compartilhado');
    await page.click('.ck__expand'); await sleep(1200);
    out.C_modalDraft = await page.$eval('.cc textarea', e => e.value).catch(() => null);
    out.C_modalLoading = await text(page, '.cc__state');
    await page.keyboard.press('Escape'); await sleep(800);
    // reabrir: sem "Abrindo a conversa…"
    let reqs = 0; const onReq = r => { if (r.url().includes('/CaseChat/77881') && r.method() === 'GET') reqs++; };
    page.on('request', onReq);
    await page.click('.plan__spec'); await sleep(150);
    out.C_reopenState = await text(page, '.cc__state');
    out.C_reopenMessages = await page.$$eval('.cc .msg', l => l.length).catch(() => 0);
    await sleep(1200);
    out.C_reopenGets = reqs; page.off('request', onReq);
    await page.keyboard.press('Escape'); await sleep(700);
  });

  // ── D. Mensagem de outra pessoa chega por WebSocket com a conversa grande fechada ────────────────
  await step('D', async () => {
    execSync(`curl -sS -X POST http://127.0.0.1:5083/api/v1/CaseChat/77881/messages -H "x-api-key: $(cat ../daniel.txt)" --form-string 'text=Daniel aqui: testei na QA e o PNG passou depois do ajuste.' --form-string 'peopleOnly=true' > /dev/null`);
    await sleep(2500);
    out.D_compactGotIt = await page.evaluate(() => [...document.querySelectorAll('.km__text')].some(e => e.innerText.includes('Daniel aqui')));
    out.D_headerBadge = await text(page, '.plan__spec-badge');
    await shot(page, '36-ao-vivo-sem-modal', await page.$('.pr-timeline'));
  });

  // ── E. Linha do tempo no topo, em tela cheia ──────────────────────────────────────────────────
  await step('E', async () => {
    out.E_btn = await text(page, '[data-tour="timeline"]');
    await page.click('[data-tour="timeline"]'); await sleep(1200);
    out.E_expanded = !!(await page.$('body > .timeline.timeline--expanded'));
    await shot(page, '37-linha-do-tempo-tela-cheia');
    await page.keyboard.press('Escape'); await sleep(900);
    out.E_closed = !(await page.$('.timeline--expanded'));
    await shot(page, '38-card-desktop', null);
  });

  // ── F. Ajuda: guia completo e "Mostrar na tela" ───────────────────────────────────────────────
  await step('F', async () => {
    await page.click('[data-tour="tour-help"]'); await sleep(900);
    out.F_groups = await page.$$eval('.th__group h3', l => l.map(e => e.innerText));
    await shot(page, '39-guia-como-usar');
    await page.evaluate(() => [...document.querySelectorAll('.th__item')].find(a => a.innerText.includes('Especialista do caso'))?.querySelector('.th__show')?.click());
    await sleep(1500);
    out.F_tourFrom = await tourInfo(page);
    await page.click('.prmake-tour__skip'); await sleep(500);
    out.F_skipped = !(await tourInfo(page));
  });
  out.A_logs = logs.slice(0, 12);
  await browser.close();
}

// ── G. Entrada sem card: só boas-vindas + busca; buscar o card continua o tour ──────────────────────
{
  const { browser, page, logs } = await open({ width: 1440, height: 900, who: 'daniel' });
  await page.goto('http://localhost:4200/auth/register', { waitUntil: 'networkidle2', timeout: 60000 });
  await sleep(2500);
  await step('G', async () => {
    out.G_first = await tourInfo(page);
    await next(page);
    out.G_second = await tourInfo(page);
    await shot(page, '40-tour-busca-card');
    await page.click('[data-tour="card-search"] input'); await page.keyboard.type('77881'); await page.keyboard.press('Enter');
    await sleep(7000);
    out.G_afterSearch = await tourInfo(page);
    await shot(page, '41-tour-continua-no-card');
  });
  out.G_logs = logs.slice(0, 8);
  await browser.close();
}

// ── H. Celular: aba Especialista e o tour abrindo a aba certa ───────────────────────────────────────
{
  const { browser, page, logs } = await open({ width: 390, height: 844, mobile: true });
  await page.goto('http://localhost:4200/auth/register?card=77881', { waitUntil: 'networkidle2', timeout: 60000 });
  await sleep(5000);
  await step('H', async () => {
    out.H_tabs = await page.$$eval('.pr-tab', l => l.map(e => e.innerText.replace(/\s+/g, ' ')));
    for (let i = 0; i < 25; i++) {
      const t = await tourInfo(page);
      if (!t) break;
      if (t.target === 'case-compact') { out.H_tourCompact = t; await shot(page, '42-celular-tour-especialista'); break; }
      await next(page);
    }
    await page.click('.prmake-tour__skip').catch(() => {}); await sleep(500);
    await page.evaluate(() => [...document.querySelectorAll('.pr-tab')].find(b => b.innerText.includes('Especialista'))?.click()); await sleep(1200);
    await shot(page, '43-celular-especialista');
    await page.evaluate(() => [...document.querySelectorAll('.pr-tab')].find(b => b.innerText.includes('Plano'))?.click()); await sleep(800);
    await page.evaluate(() => window.scrollTo(0, 0));
    await shot(page, '44-celular-plano');
  });
  out.H_logs = logs.slice(0, 8);
  await browser.close();
}
console.log(JSON.stringify(out, null, 1));
