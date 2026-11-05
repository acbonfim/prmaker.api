// 0085 (ajustes FC): aba Card da Bancada. node fc.mjs <card> <nome> <largura> <altura> [celular] [--confirm]
import { open, shot, sleep } from './lib.mjs';
const [card = '77434', name = 'fc-card', w = '1440', h = '960', mobile = ''] = process.argv.slice(2);
const { browser, page, logs } = await open({ width: +w, height: +h, mobile: mobile === 'celular' });
await page.setRequestInterception(true);
const CORS = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*' };
page.on('request', (r) => {
  if (r.method() === 'OPTIONS' && /Azure\/card\/\d+\/(full|effort|actions)/.test(r.url())) return r.respond({ status: 204, headers: CORS });
  const m = /Azure\/card\/(\d+)\/full/.exec(r.url());
  if (m) {
    const us = m[1] === '88888';
    return r.respond({ status: 200, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' }, body: JSON.stringify({
      id: +m[1], rev: 3, url: `https://dev.azure.com/solvace/x/_apis/wit/workItems/${m[1]}`,
      fields: { 'System.WorkItemType': us ? 'User Story' : 'Bug', 'System.Title': us ? 'Exportar relatório de ações em Excel' : 'Cálculo do indicador OEE errado no turno da noite',
        'System.State': 'Active', 'System.BoardColumn': 'Doing', 'System.Reason': 'Approved', 'System.AreaPath': 'Solvace\\Product Development\\Core',
        'System.IterationPath': 'Solvace\\2026\\Sprint 22', 'System.Tags': us ? 'Excel; Relatórios' : 'OEE; Turnos; Gerdau',
        'System.AssignedTo': { displayName: 'Bruno Ferreira' }, 'System.CreatedBy': { displayName: 'Daniel Strinta' }, 'System.ChangedBy': { displayName: 'Bruno Ferreira' },
        'System.CreatedDate': '2026-11-01T10:00:00', 'System.ChangedDate': '2026-11-04T16:20:00',
        'System.AttachedFileCount': 2, 'System.HyperLinkCount': 0, 'System.ExternalLinkCount': 3, 'System.RelatedLinkCount': 1,
        'Custom.Environment': 'gerdau-prod', 'Custom.Module': 'Indicadores', 'Microsoft.VSTS.Common.Priority': 2,
        ...(us ? { 'Microsoft.VSTS.Scheduling.StoryPoints': 5,
          'System.Description': '<p>Como <b>gestor</b>, quero exportar o relatório de ações em Excel para analisar fora do sistema.</p>',
          'Microsoft.VSTS.Common.AcceptanceCriteria': '<ul><li>Botão "Exportar Excel" na lista de ações</li><li>Respeita os filtros aplicados</li><li>Colunas: código, título, responsável, prazo, status</li></ul>' }
          : { 'Microsoft.VSTS.Common.Severity': '2 - High', 'Microsoft.VSTS.Scheduling.RemainingWork': 3,
          'Microsoft.VSTS.TCM.ReproSteps': '<p>1. Abrir o painel de indicadores da planta</p><p>2. Selecionar o turno C (22h–6h)</p><p>3. Ver o OEE: aparece 140% (o esperado é ~72%)</p>' }) },
      comments: [], history: [], alerts: { missingRootCause: true, missingResolutionType: false, missingGeneralClassification: false, missingClassification: false, remainingNotZero: true } }) });
  }
  if (r.url().includes('/effort')) {
    const us = r.url().includes('/88888/');
    return r.respond({ status: 200, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' },
      body: JSON.stringify(us ? { originalEstimate: 16, completedWork: 10, remainingWork: 8 } : { originalEstimate: 8, completedWork: 5, remainingWork: 3 }) });
  }
  r.continue();
});
await page.goto(`http://localhost:4200/auth/workbench?card=${card}&focus=card`, { waitUntil: 'networkidle2' });
await sleep(3500);
await page.keyboard.press('Escape'); await sleep(400);
if (!(await page.$('app-workbench-card'))) { await (await page.$('[data-tour="wb-card-btn"]'))?.click(); await sleep(900); }
if (process.argv.includes('--confirm')) {
  const btns = await page.$$('app-workbench-card .wc-log__quick .wc-btn'); await btns[1]?.click(); await sleep(500);
}
await shot(page, name);
console.log(logs.join('\n'));
await browser.close();
