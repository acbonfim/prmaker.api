import { launch, open, shot, sleep, clickText, BASE } from './lib.mjs';
const step = process.argv[2] ?? 'a';
const browser = await launch();
const fail = (m) => { console.log('FALHOU:', m); process.exitCode = 1; };
try {
  if (step === 'a') {
    const { page, logs } = await open(browser);
    await page.goto(`${BASE}/auth/dashboard`, { waitUntil: 'networkidle2' });
    // 1. Primeiro acesso: o guia abre sozinho
    const dlg = await page.waitForSelector('app-setup-guide-dialog .sg', { timeout: 15000 }).catch(() => null);
    if (!dlg) fail('guia não abriu sozinho no primeiro acesso');
    await sleep(800);
    await shot(page, '01-primeiro-acesso-guia');
    const progress = await page.$eval('.sg__progress-text', e => e.textContent.trim());
    console.log('progresso:', progress);
    const firstGuide = await page.$eval('.gd', e => e.textContent.slice(0, 120)).catch(() => '');
    console.log('guia aberto:', firstGuide);
    await shot(page, '02-integracoes-pat-azure', await page.$('app-setup-guide-dialog .sg'));
    // links do PAT montados da organização
    const links = await page.$$eval('.gd a', as => as.map(a => a.href));
    console.log('links:', links.join(' | '));
    if (!links.some(l => l.includes('dev.azure.com/solvacelabs/_usersSettings/tokens'))) fail('link do PAT sem a organização');
    // 2. GitHub: como obter
    await clickText(page, '.it', 'GitHub — token pessoal');
    await page.evaluate(() => [...document.querySelectorAll('.it')].find(e => e.textContent.includes('GitHub — token')).querySelector('.it__how').click());
    await sleep(300);
    await shot(page, '03-github-como-obter', await page.$('app-setup-guide-dialog .sg'));
    // 3. Teams (grupo do admin no texto)
    await page.evaluate(() => [...document.querySelectorAll('.it')].find(e => e.textContent.includes('Teams — URL')).querySelector('.it__how').click());
    await sleep(300);
    const teams = await page.$eval('.gd', e => e.textContent);
    if (!teams.includes('Aprovações de PR')) fail('passo a passo do Teams sem o nome do grupo');
    // provedor Gemini → Claude "Não precisa"
    const claudeChip = await page.evaluate(() => [...document.querySelectorAll('.it')].find(e => e.textContent.includes('Claude (Anthropic)'))?.querySelector('.chip')?.textContent.trim());
    console.log('chip Claude Plugin com provedor Gemini:', claudeChip);
    await shot(page, '04-teams-como-obter', await page.$('app-setup-guide-dialog .sg'));
    // 4. Preencher o Azure: abre Minhas integrações focada
    await page.evaluate(() => [...document.querySelectorAll('.it')].find(e => e.textContent.includes('Azure DevOps — Personal')).querySelector('.it__fill').click());
    await page.waitForSelector('app-my-integrations-dialog .integration--open', { timeout: 10000 });
    await sleep(600);
    const first = await page.$eval('app-my-integrations-dialog .integration', e => e.querySelector('.integration__name').textContent.trim());
    console.log('primeira em Minhas integrações:', first);
    if (first !== 'AzureDevOps Configurations') fail('Minhas integrações não abriu focada no Azure');
    await shot(page, '05-minhas-integracoes-focada', await page.$('app-my-integrations-dialog'));
    const label = await page.$eval('app-my-integrations-dialog .integration--open mat-label', e => e.textContent.trim()).catch(() => '');
    console.log('rótulo do 1º campo:', label);
    await page.type('app-my-integrations-dialog .integration--open input[type=password]', 'pat-de-teste-123');
    await clickText(page, 'app-my-integrations-dialog .integration--open button', 'Salvar');
    await sleep(1500);
    await clickText(page, 'app-my-integrations-dialog .dialog-actions button', 'Fechar');
    await sleep(1500);
    const azureChip = await page.evaluate(() => [...document.querySelectorAll('.it')].find(e => e.textContent.includes('Azure DevOps — Personal'))?.querySelector('.chip')?.textContent.trim());
    console.log('Azure depois de salvar:', azureChip);
    if (!azureChip?.includes('Configurado')) fail('status do Azure não atualizou');
    // 5. Etapas da máquina
    await clickText(page, '.st__head', 'Claude Code na sua máquina');
    await sleep(400);
    await shot(page, '06-claude-code', await page.$('app-setup-guide-dialog .sg'));
    await clickText(page, '.st__head', 'Executor — o Claude');
    await sleep(400);
    await shot(page, '07-executor-sem-maquina', await page.$('app-setup-guide-dialog .sg'));
    // 6. Fechar → balão no menu do usuário
    await clickText(page, '.sg__foot button', 'Fechar');
    const pop = await page.waitForSelector('.driver-popover', { timeout: 6000 }).catch(() => null);
    if (!pop) fail('balão do menu não apareceu ao fechar');
    await sleep(700);
    await shot(page, '08-balao-menu-usuario');
    await clickText(page, '.driver-popover button', 'Entendi');
    await sleep(500);
    // 7. Recarregar: não abre de novo; menu mostra o progresso
    await page.reload({ waitUntil: 'networkidle2' });
    await sleep(4000);
    if (await page.$('app-setup-guide-dialog')) fail('guia abriu de novo depois de visto');
    if (await page.$('.driver-popover')) fail('balão apareceu de novo');
    await page.click('[data-tour="user-menu"]');
    await sleep(600);
    await shot(page, '09-menu-primeiros-passos');
    const badge = await page.$eval('.pm-progress', e => e.textContent.trim()).catch(() => '');
    console.log('progresso no menu:', badge);
    console.log(logs.filter(l => !l.includes('2023') && !l.includes('/ws/') && !l.includes('realtime')).join('\n'));
  }
  if (step === 'b') {
    // máquina simulada: reabre pelo menu e confere o status pelo diagnóstico
    const { page, logs } = await open(browser);
    await page.goto(`${BASE}/auth/dashboard`, { waitUntil: 'networkidle2' });
    // navegador novo = primeiro acesso de novo: o guia abre sozinho (ainda falta o GitHub)
    await page.waitForSelector('app-setup-guide-dialog .sg', { timeout: 15000 });
    await sleep(1500);
    await clickText(page, '.st__head', 'Executor — o Claude');
    await sleep(400);
    const states = await page.$$eval('.st__head', hs => hs.map(h => h.querySelector('.st__title').textContent.trim() + ' → ' + (h.querySelector('.chip')?.textContent.trim() ?? '') + ' | ' + h.querySelector('.st__detail').textContent.trim()));
    console.log(states.join('\n'));
    await shot(page, '10-com-maquina-diagnostico', await page.$('app-setup-guide-dialog .sg'));
    console.log(logs.filter(l => !l.includes('2023') && !l.includes('/ws/')).join('\n'));
  }
  if (step === 'm') {
    const { page } = await open(browser, { width: 390, height: 844, mobile: true });
    await page.goto(`${BASE}/auth/dashboard`, { waitUntil: 'networkidle2' });
    await page.waitForSelector('app-setup-guide-dialog .sg', { timeout: 15000 });
    await sleep(1000);
    await shot(page, '11-celular-guia');
  }
} finally {
  await browser.close();
}
