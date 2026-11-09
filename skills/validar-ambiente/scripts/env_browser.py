#!/usr/bin/env python3
"""Navegador da validacao em ambiente (feature 0092).

Um processo local por sessao (ambiente + usuario) mantem o Chrome aberto e logado entre os comandos, para o Claude
seguir o roteiro passo a passo e gravar os prints. A senha vem so do arquivo local de credenciais
(~/.claude/solvace-env-credentials.json, gravado por `prmake-skills.sh env-credentials`) e nunca e impressa.
Ambiente somente leitura (DEMO, producao): toda gravacao na rede (POST/PUT/PATCH/DELETE fora da lista de leitura) e
bloqueada enquanto a sessao existir — menos as rotas de --allow (configuracao readOnlyAllow, ex.: preferencia/filtro do
usuario, ou uma rota que a pessoa autorizou), que passam e ficam registradas.

Uso (pelo env.sh):
  env_browser.py start --env <key> --url <url> --user <email> --out <pasta> [--read-only] [--allow <trecho-da-rota>]...
                       [--headed] [--label <nome>]
  env_browser.py go|click|fill|select|press|wait|shot|snap|text|stop <sessao> [args]
  env_browser.py status
Saida: 0 ok · 1 erro · 2 sem navegador/Playwright · 3 login recusado · 4 sem senha · 5 sessao inexistente.
"""
import argparse
import hashlib
import json
import os
import re
import secrets
import signal
import subprocess
import sys
import time
import urllib.request
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from urllib.parse import urljoin, urlparse

HOME = Path.home()
ROOT = Path(os.environ.get("PRMAKE_BROWSER_DIR", HOME / ".prmake" / "browser"))
SESSIONS = ROOT / "sessions"
PROFILES = ROOT / "profiles"
CREDS = Path(os.environ.get("PRMAKE_ENV_CREDENTIALS", HOME / ".claude" / "solvace-env-credentials.json"))
CREDS_CMD = "bash ~/.claude/skills/.prmake/prmake-skills.sh env-credentials set"
IDLE_SECONDS = int(os.environ.get("PRMAKE_BROWSER_IDLE", "1800"))
VIEWPORT = {"width": 1440, "height": 900}

# Gravacoes que nao alteram dados (consultas por POST, login, telemetria) — o resto e bloqueado no somente leitura.
READ_PATH = re.compile(
    r"(search|list|filter|query|find|count|info|languages|terms|lookup|options|combo|dropdown|paged|grid|tree|"
    r"get[A-Z_/-]|/get$|login|signin|sign-in|logout|auth|token|refresh|session|environment|permission|menu|translat)",
    re.IGNORECASE)
TELEMETRY_HOST = re.compile(r"(sentry\.io|datadoghq\.com|google-analytics|googletagmanager|hotjar|clarity\.ms|cognito-idp\.)", re.IGNORECASE)
MUTATING = {"POST", "PUT", "PATCH", "DELETE"}


def die(msg, code=1):
    print(f"ERRO: {msg}", file=sys.stderr)
    sys.exit(code)


def slug(text, size=40):
    s = re.sub(r"[^a-z0-9]+", "-", (text or "").lower()).strip("-")
    return s[:size].strip("-") or "x"


def host_of(url):
    return (urlparse(url if "://" in url else f"https://{url}").hostname or "").lower()


def session_file(sid):
    return SESSIONS / f"{sid}.json"


def read_session(sid):
    f = session_file(sid)
    if not f.exists():
        die(f"sessao '{sid}' nao existe — abra com: env.sh browser start <ambiente>", 5)
    return json.loads(f.read_text())


def alive(pid):
    try:
        os.kill(int(pid), 0)
        return True
    except (OSError, ValueError, TypeError):
        return False


# ── Credenciais (so leitura; a senha nunca sai daqui) ──────────────────────────────────────────

def password_for(env, url, user):
    if not CREDS.exists():
        return None
    try:
        data = json.loads(CREDS.read_text())
    except (OSError, json.JSONDecodeError):
        die(f"{CREDS} nao e um JSON valido — corrija no seu terminal ({CREDS_CMD})")
    host = host_of(url)
    for e in data.get("environments", []):
        if (e.get("user") or "").strip().lower() != user.strip().lower():
            continue
        if (e.get("env") or "").lower() == env.lower() or (host and host_of(e.get("url") or e.get("host") or "") == host):
            return e.get("password") or None
    return None


# ── Servidor da sessao (um processo por ambiente + usuario) ──────────────────────────────────

# Elementos clicaveis e titulos da tela (roda na moldura e em cada iframe da tela).
SNAP_JS = """() => {
  const vis = e => { const r = e.getBoundingClientRect(); const s = getComputedStyle(e);
    return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none'; };
  // Campo de formulario: o rotulo dele (label[for], label do mesmo grupo) diz o que e (filtros do legado sem id).
  const label = e => { if (!/^(INPUT|SELECT|TEXTAREA)$/.test(e.tagName)) return '';
    if (e.labels && e.labels[0] && e.labels[0].innerText.trim()) return e.labels[0].innerText.trim();
    // Rotulo antes do campo no mesmo grupo (legado: <span class="font-size-sm">Lugar</span> + select2).
    for (let p = e.parentElement, i = 0; p && i < 7; p = p.parentElement, i++) {
      const x = [...p.querySelectorAll('label, span, small, h6, p')].find(l => !l.contains(e) && !l.querySelector('input, select, textarea')
        && (l.compareDocumentPosition(e) & Node.DOCUMENT_POSITION_FOLLOWING) && l.innerText.trim() && l.innerText.trim().length <= 40);
      if (x) return x.innerText.trim(); }
    return ''; };
  const name = e => (e.getAttribute('aria-label') || label(e) || e.innerText || e.value || e.placeholder || e.title || e.getAttribute('data-original-title')
    || e.getAttribute('data-bs-original-title') || e.getAttribute('data-tooltip') || e.getAttribute('mattooltip') || e.name || '').trim().replace(/\\s+/g, ' ').slice(0, 60);
  // Botao so de icone (menu de modulos, hamburguer): nome pelo icone/classe, para o Claude saber o que e.
  const icon = e => { const i = [...e.querySelectorAll('mat-icon, i, svg, img')].find(vis);
    const cls = x => (typeof x.className === 'string' ? x.className : (x.className && x.className.baseVal) || '')
      .split(/\\s+/).filter(c => c && !/ripple|mdc-|mat-mdc|focus|touch|ng-/.test(c)).join(' ');
    const t = i ? ((i.getAttribute('fonticon') || i.getAttribute('data-mat-icon-name') || i.getAttribute('svgicon')
      || i.getAttribute('data-icon') || i.getAttribute('alt') || (i.tagName === 'IMG' ? (i.getAttribute('src') || '').split('/').pop() : '')
      || (i.tagName === 'MAT-ICON' || i.tagName === 'I' ? i.textContent : '') || '').trim() || cls(i)) : '';
    const r = e.getBoundingClientRect();
    return 'icone: ' + ((t || cls(e) || '?').trim().replace(/\\s+/g, ' ').slice(0, 40)) + ` @${Math.round(r.x)},${Math.round(r.y)}`; };
  let seq = 0;
  document.querySelectorAll('[data-pm]').forEach(x => x.removeAttribute('data-pm'));  // marcas valem so ate o proximo snap
  const out = [];
  for (const e of document.querySelectorAll('button, a[href], input, select, textarea, [role=button], [role=tab], [role=menuitem], [role=link], [role=option], [role=checkbox]')) {
    if (!vis(e) || out.length >= 90) continue;
    const tag = e.tagName.toLowerCase(); const role = e.getAttribute('role') || (tag === 'a' ? 'link' : tag === 'select' ? 'combobox' : tag === 'textarea' ? 'textbox' : tag === 'input' ? (['checkbox','radio','submit','button'].includes(e.type) ? e.type : 'textbox') : tag);
    const n = name(e) || icon(e); const id = e.id && e.id !== 'undefined' && document.querySelectorAll('#' + CSS.escape(e.id)).length === 1 ? '#' + e.id : '';
    // Sem id/nome estavel: marca o elemento (data-pm) para o click/fill desta tela achar exatamente ele.
    const mark = () => { const k = 'pm' + (++seq); e.setAttribute('data-pm', k); return `[data-pm="${k}"]`; };
    const fc = e.getAttribute('formcontrolname'); const sel = id || (fc ? `[formcontrolname="${fc}"]` : (e.name ? `${tag}[name="${e.name}"]` : (e.placeholder ? `[placeholder="${e.placeholder}"]` : (name(e) && !/^(INPUT|SELECT|TEXTAREA)$/.test(e.tagName) ? `text=${n}` : mark()))));
    if (tag === 'input' && e.type === 'password') { out.push(`${role} (senha) ${sel}`); continue; }
    // Link de icone: o destino diz para onde vai (menu lateral do legado).
    const href = tag === 'a' && !name(e) ? (e.getAttribute('href') || '').replace(location.origin, '').slice(0, 80) : '';
    out.push(`${role} "${n}"${href && !href.startsWith('#') && !href.startsWith('javascript') ? ` (vai para ${href})` : ''}${sel ? '  -> ' + sel : ''}`);
  }
  // Blocos clicaveis que nao sao botao/link (tiles do menu de modulos, itens de menu do legado): cursor de mao, o mais
  // externo, com texto, fora dos ja listados.
  const seen = new Set(document.querySelectorAll('button, a[href], input, select, textarea, [role=button], [role=tab], [role=menuitem], [role=link], [role=option], [role=checkbox]'));
  const ptr = e => getComputedStyle(e).cursor === 'pointer';
  for (const e of document.querySelectorAll('div, span, li, td, mat-card, img, label')) {
    if (out.length >= 140) break;
    if (!ptr(e) || (e.parentElement && ptr(e.parentElement)) || seen.has(e) || [...seen].some(x => e.contains(x) || x.contains(e)) || !vis(e)) continue;
    const n = (e.innerText || e.getAttribute('title') || e.getAttribute('alt') || '').trim().replace(/\\s+/g, ' ').slice(0, 60);
    if (!n) continue;
    seen.add(e);
    out.push(`item "${n}"  -> ${e.id && document.querySelectorAll('#' + CSS.escape(e.id)).length === 1 ? '#' + e.id : (() => { const k = 'pm' + (++seq); e.setAttribute('data-pm', k); return `[data-pm="${k}"]`; })()}`);
  }
  const heads = [...document.querySelectorAll('h1,h2,h3,[role=heading]')].filter(vis).map(h => h.innerText.trim()).filter(Boolean).slice(0, 12);
  return { heads, out };
}"""


class Session:
    def __init__(self, meta):
        self.meta = meta
        self.read_only = bool(meta.get("readOnly"))
        self.allow = [a.strip().lower() for a in meta.get("allow") or [] if a and a.strip()]
        self.blocked = []
        self.allowed = []
        self.out = Path(meta["out"])
        self.out.mkdir(parents=True, exist_ok=True)
        self.last = time.time()

    # Guarda do somente leitura: ligada para a sessao inteira (inclusive o que a tela dispara sozinha).
    def route(self, route):
        req = route.request
        if self.read_only and self.guard_on and req.method in MUTATING:
            u = urlparse(req.url)
            if not TELEMETRY_HOST.search(u.hostname or "") and not READ_PATH.search(u.path):
                if any(a in u.path.lower() for a in self.allow):
                    self.allowed.append(f"{req.method} {u.hostname}{u.path}")
                    return route.continue_()
                self.blocked.append(f"{req.method} {u.hostname}{u.path}")
                return route.abort("blockedbyclient")
        return route.continue_()

    def launch(self, pw):
        from playwright.sync_api import Error as PwError
        profile = PROFILES / self.meta["sid"]
        profile.mkdir(parents=True, exist_ok=True)
        for lock in ("SingletonLock", "SingletonSocket", "SingletonCookie"):
            try:
                (profile / lock).unlink()
            except OSError:
                pass
        opts = dict(headless=not self.meta.get("headed"), viewport=VIEWPORT, locale="pt-BR", accept_downloads=False)
        exe = os.environ.get("PRMAKE_CHROME")
        tries = [dict(executable_path=exe)] if exe else []
        tries += [dict(channel="chrome"), dict(channel="msedge"), {}]
        last = None
        for extra in tries:
            try:
                self.ctx = pw.chromium.launch_persistent_context(str(profile), **opts, **extra)
                break
            except PwError as e:
                last = e
        else:
            raise RuntimeError(f"nenhum navegador: instale o Google Chrome (ou defina PRMAKE_CHROME) — {last}")
        self.guard_on = False
        self.ctx.route("**/*", self.route)
        self.page = self.ctx.pages[0] if self.ctx.pages else self.ctx.new_page()

    def settle(self, ms=600):
        try:
            self.page.wait_for_load_state("networkidle", timeout=8000)
        except Exception:
            pass
        self.page.wait_for_timeout(ms)

    def on_sign_in(self):
        return "sign-in" in self.page.url or "signin" in self.page.url or "/login" in self.page.url

    def login(self):
        url, user = self.meta["url"], self.meta["user"]
        self.page.goto(url, wait_until="domcontentloaded", timeout=60000)
        self.settle(1500)
        if not self.on_sign_in() and not self.page.locator("input[type=password]").count():
            return "ja logado (sessao guardada no perfil)"
        pwd = password_for(self.meta["env"], url, user)
        if not pwd:
            raise PermissionError("sem-senha")
        user_box = self.page.locator("#username-field, input[name=username], input[type=email], input[autocomplete=username]").first
        pass_box = self.page.locator("#password-field, input[type=password]").first
        user_box.fill(user)
        pass_box.fill(pwd)
        pwd = None
        btn = self.page.locator("#signin-button, button[type=submit]").first
        btn.click()
        alerts = self.page.locator(".svc-alert-message, [role=alert], mat-error, .mat-mdc-snack-bar-label, .toast-message")
        deadline = time.time() + 40
        while time.time() < deadline:
            self.page.wait_for_timeout(700)
            if not self.on_sign_in() and not self.page.locator("input[type=password]").count():
                self.settle(1500)
                return "login ok"
            alert = self.visible_text(alerts)
            if alert:
                raise ValueError(f"login recusado: {alert[:160]}")
        raise ValueError("login recusado (continuou na tela de login)")

    @staticmethod
    def visible_text(locator):
        try:
            for i in range(min(locator.count(), 5)):
                el = locator.nth(i)
                if el.is_visible():
                    text = el.inner_text(timeout=1000).strip()
                    if text:
                        return text
        except Exception:
            pass
        return ""

    # Comandos ---------------------------------------------------------------------------------
    def frames(self):
        """Iframes visiveis com pagina (o legado abre cada tela no iframe #framepag do systems/application/default.asp)."""
        out = []
        for f in self.page.main_frame.child_frames:
            try:
                if not f.url or f.url == "about:blank":
                    continue
                el = f.frame_element()
                if not el.is_visible():
                    continue
                fid, fname = el.get_attribute("id"), el.get_attribute("name")
            except Exception:  # noqa: BLE001
                continue
            if fid or fname:
                out.append((f"#{fid}" if fid else f'iframe[name="{fname}"]', f))
        return out

    @staticmethod
    def locate_in(scope, t):
        if re.match(r"^(text|role|css|xpath|id|data-testid|placeholder|label)=", t) or t.startswith(("//", "#", ".", "[")):
            return scope.locator(t).first
        if re.match(r"^[a-z]+[\w-]*(\[|\.|#|:|$)", t) and " " not in t:
            return scope.locator(t).first
        return scope.get_by_text(t, exact=False).first

    def locate(self, target):
        t = target.strip()
        m = re.match(r"^frame=(.+?)\s*>>\s*(.+)$", t)
        if m:
            frame = next((f for sel, f in self.frames() if sel == m.group(1).strip()), None)
            if frame is None:
                raise ValueError(f"iframe {m.group(1).strip()} nao esta na tela (rode snap)")
            return self.locate_in(frame, m.group(2).strip())
        loc = self.locate_in(self.page, t)
        try:
            if not loc.count():
                # Tela legada: o alvo esta dentro do iframe da tela, nao na moldura.
                for _, frame in self.frames():
                    alt = self.locate_in(frame, t)
                    if alt.count():
                        return alt
        except Exception:  # noqa: BLE001
            pass
        return loc

    def run(self, cmd, args):
        self.last = time.time()
        self.blocked.clear()
        self.allowed.clear()
        p = self.page
        if cmd == "go":
            dest = args["url"]
            if not re.match(r"^https?://", dest):
                dest = urljoin(self.meta["url"], dest.lstrip("/"))
            p.goto(dest, wait_until="domcontentloaded", timeout=60000)
            self.settle(1200)
        elif cmd == "click":
            self.locate(args["target"]).click(timeout=15000)
            self.settle()
        elif cmd == "fill":
            self.locate(args["target"]).fill(args["value"], timeout=15000)
            self.settle(300)
        elif cmd == "select":
            self.locate(args["target"]).select_option(label=args["value"], timeout=15000)
            self.settle(300)
        elif cmd == "press":
            p.keyboard.press(args["key"])
            self.settle()
        elif cmd == "wait":
            w = args["target"]
            if re.fullmatch(r"\d+", w):
                p.wait_for_timeout(min(int(w), 60000))
            else:
                self.locate(w).wait_for(state="visible", timeout=30000)
        elif cmd == "shot":
            return self.shot(args.get("name") or "print", args.get("caption"), bool(args.get("full")))
        elif cmd == "snap":
            return self.snap()
        elif cmd == "text":
            text = p.locator("body").inner_text(timeout=10000)
            for sel, frame in self.frames():
                try:
                    text += f"\n\n[iframe {sel}: {frame.url}]\n" + frame.locator("body").inner_text(timeout=10000)
                except Exception:  # noqa: BLE001
                    pass
            return {"text": text[:20000]}
        else:
            raise ValueError(f"comando desconhecido: {cmd}")
        return {}

    def shot(self, name, caption, full):
        index_file = self.out / "shots.json"
        shots = json.loads(index_file.read_text()) if index_file.exists() else []
        n = len(shots) + 1
        path = self.out / f"{n:02d}-{slug(name)}.jpg"
        data = self.page.screenshot(type="jpeg", quality=82, full_page=full)
        digest = hashlib.sha256(data).hexdigest()
        same = next((x for x in shots if x.get("sha256") == digest), None)
        if same:
            # Tela igual a um print ja tirado: nao vira evidencia de novo (navegacao/retentativa).
            return {"file": str(self.out / same["file"]), "number": shots.index(same) + 1, "duplicate": True}
        path.write_bytes(data)
        shots.append({"file": path.name, "caption": caption or name, "url": self.page.url, "title": self.page.title(),
                      "at": time.strftime("%Y-%m-%d %H:%M:%S"), "sha256": digest})
        index_file.write_text(json.dumps(shots, ensure_ascii=False, indent=2))
        return {"file": str(path), "number": n}

    def snap(self):
        path = self.out / "_snap.jpg"
        self.page.screenshot(path=str(path), type="jpeg", quality=70)
        items = self.page.evaluate(SNAP_JS)
        frames = []
        for sel, frame in self.frames():
            try:
                fi = frame.evaluate(SNAP_JS)
            except Exception:  # noqa: BLE001
                continue
            items["heads"] += fi["heads"]
            items["out"] += [re.sub(r"  -> (.+)$", lambda m: f"  -> frame={sel} >> {m.group(1)}", e) for e in fi["out"]]
            try:
                ftext = frame.locator("body").inner_text(timeout=10000)
            except Exception:  # noqa: BLE001
                ftext = ""
            frames.append(f"{sel} -> {frame.url}")
            items.setdefault("ftext", []).append(f"[iframe {sel}]\n{ftext}")
        text = self.page.locator("body").inner_text(timeout=10000)
        if items.get("ftext"):
            text = "\n\n".join(items["ftext"]) + "\n\n[moldura]\n" + text
        out = {"screenshot": str(path), "headings": items["heads"], "elements": items["out"],
               "text": re.sub(r"\n{3,}", "\n\n", text)[:3500]}
        if frames:
            out["frames"] = frames
        return out


def write_meta(meta):
    SESSIONS.mkdir(parents=True, exist_ok=True)
    tmp = session_file(meta["sid"]).with_suffix(".tmp")
    tmp.write_text(json.dumps(meta, ensure_ascii=False, indent=2))
    os.chmod(tmp, 0o600)
    tmp.replace(session_file(meta["sid"]))


def serve(sid):
    meta = json.loads(session_file(sid).read_text())
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        meta.update(status="error", code=2, message="Playwright ausente no venv da skill (rode env.sh setup)")
        write_meta(meta)
        return
    s = Session(meta)
    with sync_playwright() as pw:
        try:
            s.launch(pw)
            result = s.login()
        except PermissionError:
            meta.update(status="error", code=4, message="sem-senha")
            write_meta(meta)
            return
        except ValueError as e:
            meta.update(status="error", code=3, message=str(e))
            write_meta(meta)
            return
        except Exception as e:  # noqa: BLE001
            meta.update(status="error", code=2 if "navegador" in str(e) else 1, message=str(e)[:400])
            write_meta(meta)
            return
        s.guard_on = True

        token = secrets.token_hex(16)

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def do_POST(self):
                if self.headers.get("x-token") != token:
                    self.send_response(403)
                    self.end_headers()
                    return
                body = json.loads(self.rfile.read(int(self.headers.get("content-length", "0"))) or b"{}")
                cmd = body.get("cmd")
                if cmd == "stop":
                    out = {"ok": True, "stopped": True}
                    s.stop = True
                else:
                    try:
                        data = s.run(cmd, body.get("args") or {})
                        out = {"ok": True, **data}
                    except Exception as e:  # noqa: BLE001
                        out = {"ok": False, "error": str(e).splitlines()[0][:400]}
                    out.update(url=s.page.url, title=s.page.title() if not s.page.is_closed() else "",
                               blocked=list(s.blocked), allowed=list(s.allowed))
                raw = json.dumps(out, ensure_ascii=False).encode()
                self.send_response(200)
                self.send_header("content-type", "application/json")
                self.send_header("content-length", str(len(raw)))
                self.end_headers()
                self.wfile.write(raw)

        server = HTTPServer(("127.0.0.1", 0), Handler)
        server.timeout = 30
        s.stop = False
        meta.update(status="ready", pid=os.getpid(), port=server.server_address[1], token=token, message=result,
                    landed=s.page.url)
        write_meta(meta)
        while not s.stop and time.time() - s.last < IDLE_SECONDS:
            server.handle_request()
        server.server_close()
        s.ctx.close()
    meta.update(status="stopped", pid=None, port=None, token=None)
    write_meta(meta)


# ── Cliente ──────────────────────────────────────────────────────────────────────────────────

def call(sid, cmd, args=None, timeout=120):
    meta = read_session(sid)
    if meta.get("status") != "ready" or not alive(meta.get("pid")):
        die(f"sessao '{sid}' nao esta aberta — abra de novo: env.sh browser start {meta.get('env', '<ambiente>')} --user {meta.get('user', '<usuario>')}", 5)
    req = urllib.request.Request(f"http://127.0.0.1:{meta['port']}/", data=json.dumps({"cmd": cmd, "args": args or {}}).encode(),
                                 headers={"content-type": "application/json", "x-token": meta["token"]})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read())


def report(res):
    for b in res.get("blocked") or []:
        print(f"BLOQUEADO (ambiente somente leitura): {b} — so com autorizacao da pessoa para esta rota: reabra com "
              f"env.sh browser start <ambiente> ... --allow '{urlparse('https://' + b.split(' ', 1)[-1]).path.rsplit('/', 1)[-1]}'")
    for a in res.get("allowed") or []:
        print(f"LIBERADO (rota autorizada no somente leitura): {a}")
    if not res.get("ok", True):
        print(f"FALHOU: {res.get('error')}")
    for k in ("file", "screenshot"):
        if res.get(k):
            print(f"{'print' if k == 'file' else 'tela'}: {res[k]}" + (f" (#{res['number']})" if k == "file" else "")
                  + (" — IGUAL a um print ja tirado: nao gravei outro (use o mesmo no relatorio)" if k == "file" and res.get("duplicate") else ""))
    if res.get("frames"):
        print("iframes da tela (tela legada: os alvos dentro dele vem com 'frame=<iframe> >> '): " + " | ".join(res["frames"]))
    if res.get("headings"):
        print("titulos: " + " | ".join(res["headings"]))
    if res.get("elements"):
        print("elementos (alvo para click/fill):")
        for e in res["elements"]:
            print(f"  {e}")
    if res.get("text"):
        print("--- texto da tela ---")
        print(res["text"])
    if res.get("url"):
        print(f"pagina: {res.get('title', '')} — {res['url']}")
    return 0 if res.get("ok", True) else 1


def start(a):
    sid = slug(f"{a.env}-{a.user}", 80)
    f = session_file(sid)
    if f.exists():
        meta = json.loads(f.read_text())
        if meta.get("status") == "ready" and alive(meta.get("pid")):
            if sorted(meta.get("allow") or []) == sorted(a.allow or []):
                print(f"sessao {sid} ja aberta ({meta['url']} como {meta['user']}) — pagina: {meta.get('landed', '')}")
                return 0
            # Rotas liberadas mudaram: a guarda e montada ao abrir — reabre a sessao (o login fica no perfil).
            stop(sid)
    if not password_for(a.env, a.url, a.user):
        print(f"SEM SENHA para {a.user} em {a.env} ({host_of(a.url)}).")
        print(f"A pessoa grava no PROPRIO terminal (fora do chat): {CREDS_CMD} {a.env} {a.user}")
        return 4
    meta = {"sid": sid, "env": a.env, "label": a.label or a.env, "url": a.url, "user": a.user, "readOnly": a.read_only,
            "allow": sorted(set(a.allow or [])), "headed": a.headed, "out": str(Path(a.out).expanduser().resolve()), "status": "starting",
            "started": time.strftime("%Y-%m-%d %H:%M:%S")}
    write_meta(meta)
    log = open(ROOT / f"{sid}.log", "ab")
    kw = {"start_new_session": True} if os.name != "nt" else {"creationflags": 0x00000200 | 0x00000008}
    subprocess.Popen([sys.executable, os.path.abspath(__file__), "_serve", sid], stdout=log, stderr=log, stdin=subprocess.DEVNULL, **kw)
    deadline = time.time() + 150
    while time.time() < deadline:
        time.sleep(1)
        meta = json.loads(f.read_text())
        if meta.get("status") == "ready":
            print(f"sessao {sid} aberta: {meta['label']} ({meta['url']}) como {meta['user']} — {meta['message']}")
            print(f"pagina: {meta.get('landed')}")
            print(f"prints em: {meta['out']}")
            if a.read_only:
                print("SOMENTE LEITURA: gravacoes na rede ficam bloqueadas nesta sessao (so navegar e consultar)"
                      + (f" — menos as rotas liberadas: {', '.join(meta.get('allow') or [])}." if meta.get("allow") else "."))
            return 0
        if meta.get("status") == "error":
            code = int(meta.get("code") or 1)
            if code == 4:
                print(f"SEM SENHA para {a.user} em {a.env}. A pessoa grava no PROPRIO terminal: {CREDS_CMD} {a.env} {a.user}")
            elif code == 3:
                print(f"LOGIN RECUSADO para {a.user} em {a.env}: {meta.get('message')}. Senha errada ou expirada? "
                      f"A pessoa regrava no PROPRIO terminal: {CREDS_CMD} {a.env} {a.user}")
            else:
                print(f"ERRO ao abrir o navegador: {meta.get('message')} (log: {ROOT / (sid + '.log')})")
            return code
    print(f"ERRO: o navegador nao respondeu em 150 s (log: {ROOT / (sid + '.log')})")
    return 1


def stop(sid):
    meta = read_session(sid)
    if meta.get("status") == "ready" and alive(meta.get("pid")):
        try:
            call(sid, "stop", timeout=20)
        except Exception:  # noqa: BLE001
            try:
                os.kill(int(meta["pid"]), signal.SIGTERM)
            except OSError:
                pass
    meta.update(status="stopped", pid=None, port=None, token=None)
    write_meta(meta)
    print(f"sessao {sid} encerrada")
    return 0


def status():
    if not SESSIONS.exists():
        print("nenhuma sessao")
        return 0
    for f in sorted(SESSIONS.glob("*.json")):
        m = json.loads(f.read_text())
        state = m.get("status") if m.get("status") != "ready" or alive(m.get("pid")) else "caiu"
        print(f"{m['sid']}\t{state}\t{m.get('url')}\t{m.get('user')}{'  (somente leitura)' if m.get('readOnly') else ''}")
    return 0


def main():
    argv = sys.argv[1:]
    if not argv:
        print(__doc__)
        return 1
    cmd = argv[0]
    if cmd == "_serve":
        serve(argv[1])
        return 0
    if cmd == "status":
        return status()
    if cmd == "start":
        ap = argparse.ArgumentParser(prog="env_browser.py start")
        ap.add_argument("--env", required=True)
        ap.add_argument("--url", required=True)
        ap.add_argument("--user", required=True)
        ap.add_argument("--out", required=True)
        ap.add_argument("--label")
        ap.add_argument("--read-only", action="store_true")
        ap.add_argument("--headed", action="store_true")
        ap.add_argument("--allow", action="append", default=[])
        return start(ap.parse_args(argv[1:]))
    if len(argv) < 2:
        die(f"uso: env_browser.py {cmd} <sessao> ...")
    sid, rest = argv[1], argv[2:]
    if cmd == "stop":
        return stop(sid)
    need = {"go": ["url"], "click": ["target"], "fill": ["target", "value"], "select": ["target", "value"],
            "press": ["key"], "wait": ["target"], "shot": ["name"], "snap": [], "text": []}
    if cmd not in need:
        die(f"comando desconhecido: {cmd}")
    flags = {"full": "--full" in rest}
    rest = [x for x in rest if x != "--full"]
    caption = None
    if "--caption" in rest:
        i = rest.index("--caption")
        caption = rest[i + 1] if i + 1 < len(rest) else None
        rest = rest[:i] + rest[i + 2:]
    if len(rest) < len(need[cmd]):
        die(f"uso: env_browser.py {cmd} <sessao> {' '.join('<' + n + '>' for n in need[cmd])}")
    args = dict(zip(need[cmd], rest))
    if cmd == "shot":
        args.update(flags, caption=caption)
    return report(call(sid, cmd, args))


if __name__ == "__main__":
    sys.exit(main())
