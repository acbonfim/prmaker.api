// 0085 rodada 5 (F5a): aba Card nova, modal do card, plano com ⋮/arquivos e a tela de PR (terminologia US).
// node f5.mjs card <card> <nome> [w] [h]  |  node f5.mjs modal <card> <nome>  |  node f5.mjs plan <card> <nome>  |  node f5.mjs register <card> <nome>
import { open, shot, sleep } from './lib.mjs';
const [mode = 'card', card = '77434', name = 'f5-card', w = '1440', h = '960'] = process.argv.slice(2).filter((a) => !a.startsWith('--'));
const { browser, page, logs } = await open({ width: +w, height: +h });
await page.setRequestInterception(true);
const CORS = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*' };
const US_CARDS = new Set(['88501', '88888']);
page.on('request', (r) => {
  if (r.method() === 'OPTIONS' && /Azure\/card\/\d+\/(full|effort|actions)/.test(r.url())) return r.respond({ status: 204, headers: CORS });
  const m = /Azure\/card\/(\d+)\/full/.exec(r.url());
  if (m) {
    const us = US_CARDS.has(m[1]);
    return r.respond({ status: 200, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' }, body: JSON.stringify({
      id: +m[1], rev: 3, url: `https://dev.azure.com/solvace/x/_apis/wit/workItems/${m[1]}`,
      fields: { 'System.WorkItemType': us ? 'User Story' : 'Bug', 'System.Title': us ? 'Exportar relatório de ações em Excel' : 'View vw_wcm_site_user duplicando linha em produção',
        'System.State': us ? 'Planning (done)' : 'Active', 'System.BoardColumn': us ? 'Ready for fix' : 'Doing', 'System.Reason': 'Approved',
        'System.AreaPath': 'Solvace\\Product Development Team', 'System.IterationPath': 'Solvace\\Solvace Product Improvement',
        'System.Tags': us ? 'Excel; Relatórios' : 'WCM; MDLZ',
        'System.AssignedTo': { displayName: 'Bruno Ferreira' }, 'System.CreatedBy': { displayName: 'Liza Peixoto' }, 'System.ChangedBy': { displayName: 'Marcus Barbosa' },
        'System.CreatedDate': '2026-11-05T11:00:00', 'System.ChangedDate': '2026-11-05T11:15:00',
        'System.AttachedFileCount': us ? 0 : 2, 'System.HyperLinkCount': 0, 'System.ExternalLinkCount': us ? 0 : 3, 'System.RelatedLinkCount': 1,
        'Custom.Environment': 'mdlz-prod', 'Custom.Module': 'All', 'Microsoft.VSTS.Common.Priority': 2,
        ...(us ? { 'Microsoft.VSTS.Scheduling.StoryPoints': 12,
          'System.Description': '<p>Como <b>gestor</b>, quero exportar o relatório de ações em Excel para analisar fora do sistema.</p><p>Hoje o relatório só sai em PDF e os gestores copiam linha a linha para planilhas. A exportação deve respeitar os filtros da tela e o idioma do usuário.</p>',
          'Microsoft.VSTS.Common.AcceptanceCriteria': '<ul><li>Botão "Exportar Excel" na lista de ações</li><li>Respeita os filtros aplicados</li><li>Colunas: código, título, responsável, prazo, status</li><li>Datas no formato do idioma do usuário</li></ul>' }
          : { 'Microsoft.VSTS.Common.Severity': '2 - High', 'Microsoft.VSTS.Scheduling.RemainingWork': 3,
          'Microsoft.VSTS.TCM.ReproSteps': '<p>A view vw_wcm_site_user está duplicando linha em produção.</p><p>Também achei a causa: <b>ele tem 4 linhas em <code>DB_MDLZ_PRD_GLOBAL.dbo.TB_WCM_USER_PICTURE</code></b>. A <code>VW_WCM_SITE_USER</code> faz <code>LEFT JOIN</code> nessa tabela pelo <code>USER_ID</code>, então cada foto vira uma linha do usuário na view.</p><ul><li>A <code>TB_WCM_USER_PICTURE</code> não tem PK (está como heap na análise do ACP), por isso nada impede várias fotos por usuário.</li><li>Ele é o único usuário do GLOBAL com foto repetida.</li><li>O problema aparece em qualquer banco LOCAL da MDLZ que use a view.</li></ul><p>Para corrigir na origem, uma destas:</p><ol><li>Apagar as 3 fotos repetidas (manter a mais recente) e criar a PK.</li><li>Trocar o LEFT JOIN por OUTER APPLY TOP 1.</li></ol>' }) },
      comments: [], history: [], alerts: { missingRootCause: true, missingResolutionType: false, missingGeneralClassification: false, missingClassification: false, remainingNotZero: true } }) });
  }
  // --artifacts: arquivos da skill no plano em tela (faixa "Arquivos do plano").
  if (process.argv.includes('--artifacts') && r.method() === 'GET' && /ExecutionPlan\/[0-9a-f-]{36}(\?.*)?$/.test(r.url())) {
    fetch(r.url(), { headers: r.headers() }).then((res) => res.json()).then((plan) => {
      const mk = (n, kind, name, description) => ({ id: `fake-${n}`, planId: plan.id, number: n, noteId: null, stepKey: null, name, kind,
        contentType: 'text/markdown', size: 1200, sha256: `sha-${n}`, description, createdBy: 'Claude', createdAt: plan.createdAt });
      plan.artifacts = [mk(1, 'analysis', 'analise-inicial.md', 'Análise da US'), mk(2, 'script', 'export-acoes.sql', 'Consulta de apoio'),
        mk(3, 'ticket', 'chamado-permissao-relatorio.md', 'Chamado para liberar o relatório'), mk(4, 'data', 'amostra-acoes.csv', null)];
      r.respond({ status: 200, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' }, body: JSON.stringify(plan) });
    }).catch(() => r.continue());
    return;
  }
  if (r.url().includes('/effort')) {
    const us = [...US_CARDS].some((c) => r.url().includes(`/${c}/`));
    return r.respond({ status: 200, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' },
      body: JSON.stringify(us ? { originalEstimate: 12, completedWork: 0, remainingWork: 12 } : { originalEstimate: 8, completedWork: 5, remainingWork: 3 }) });
  }
  r.continue();
});

if (mode === 'register') {
  await page.goto(`http://localhost:4200/auth/register?card=${card}`, { waitUntil: 'networkidle2' });
  await sleep(4500);
  await page.keyboard.press('Escape'); await sleep(600);
  await shot(page, name);
  const info = await page.evaluate(() => ({
    kicker: document.querySelector('app-execution-plan .plan__meta-kicker')?.textContent?.trim() ?? null,
    hasMoreBtnPause: !!document.querySelector('app-execution-plan .plan__header [data-tour="plan-control"]'),
    filesBar: !!document.querySelector('app-execution-plan .pfiles'),
    doc: Array.from(document.querySelectorAll('app-execution-plan button')).map((b) => b.textContent?.trim()).filter((t) => /completo|completa/.test(t ?? ''))
  }));
  console.log(JSON.stringify(info));
} else if (mode === 'plan') {
  await page.goto(`http://localhost:4200/auth/workbench?card=${card}&focus=plan`, { waitUntil: 'networkidle2' });
  await sleep(4500);
  await page.keyboard.press('Escape'); await sleep(600);
  await shot(page, name);
  await (await page.$('app-execution-plan .plan__more'))?.click(); await sleep(700);
  await shot(page, name + '-menu');
  const info = await page.evaluate(() => ({
    kicker: document.querySelector('app-execution-plan .plan__meta-kicker')?.textContent?.trim() ?? null,
    headerControls: document.querySelectorAll('app-execution-plan .plan__header [data-tour="plan-control"]').length,
    menu: Array.from(document.querySelectorAll('.mat-mdc-menu-panel [mat-menu-item], .mat-mdc-menu-panel .mat-mdc-menu-item')).map((b) => b.textContent?.trim()),
    filesBar: document.querySelector('app-execution-plan .pfiles')?.textContent?.replace(/\s+/g, ' ').trim() ?? null,
    starter: document.querySelector('.pr-info__author--starter')?.textContent?.replace(/\s+/g, ' ').trim() ?? null
  }));
  console.log(JSON.stringify(info));
} else if (mode === 'modal') {
  await page.goto(`http://localhost:4200/auth/workbench?card=${card}`, { waitUntil: 'networkidle2' });
  await sleep(4000);
  await page.keyboard.press('Escape'); await sleep(600);
  await (await page.$('[data-tour="wb-card-btn"]'))?.click();
  await sleep(110);
  await shot(page, name + '-anim');
  await sleep(700);
  await shot(page, name);
  await page.keyboard.press('Escape'); await sleep(140);
  await shot(page, name + '-closing');
  await sleep(600);
  console.log(JSON.stringify({ open: !!(await page.$('app-workbench-card-dialog')) }));
} else {
  await page.goto(`http://localhost:4200/auth/workbench?card=${card}&focus=card`, { waitUntil: 'networkidle2' });
  await sleep(3500);
  await page.keyboard.press('Escape'); await sleep(400);
  await shot(page, name);
  const pane = await page.$('[data-tour="wb-card-pane"]');
  const box = await pane?.boundingBox();
  console.log(JSON.stringify({ pane: box }));
}
console.log(logs.join('\n'));
await browser.close();
