import { open, shot, sleep } from './lib.mjs';
const [card = '77434', name = 'fa-01', w = '1440', h = '960', mobile = ''] = process.argv.slice(2);
const { browser, page, logs } = await open({ width: +w, height: +h, mobile: !!mobile });
await page.setRequestInterception(true);
const CORS = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*' };
page.on('request', (r) => {
  if (r.method() === 'OPTIONS' && /Azure\/card\/\d+\/(full|effort)/.test(r.url())) return r.respond({ status: 204, headers: CORS });
  const m = /Azure\/card\/(\d+)\/full/.exec(r.url());
  if (m) {
    const us = m[1] === '88888';
    return r.respond({ status: 200, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' }, body: JSON.stringify({
      id: +m[1], rev: 3, url: `https://dev.azure.com/solvace/x/_apis/wit/workItems/${m[1]}`,
      fields: { 'System.WorkItemType': us ? 'User Story' : 'Bug', 'System.Title': us ? 'Exportar relatório de ações em Excel' : 'Cálculo do indicador OEE errado no turno da noite',
        'System.State': 'Active', 'System.BoardColumn': 'Doing', 'System.AreaPath': 'Solvace\\Product Development\\Core',
        'System.AssignedTo': { displayName: 'Bruno Ferreira' }, 'System.CreatedBy': { displayName: 'Daniel Strinta' },
        'System.CreatedDate': '2026-11-01T10:00:00', 'System.ChangedDate': '2026-11-04T16:20:00',
        'Custom.Environment': 'gerdau-prod', 'Custom.Module': 'Indicadores', 'Microsoft.VSTS.Common.Priority': 2, 'Microsoft.VSTS.Common.Severity': '2 - High',
        'Microsoft.VSTS.Scheduling.RemainingWork': 3, 'Microsoft.VSTS.TCM.ReproSteps': '<p>1. Abrir o painel</p><p>2. Ver o OEE do turno C</p>' },
      comments: [], history: [], alerts: { missingRootCause: true, missingResolutionType: false, missingGeneralClassification: false, missingClassification: false, remainingNotZero: true } }) });
  }
  if (r.url().includes('/effort')) return r.respond({ status: 200, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' }, body: JSON.stringify({ originalEstimate: 8, completedWork: 5, remainingWork: 3 }) });
  r.continue();
});
await page.goto(`http://localhost:4200/auth/workbench?card=${card}`, { waitUntil: 'networkidle2' });
await sleep(3500);
await page.keyboard.press('Escape'); await sleep(600);
await shot(page, name);
if (process.argv.includes('--card')) {
  const b = await page.$('[data-tour="wb-card-btn"]'); await b?.click(); await sleep(900); await shot(page, name + '-card');
}
if (process.argv.includes('--plan')) {
  const b = await page.$('.wb-seg__btn:nth-child(2)'); await b?.click(); await sleep(1200); await shot(page, name + '-plan');
}
if (process.argv.includes('--timeline')) {
  const b = await page.$('[data-tour="wb-timeline-btn"]'); await b?.click(); await sleep(1500); await shot(page, name + '-timeline');
}
console.log(logs.join('\n'));
await browser.close();
