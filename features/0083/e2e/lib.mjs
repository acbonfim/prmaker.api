import puppeteer from 'puppeteer-core'; import CryptoJS from 'crypto-js'; import fs from 'fs';
export const SECRET = 'as8&6ahh$#oa(23)K8t$#';
const b64 = o => Buffer.from(JSON.stringify(o)).toString('base64url');
const jwt = `${b64({ alg: 'HS256' })}.${b64({ exp: 4102444800, unique_name: 'Carla Nova' })}.x`;
const enc = v => CryptoJS.AES.encrypt(JSON.stringify(v), SECRET).toString();
export const sleep = ms => new Promise(r => setTimeout(r, ms));
export const SHOTS = '/Users/alexcarlos/repos/solvace/prform.api-0083/.t0083/e2e/shots';
export const BASE = 'http://localhost:4283';
export async function launch() {
  return puppeteer.launch({ executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', headless: 'new', args: ['--lang=pt-BR'] });
}
export async function open(browser, { width = 1440, height = 960, mobile = false } = {}) {
  const apiKey = fs.readFileSync('/Users/alexcarlos/repos/solvace/prform.api-0083/.t0083/carla.txt', 'utf8').trim();
  const page = await browser.newPage();
  await page.setViewport({ width, height, deviceScaleFactor: mobile ? 2 : 1, isMobile: mobile, hasTouch: mobile });
  const logs = [];
  page.on('console', m => { if (m.type() === 'error') logs.push(`[error] ${m.text()}`); });
  page.on('pageerror', e => logs.push(`[pageerror] ${e.message}`));
  page.on('response', r => { if (r.status() >= 400 && r.url().includes('5183')) logs.push(`[http ${r.status()}] ${r.request().method()} ${r.url()}`); });
  await page.evaluateOnNewDocument((a, k) => {
    if (!sessionStorage.getItem('seeded')) {
      localStorage.setItem('ac_847g4126110ad7fe6771c357c80331e6', a);
      localStorage.setItem('apk_av4711118840ad7fe677asd7c803a31e6', k);
      sessionStorage.setItem('seeded', '1');
    }
  }, enc({ accessToken: jwt, refreshToken: jwt, user: { name: 'Carla Nova', fullName: 'Carla Nova', externalId: '33333333-3333-3333-3333-333333333333' } }), enc(apiKey));
  return { page, logs };
}
export async function shot(page, name, el) {
  const path = `${SHOTS}/${name}.png`;
  if (el) await el.screenshot({ path }); else await page.screenshot({ path });
  console.log('shot', name);
}
export async function clickText(page, selector, text) {
  const ok = await page.evaluate((sel, t) => {
    const el = [...document.querySelectorAll(sel)].find(e => e.textContent.replace(/\s+/g, ' ').includes(t));
    if (el) { el.scrollIntoView({ block: 'center' }); el.click(); return true; }
    return false;
  }, selector, text);
  if (!ok) throw new Error(`não achei "${text}" em ${selector}`);
}
