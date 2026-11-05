// 0085 rodada 3 (FE): aba Execução da Bancada (plano por item) e a tela de PR intacta.
// node fe.mjs plan <card> <nome> [--item=<n>] [--menu]   |   node fe.mjs register <card> <nome>
import { open, shot, sleep } from './lib.mjs';
const [mode = 'plan', card = '77434', name = 'fe-plan', w = '1440', h = '960'] = process.argv.slice(2).filter((a) => !a.startsWith('--'));
const item = (process.argv.find((a) => a.startsWith('--item=')) ?? '').slice(7);
const { browser, page, logs } = await open({ width: +w, height: +h });
await page.setRequestInterception(true);
const CORS = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*' };
page.on('request', (r) => {
  if (r.method() === 'OPTIONS' && /Azure\/card\/\d+\/(full|effort)/.test(r.url())) return r.respond({ status: 204, headers: CORS });
  const m = /Azure\/card\/(\d+)\/full/.exec(r.url());
  if (m) {
    return r.respond({ status: 200, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' }, body: JSON.stringify({
      id: +m[1], rev: 3, url: `https://dev.azure.com/solvace/x/_apis/wit/workItems/${m[1]}`,
      fields: { 'System.WorkItemType': 'Bug', 'System.Title': 'Cálculo do indicador OEE errado no turno da noite',
        'System.State': 'Active', 'System.BoardColumn': 'Doing', 'System.AreaPath': 'Solvace\\Product Development\\Core',
        'System.AssignedTo': { displayName: 'Bruno Ferreira' }, 'System.CreatedBy': { displayName: 'Daniel Strinta' },
        'System.CreatedDate': '2026-11-01T10:00:00', 'System.ChangedDate': '2026-11-04T16:20:00',
        'Custom.Environment': 'gerdau-prod', 'Custom.Module': 'Indicadores', 'Microsoft.VSTS.Common.Priority': 2, 'Microsoft.VSTS.Common.Severity': '2 - High',
        'Microsoft.VSTS.Scheduling.RemainingWork': 3, 'Microsoft.VSTS.TCM.ReproSteps': '<p>1. Abrir o painel</p><p>2. Ver o OEE do turno C</p>' },
      comments: [], history: [], alerts: { missingRootCause: true, missingResolutionType: false, missingGeneralClassification: false, missingClassification: false, remainingNotZero: true } }) });
  }
  // --fake: um item da análise (#30, concluído) ligado ao plano de análise do card — para o seletor e o aviso de item fechado.
  if (process.argv.includes('--fake') && r.method() === 'GET' && /WorkItems\/card\/\d+$/.test(r.url())) {
    const headers = r.headers();
    fetch(r.url(), { headers }).then((res) => res.json()).then(async (items) => {
      const plans = await (await fetch(r.url().replace(/WorkItems\/card\/(\d+)$/, 'ExecutionPlan/card/$1'), { headers })).json();
      const analysis = plans.find((p) => p.phase === 'analysis');
      if (analysis) items.push({ ...items[items.length - 1], id: 'fake-30', number: 30, title: `Análise inicial do bug #${card}`, status: 'completed',
        origin: 'analysis', planId: analysis.id, planPhase: 'analysis', createdAt: analysis.createdAt, tasks: [], requestId: null, activity: null });
      r.respond({ status: 200, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' }, body: JSON.stringify(items) });
    }).catch(() => r.continue());
    return;
  }
  // --fakeq: um pedido do Claude rodando no card (popover do robô).
  if (process.argv.includes('--fakeq') && r.method() === 'GET' && /ExecutionQueue\/card\/\d+$/.test(r.url())) {
    fetch(r.url(), { headers: r.headers() }).then((res) => res.json()).then((q) => {
      const now = Date.now();
      q.active = { id: 'fake-req', status: 'running', kind: 'resume', cardNumber: card, workerName: 'MacBook-Bruno', source: 'screen',
        requestedBy: 'Bruno Ferreira', attempts: 1, maxAttempts: 3, createdAt: new Date(now - 300000).toISOString(),
        claimedAt: new Date(now - 240000).toISOString(), startedAt: new Date(now - 240000).toISOString(),
        currentActivity: { label: 'Lendo src/app/indicadores/oee.service.ts', tool: 'Read', at: new Date(now - 8000).toISOString() },
        recentActivities: [
          { label: 'Buscando "turnoNoite" no código', tool: 'Grep', at: new Date(now - 60000).toISOString() },
          { label: 'prmake_base_search OEE', tool: 'mcp__prmake__prmake_base_search', at: new Date(now - 120000).toISOString() }] };
      r.respond({ status: 200, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' }, body: JSON.stringify(q) });
    }).catch(() => r.continue());
    return;
  }
  if (r.url().includes('/effort')) return r.respond({ status: 200, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' }, body: JSON.stringify({ originalEstimate: 8, completedWork: 5, remainingWork: 3 }) });
  r.continue();
});
if (mode === 'register') {
  await page.goto(`http://localhost:4200/auth/register?card=${card}`, { waitUntil: 'networkidle2' });
  await sleep(4500);
  await page.keyboard.press('Escape'); await sleep(600);
  await shot(page, name);
} else {
  const q = item ? `&item=${item}&focus=plan` : '&focus=plan';
  await page.goto(`http://localhost:4200/auth/workbench?card=${card}${q}`, { waitUntil: 'networkidle2' });
  await sleep(4000);
  await page.keyboard.press('Escape'); await sleep(600);
  await shot(page, name);
  if (process.argv.includes('--menu')) {
    await (await page.$('.wb-exec__pick'))?.click(); await sleep(700);
    await shot(page, name + '-menu');
    await page.keyboard.press('Escape'); await sleep(400);
  }
  if (process.argv.includes('--robot')) {
    await (await page.$('.wb-robot'))?.click(); await sleep(800);
    await shot(page, name + '-robot');
    await page.keyboard.press('Escape'); await sleep(300);
  }
  const info = await page.evaluate(() => ({
    pick: document.querySelector('.wb-exec__pick')?.textContent?.replace(/\s+/g, ' ').trim() ?? null,
    closed: !!document.querySelector('.wb-closed'),
    vbarVer: !!document.querySelector('app-execution-plan .vbar__ver'),
    headline: document.querySelector('app-execution-plan .headline, app-execution-plan [class*="head"] p')?.textContent?.trim()?.slice(0, 160) ?? null
  }));
  console.log(JSON.stringify(info));
}
console.log(logs.join('\n'));
await browser.close();
