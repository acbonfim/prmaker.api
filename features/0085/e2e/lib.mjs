import puppeteer from 'puppeteer-core'; import CryptoJS from 'crypto-js'; import fs from 'fs';
export const SECRET = 'as8&6ahh$#oa(23)K8t$#';
const b64 = o => Buffer.from(JSON.stringify(o)).toString('base64url');
const jwt = `${b64({ alg: 'HS256' })}.${b64({ exp: 4102444800, unique_name: 'Bruno Ferreira' })}.x`;
const enc = v => CryptoJS.AES.encrypt(JSON.stringify(v), SECRET).toString();
export const sleep = ms => new Promise(r => setTimeout(r, ms));
export const SHOTS = '/Users/alexcarlos/repos/solvace/prform.api-0085/.t0085/shots';
const USERS = {
  bruno: { file: 'bruno.txt', name: 'Bruno Ferreira', ext: '11111111-1111-1111-1111-111111111111' },
  daniel: { file: 'daniel.txt', name: 'Daniel Strinta', ext: '22222222-2222-2222-2222-222222222222' }
};
export async function open({ width = 1440, height = 960, mobile = false, who = 'bruno' } = {}) {
  const u = USERS[who];
  const apiKey = fs.readFileSync(`/Users/alexcarlos/repos/solvace/prform.api-0085/.t0085/${u.file}`, 'utf8').trim();
  const browser = await puppeteer.launch({ executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', headless: 'new', args: ['--lang=pt-BR'] });
  const page = await browser.newPage();
  await page.setViewport({ width, height, deviceScaleFactor: mobile ? 2 : 1, isMobile: mobile, hasTouch: mobile });
  const logs = [];
  page.on('console', m => { if (['error'].includes(m.type())) logs.push(`[${m.type()}] ${m.text()}`); });
  page.on('pageerror', e => logs.push(`[pageerror] ${e.message}`));
  page.on('response', r => { if (r.status() >= 400 && r.url().includes('/api/')) logs.push(`[http ${r.status()}] ${r.request().method()} ${r.url()}`); });
  await page.evaluateOnNewDocument((a, k) => {
    localStorage.setItem('ac_847g4126110ad7fe6771c357c80331e6', a);
    localStorage.setItem('apk_av4711118840ad7fe677asd7c803a31e6', k);
    const ext = '11111111-1111-1111-1111-111111111111';
    localStorage.setItem(`prmake.setup.${ext}`, JSON.stringify({ seen: 99, menuTour: 99 }));
    for (const t of ['workbench', 'pr-screen', 'base-solvace']) localStorage.setItem(`prmake.tour.${t}.${ext}`, JSON.stringify({ version: 99, entryDone: 99 }));
  }, enc({ accessToken: jwt, refreshToken: jwt, user: { name: u.name, fullName: u.name, externalId: u.ext } }), enc(apiKey));
  return { browser, page, logs };
}
export async function shot(page, name, el) {
  const path = `${SHOTS}/${name}.png`;
  if (el) await el.screenshot({ path }); else await page.screenshot({ path });
  console.log('shot', name);
}
