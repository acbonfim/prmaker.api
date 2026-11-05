// 0085 rodada 3 (FD): editor de item com os controles dos parâmetros (repositório/branch/destino/estado/commits/select).
// node fd.mjs [card] [prefixo]   — mocks: GitHub/repositories|branches|commits e Azure/card/{id}/states (integrações locais vazias).
import { open, shot, sleep } from './lib.mjs';
const [card = '77434', prefix = 'fd'] = process.argv.slice(2);
const { browser, page, logs } = await open({ width: 1440, height: 1000 });
await page.setRequestInterception(true);
const H = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*' };
const json = (r, body) => r.respond({ status: 200, contentType: 'application/json', headers: H, body: JSON.stringify(body) });
const BRANCHES = ['master', 'development', 'qa', 'release-version', 'hotfix-version', 'edge', 'hotfix/77434', 'hotfix/77434-dev', 'hotfix/77100', 'feature/oee-turnos'];
page.on('request', (r) => {
  const u = r.url();
  if (r.method() === 'OPTIONS' && /(GitHub\/|Azure\/card\/)/.test(u)) return r.respond({ status: 204, headers: H });
  if (/GitHub\/repositories/.test(u)) return json(r, [
    { id: 'edv-solvace-api', label: 'edv-solvace-api', defaultBranch: 'master' },
    { id: 'edv-solvace', label: 'edv-solvace', defaultBranch: 'master' },
    { id: 'solvace-indicators-web', label: 'solvace-indicators-web', defaultBranch: 'development' }]);
  if (/GitHub\/branches/.test(u)) {
    const q = (new URL(u).searchParams.get('search') ?? '').toLowerCase();
    return json(r, BRANCHES.filter((b) => b.includes(q)).map((name) => ({ name, isDefault: name === 'master', protected: !name.includes('/') })));
  }
  if (/GitHub\/commits/.test(u)) return json(r, [
    { sha: 'a1b2c3d4e5f6', title: 'fix(oee): turno da noite atravessa a meia-noite', author: 'Bruno Ferreira', date: '2026-11-04' },
    { sha: 'b2c3d4e5f6a1', title: 'test(oee): caso do turno C', author: 'Bruno Ferreira', date: '2026-11-04' },
    { sha: 'c3d4e5f6a1b2', title: 'chore: ajusta log do cálculo', author: 'Daniel Strinta', date: '2026-11-03' }]);
  if (/Azure\/card\/\d+\/states/.test(u)) return json(r, { type: 'Bug', current: 'Active', states: [
    { name: 'New', category: 'Proposed' }, { name: 'Active', category: 'InProgress' }, { name: 'Dev Test in QA', category: 'InProgress', suggested: true },
    { name: 'Ready for QA', category: 'InProgress', suggested: true }, { name: 'Test in Production', category: 'Resolved', suggested: true },
    { name: 'Resolved', category: 'Resolved' }, { name: 'Closed', category: 'Completed' }] });
  if (/Azure\/card\/\d+\/full/.test(u)) return json(r, { id: +card, rev: 1, fields: { 'System.WorkItemType': 'Bug', 'System.Title': 'OEE errado no turno da noite', 'System.State': 'Active' }, comments: [], history: [], alerts: {} });
  if (u.includes('/effort')) return json(r, { originalEstimate: 8, completedWork: 5, remainingWork: 3 });
  r.continue();
});
await page.goto(`http://localhost:4200/auth/workbench?card=${card}`, { waitUntil: 'networkidle2' });
await sleep(3500);
await page.keyboard.press('Escape'); await sleep(500);
await (await page.$('[data-tour="wi-new"]'))?.click();
await sleep(1500);
const dlg = async () => page.$('.mat-mdc-dialog-surface');
const pickTemplate = async (key) => {
  const v = await page.$$eval('.wie select.wi-select option', (os, k) => os.find((o) => o.value.endsWith(k))?.value, key);
  if (v) await page.select('.wie select.wi-select', v);
  await sleep(1500);
};
// 1) Modelo PR → release-version: repositório/branch/destino com os controles.
await pickTemplate('pr-release-version');
await shot(page, `${prefix}-template-rv`, await dlg());
// 2) Destino do PR (3ª sub-tarefa): abre o autocomplete com sugestões das regras + GitHub.
const targets = await page.$$('app-wi-branch-picker input');
if (targets.length) { await targets[targets.length - 1].click(); await sleep(1200); await shot(page, `${prefix}-target-open`); }
await page.keyboard.press('Escape'); await sleep(300);
// 3) Branch de trabalho: digita e vê sugestões (hotfix/77434-rv etc.).
const firstBranch = (await page.$$('app-wi-branch-picker input'))[0];
if (firstBranch) { await firstBranch.click(); await firstBranch.evaluate((el) => el.select()); await firstBranch.type('77434'); await sleep(1100); await shot(page, `${prefix}-branch-search`); }
await page.keyboard.press('Escape'); await sleep(300);
// 4) Repositório.
const repo = (await page.$$('app-wi-repo-picker input'))[0];
if (repo) { await repo.click(); await sleep(700); await shot(page, `${prefix}-repo-open`); }
await page.keyboard.press('Escape'); await sleep(300);
// 5) Cherry-pick: escolher commits.
const radios = await page.$$('app-wi-commits-picker input[type=radio]');
if (radios[1]) { await radios[1].click(); await sleep(1200); const box = await page.$('app-wi-commits-picker .wip-commit input'); await box?.click(); await sleep(300); }
await shot(page, `${prefix}-commits`, await dlg());
// 6) Mover o card: estados sugeridos.
await pickTemplate('mover-card');
const st = await page.$('app-wi-state-picker input');
if (st) { await st.click(); await sleep(1200); await shot(page, `${prefix}-state-open`); }
await page.keyboard.press('Escape'); await sleep(300);
// 7) Abrir chamado (kind ticket com selects) + o select de tipo com ícones.
await pickTemplate('abrir-chamado');
await shot(page, `${prefix}-ticket`, await dlg());
await (await page.$('.wie__kind'))?.click(); await sleep(800);
await shot(page, `${prefix}-kinds`);
console.log(logs.filter((l) => !/states|branches|repositories|commits/.test(l)).join('\n'));
await browser.close();
