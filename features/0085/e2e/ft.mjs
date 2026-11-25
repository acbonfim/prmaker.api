// 0085 rodada 4 (FT): Ferramentas do especialista na Bancada + tipos do item de trabalho.
// node ft.mjs [card] [prefixo] — o catálogo real ganha as ferramentas F1–F15 + revisar-pr (mock, enquanto o seed não chega)
// e os `itemTypes`; GitHub/estados/PRs do card simulados; from-template responde 409 duplicate (mostra o fluxo).
import fs from 'fs';
import { open, shot, sleep } from './lib.mjs';
const [card = '77434', prefix = 'ft'] = process.argv.slice(2);
const apiKey = fs.readFileSync('/Users/alexcarlos/repos/solvace/prform.api-0085/.t0085/bruno.txt', 'utf8').trim();
const real = await (await fetch(`http://127.0.0.1:5083/api/v1/WorkItems/catalog?card=${card}`, { headers: { 'X-API-Key': apiKey } })).json();
const T = (key, title, description, type, tasks, extra = {}) => ({ key, title, description, type, tasks, ...extra });
const repoAsk = { key: 'repository', label: 'Repositório', input: 'repository', default: 'edv-solvace-api' };
const promo = ['release-version', 'hotfix-version', 'edge'];
const TOOLS = [
  T('pr-para-branch', 'Abrir PR para uma branch', 'Branch {branch}{suffix:@target} criada a partir do destino escolhido, cherry-pick dos commits do card e PR para o destino. Destinos sugeridos: release-version, hotfix-version, edge.', 'pull-request', [
    { title: 'Criar {branch}{suffix:@target} a partir de {ask:target}', kind: 'branch', params: { repository: '{ask:repository}', branch: '{branch}{suffix:@target}', source: '{ask:target}' } },
    { title: 'Cherry-pick dos commits do card', kind: 'cherry-pick', params: { repository: '{ask:repository}', commits: 'card' } },
    { title: 'Abrir PR {branch}{suffix:@target} → {ask:target}', kind: 'pr', params: { repository: '{ask:repository}', target: '{ask:target}' } }
  ], { ask: [repoAsk, { key: 'target', label: 'Branch de destino', input: 'target-branch', options: promo }] }),
  T('prs-fluxo-producao', 'PRs do fluxo (development e qa)', 'Branches {branch}-dev e {branch}-qa criadas a partir de development e qa, cherry-pick dos commits e PRs.', 'pull-request', [
    { title: 'PR {branch}-dev → development', kind: 'pr' }, { title: 'PR {branch}-qa → qa', kind: 'pr' }]),
  T('promover-revamp', 'Promover para release-version, edge e master', 'Perfil do revamp.', 'pull-request', [], { applicable: false, reason: 'Só para repositórios revamp (este card é legado).' }),
  T('pr-hotfix-version', 'PR para hotfix-version', 'Branch -hv do destino, cherry-pick e PR.', 'pull-request', [{ title: 'PR → hotfix-version', kind: 'pr' }]),
  T('pr-release-version', 'PR para release-version', 'Branch -rv do destino, cherry-pick e PR.', 'pull-request', [{ title: 'PR → release-version', kind: 'pr' }]),
  T('resolver-conflito-promocao', 'Resolver conflito do PR para {ask:target}', 'O PR para o destino parou em conflito: branch recriada, cherry-pick, conflito resolvido e PR novo.', 'pull-request', [],
    { ask: [repoAsk, { key: 'target', label: 'Destino com conflito', input: 'target-branch', options: promo }] }),
  T('revisar-pr', 'Revisar um PR', 'O Claude revisa o PR escolhido (do card ou colado) e registra a conclusão na Timeline.', 'review', [
    { title: 'Revisar o PR', kind: 'pr-review', params: { repository: '{ask:repository}', pullRequest: '{ask:pullRequest}', focus: '{ask:focus}' } },
    { title: 'Conclusão na Timeline', kind: 'comment', params: { target: 'timeline' } }
  ], { ask: [repoAsk, { key: 'pullRequest', label: 'Pull request', input: 'pull-request' }, { key: 'focus', label: 'Foco da revisão', input: 'textarea', required: false }] }),
  T('ajuste-pos-qa', 'Ajuste após retorno de QA', 'Ajuste na {branch}, validação e o mesmo commit levado às branches dos PRs abertos — os PRs se atualizam.', 'adjustment', [{ title: 'Ajustar', kind: 'code' }]),
  T('usar-termo-existente', 'Trocar a mensagem nova por um termo existente', 'Texto novo precisa de aprovação de Produto: usar o termo do Multilingual.', 'text', [{ title: 'Achar o termo', kind: 'sql-query' }]),
  T('corrigir-sonar', 'Corrigir crítica do Sonar no PR', 'Ajustar a branch do PR para passar no quality gate.', 'adjustment', [], { applicable: false, reason: 'Só para revamp e API de integrações.' }),
  T('fechamento-code-fix', 'Fechamento: PRMake, root cause, code-fix e Dev Test in QA', 'Gera a descrição/RCA, grava o root cause, classifica code-fix, publica o resumo PT/EN e move para Dev Test in QA.', 'closing', [{ title: 'Root cause', kind: 'devops-action' }]),
  T('validar-em-qa', 'Validar em QA e mover para Ready for QA', 'Uma pessoa confere a correção no ambiente de QA; aprovada, o card vai para Ready for QA.', 'closing', [{ title: 'Validar em QA', kind: 'manual' }]),
  T('fechamento-sem-codigo', 'Fechamento sem código (orientação ao cliente)', 'Root cause, classificação, orientação ao cliente PT/EN, Remaining zerado e Test in Production.', 'closing', [{ title: 'Classificar ({ask:classification})', kind: 'devops-action' }],
    { ask: [{ key: 'classification', label: 'Classificação', input: 'select', options: ['user-education', 'configuration', 'script-user-action', 'cannot-reproduce', 'duplicated'] }] }),
  T('script-com-chamado', 'Script de correção de dados com chamado', 'Consulta antes, script com rollback, chamado e conferência depois.', 'data', [{ title: 'Consultar', kind: 'sql-query' }]),
  T('diagnostico-usuario', 'Conferir o usuário (banco + Cognito)', 'Cadastro, vínculo SSO, permissões e conta no Cognito, só leitura.', 'data', [{ title: 'Cadastro', kind: 'sql-query' }]),
  T('novo-resumo', 'Novo resumo PT/EN no card', 'Publica um novo resumo não técnico (PT/EN) mantendo os anteriores.', 'text', [{ title: 'Resumo', kind: 'devops-action' }])
];
const ITEM_TYPES = [
  ['task', 'Tarefa', 'task_alt', '#58a6ff'], ['pull-request', 'Pull request', 'merge', '#a371f7'], ['review', 'Revisão de PR', 'rate_review', '#d29922'],
  ['analysis', 'Análise', 'manage_search', '#39c5cf'], ['fix', 'Correção', 'build', '#ff7b72'], ['implementation', 'Implementação', 'construction', '#3fb950'],
  ['adjustment', 'Ajuste', 'tune', '#e3b341'], ['closing', 'Fechamento', 'flag', '#8b949e'], ['data', 'Dados/Script', 'storage', '#db61a2'],
  ['ticket', 'Chamado', 'confirmation_number', '#f0883e'], ['text', 'Textos/Resumo', 'description', '#79c0ff']
].map(([key, label, icon, color]) => ({ key, label, icon, color }));
const keys = new Set(TOOLS.map((t) => t.key));
const catalog = { ...real, defaultRepository: 'edv-solvace-api', itemTypes: ITEM_TYPES,
  templates: [...TOOLS, ...(real.templates ?? []).filter((t) => !keys.has(t.key))] };

const { browser, page, logs } = await open({ width: 1440, height: 960 });
await page.setRequestInterception(true);
const H = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*' };
const json = (r, body, status = 200) => r.respond({ status, contentType: 'application/json', headers: H, body: JSON.stringify(body) });
const BRANCHES = ['master', 'development', 'qa', 'release-version', 'hotfix-version', 'edge', 'hotfix/77434', 'hotfix/77434-dev'];
let posted = null;
page.on('request', (r) => {
  const u = r.url();
  if (r.method() === 'OPTIONS' && /(GitHub\/|Azure\/card\/|WorkItems\/catalog|from-template|PullRequest\/\d+\/github)/.test(u)) return r.respond({ status: 204, headers: H });
  if (/WorkItems\/catalog/.test(u)) return json(r, catalog);
  if (/from-template/.test(u) && r.method() === 'POST') {
    posted = r.postData();
    return json(r, { code: 'duplicate', existing: { number: 41, title: 'PR para release-version', status: 'pending' } }, 409);
  }
  if (/PullRequest\/\d+\/github/.test(u)) return json(r, [
    { id: 1, pullRequestRegisterId: 1, cardNumber: card, repositoryId: 'edv-solvace-api', branchPrefix: 'hotfix', branchName: 'hotfix/77434-dev', targetBranch: 'development', number: 812,
      url: 'https://github.com/solvace/edv-solvace-api/pull/812', title: 'fix(oee): turno da noite', description: '', status: 'OPEN', isDraft: false, statusSyncedAt: null, statusStale: false, alreadyExisted: false, userId: 'u', createdAt: '2026-11-04T10:00:00', updatedAt: null },
    { id: 2, pullRequestRegisterId: 1, cardNumber: card, repositoryId: 'edv-solvace-api', branchPrefix: 'hotfix', branchName: 'hotfix/77434-qa', targetBranch: 'qa', number: 813,
      url: 'https://github.com/solvace/edv-solvace-api/pull/813', title: 'fix(oee): turno da noite (qa)', description: '', status: 'MERGED', isDraft: false, statusSyncedAt: null, statusStale: false, alreadyExisted: false, userId: 'u', createdAt: '2026-11-04T11:00:00', updatedAt: null }]);
  if (/GitHub\/repositories/.test(u)) return json(r, [{ id: 'edv-solvace-api', label: 'edv-solvace-api', defaultBranch: 'master' }, { id: 'edv-solvace', label: 'edv-solvace', defaultBranch: 'master' }]);
  if (/GitHub\/branches/.test(u)) {
    const q = (new URL(u).searchParams.get('search') ?? '').toLowerCase();
    return json(r, BRANCHES.filter((b) => b.includes(q)).map((name) => ({ name, isDefault: name === 'master', protected: !name.includes('/') })));
  }
  if (/Azure\/card\/\d+\/full/.test(u)) return json(r, { id: +card, rev: 1, fields: { 'System.WorkItemType': 'Bug', 'System.Title': 'Cálculo do indicador OEE errado no turno da noite', 'System.State': 'Active' }, comments: [], history: [], alerts: {} });
  if (u.includes('/effort')) return json(r, { originalEstimate: 8, completedWork: 5, remainingWork: 3 });
  r.continue();
});
const clickText = (sel, text) => page.evaluate((sel, text) => {
  const el = [...document.querySelectorAll(sel)].find((e) => e.innerText.trim().includes(text)); if (el) { el.click(); return true; } return false; }, sel, text);

await page.goto(`http://localhost:4200/auth/workbench?card=${card}`, { waitUntil: 'networkidle2' });
await sleep(3500);
await page.keyboard.press('Escape'); await sleep(400);
console.log('botão:', await page.$eval('[data-tour="wb-tools"]', (e) => e.innerText.replace(/\s+/g, ' ').trim()).catch(() => 'SEM BOTÃO'));
console.log('botão lista:', await page.$eval('[data-tour="wi-tools"]', (e) => e.innerText.replace(/\s+/g, ' ').trim()).catch(() => 'SEM BOTÃO'));
await shot(page, `${prefix}-bancada`);
// 1) Painel das ferramentas sobre a coluna do especialista.
await page.click('[data-tour="wb-tools"]'); await sleep(900);
await shot(page, `${prefix}-tools`);
await page.click('.st__more').catch(() => {}); await sleep(400);
await page.evaluate(() => document.querySelector('.st__body')?.scrollTo(0, 99999)); await sleep(300);
await shot(page, `${prefix}-tools-all`, await page.$('.wb-tools'));
await page.evaluate(() => document.querySelector('.st__body')?.scrollTo(0, 0));
// Busca.
await page.type('.st__search-input', 'release'); await sleep(500);
await shot(page, `${prefix}-tools-search`, await page.$('.wb-tools'));
await page.evaluate(() => { const i = document.querySelector('.st__search-input'); i.value = ''; i.dispatchEvent(new Event('input', { bubbles: true })); }); await sleep(300);
// 2) Formulário: Abrir PR para uma branch → destino release-version.
await page.click('[data-tool="pr-para-branch"]'); await sleep(900);
const target = (await page.$$('.st__form app-wi-branch-picker input'))[0];
await target.click(); await sleep(900);
await shot(page, `${prefix}-tool-target`);
await clickText('mat-option', 'release-version'); await sleep(600);
await shot(page, `${prefix}-tool-form`);
console.log('prévia:', await page.$$eval('.st__preview li', (l) => l.map((x) => x.innerText.trim())));
// 3) Criar item → 409 duplicate (mock).
await page.click('.st__create'); await sleep(1600);
console.log('POST from-template:', posted);
console.log('dup:', await page.evaluate(() => ({ dup: !!document.querySelector('.st__dup'), top: document.querySelector('.st__form')?.scrollTop })));
await shot(page, `${prefix}-tool-dup`);
// 4) Revisar PR: seletor de PR do card.
await page.click('.st__head button'); await sleep(500);
await page.click('[data-tool="revisar-pr"]'); await sleep(900);
const prInput = await page.$('app-wi-pr-picker input');
await prInput?.click(); await sleep(900);
await shot(page, `${prefix}-revisar-pr`);
await page.keyboard.press('Escape'); await sleep(300);
await page.click('.st__head button[aria-label="Fechar as ferramentas"]').catch(() => {}); await sleep(400);
// 5) Editor "+ Novo item": chips com os tipos do item.
await page.click('[data-tour="wi-new"]'); await sleep(1200);
await shot(page, `${prefix}-editor-types`, await page.$('.mat-mdc-dialog-surface'));
await page.keyboard.press('Escape'); await sleep(500);
await browser.close();

// 6) Celular: Ferramentas em tela cheia (pelo botão da lista de itens).
const m = await open({ width: 412, height: 900, mobile: true });
await m.page.setRequestInterception(true);
m.page.on('request', (r) => {
  const u = r.url();
  if (r.method() === 'OPTIONS' && /(WorkItems\/catalog|Azure\/card\/)/.test(u)) return r.respond({ status: 204, headers: H });
  if (/WorkItems\/catalog/.test(u)) return json(r, catalog);
  if (/Azure\/card\/\d+\/full/.test(u)) return json(r, { id: +card, rev: 1, fields: { 'System.WorkItemType': 'Bug', 'System.Title': 'Cálculo do indicador OEE errado no turno da noite', 'System.State': 'Active' }, comments: [], history: [], alerts: {} });
  r.continue();
});
await m.page.goto(`http://localhost:4200/auth/workbench?card=${card}`, { waitUntil: 'networkidle2' }); await sleep(3500);
await m.page.keyboard.press('Escape'); await sleep(400);
await m.page.click('[data-tour="wi-tools"]').catch((e) => console.log('sem botão na lista (celular)', e.message)); await sleep(900);
await shot(m.page, `${prefix}-tools-celular`);
await m.browser.close();
console.log(logs.filter((l) => !/states|branches|repositories|commits|ngIf|effort|Failed to load|from-template/.test(l)).join('\n'));
