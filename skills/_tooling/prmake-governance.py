#!/usr/bin/env python3
"""Governança do PRMake (feature 0098): hook único do Claude Code.

Servido pela API (GET /api/v1/Skills/tool/governance) e instalado pelo prmake-skills.sh em
~/.claude/skills/.prmake/prmake-governance.py; registrado nos hooks SessionStart, UserPromptSubmit, PreToolUse,
PostToolUse e Stop do ~/.claude/settings.json (e no settings do executor, 1.7.0 — com --from-executor).

O que faz (a configuração vem do PRMake: GET Governance/hooks/config, com cache em ~/.prmake/governance.json):
- PreToolUse: hook "evaluate" ativo e a ferramenta casa com as regras (toolMatchers) -> POST Governance/evaluate;
  block -> nega a ferramenta (JSON hookSpecificOutput.permissionDecision = "deny") com o motivo; warn -> deixa passar e
  devolve o aviso ao Claude (additionalContext) e à pessoa (systemMessage); allow -> nada. Ferramentas mcp__prmake__*
  não passam aqui (o servidor já aplica as regras no MCP).
- PostToolUse/SessionStart/UserPromptSubmit/Stop: hook "record" ativo -> o evento vai para a fila
  ~/.prmake/governance-queue.jsonl e segue em lote para POST Governance/hooks/events (30 eventos, 60 s, Stop/SessionStart).
  Nunca manda o texto do pedido (só o tamanho); do Bash manda o comando cortado.
- Padrões de banco e de aplicação (0101), só com o hook "evaluate" ativo para a ferramenta: PostToolUse de
  Write/Edit/MultiEdit em .sql ou migração (pasta migrations/Migrations; sem Designer/ModelSnapshot) -> lê o arquivo
  (até 200 KB), POST Governance/standards/check (target sql|migration, repositório pelo remote origin, card pela branch)
  e devolve até 10 achados ao Claude (additionalContext). PreToolUse de Write de .sql: block -> nega com os achados.
  PRMake fora ou sem o recurso: deixa passar sempre.
- Rede fora/tempo esgotado: respeita failOpen (padrão: deixa passar) e nunca trava a sessão. Erros vão para
  ~/.prmake/governance.log (curto, rotacionado). Só a biblioteca padrão do Python 3; funciona no Windows/Git Bash.

Uso:
  prmake-governance.py [--from-executor]   hook (JSON do Claude Code no stdin; sempre sai com 0)
  prmake-governance.py status              configuração em cache, versão e fila
  prmake-governance.py test "<comando>"    avalia um comando do Bash nas regras do PRMake (não executa nada)
  prmake-governance.py refresh             relê a configuração no PRMake agora
  prmake-governance.py flush               envia a fila de eventos agora

Token: PRMAKE_TOKEN ou ~/.claude/prmake-token.txt (o mesmo das skills). API: PRMAKE_API_BASE ou a do instalador.
"""
import datetime
import hashlib
import json
import os
import re
import socket
import sys
import time

# urllib/tempfile só quando precisa (rede/duração): o hook roda a cada ferramenta e a importação custa caro.

TOOL_VERSION = "__PRMAKE_TOOL_VERSION__"
API_BASE_INSTALLED = "__PRMAKE_API_BASE__"
DEFAULT_API_BASE = "https://api.softhouse.app.br/api/v1"

DEFAULT_CONFIG = {"mode": "monitor", "failOpen": True, "timeoutMs": 1500, "cacheSeconds": 60, "hooks": [],
                  "toolMatchers": [], "version": ""}
OFFLINE_BACKOFF_S = 30       # depois de uma falha de rede, não tenta de novo por 30 s (não atrasa cada ferramenta)
FLUSH_EVERY = 30             # eventos na fila
FLUSH_AGE_S = 60             # idade do evento mais antigo da fila
BATCH_MAX = 200              # por requisição (limite do servidor)
BATCHES_PER_FLUSH = 5
QUEUE_MAX = 5000             # sem rede por muito tempo: descarta os mais antigos
INPUT_MAX = 4000             # entrada da ferramenta no evaluate
RECORD_INPUT_MAX = 500       # entrada da ferramenta no registro
LOG_MAX_BYTES = 200_000
STARTS_TTL_S = 6 * 3600
CARD_BRANCH = re.compile(r"(?:^|/)(?:hotfix|bugfix|feature)/(\d+)", re.IGNORECASE)
RECORD_EVENTS = ("SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "Stop", "SubagentStop")
# 0101: padrões de banco e de aplicação (POST Governance/standards/check) em .sql e migrações.
STANDARDS_TOOLS = ("Write", "Edit", "MultiEdit")
STANDARDS_MAX_BYTES = 200 * 1024      # lido do arquivo/enviado
STANDARDS_MAX_FINDINGS = 10           # devolvidos ao Claude
STANDARDS_MISSING_S = 600             # PRMake sem o recurso (404): não pergunta de novo por 10 min
MIGRATION_EXTS = (".cs", ".ts", ".js", ".py", ".java", ".sql")


# --------------------------------------------------------------------------------------------------- caminhos e base
def home():
    return os.path.expanduser("~")


def prmake_home():
    return os.environ.get("PRMAKE_HOME") or os.path.join(home(), ".prmake")


def claude_home():
    return os.environ.get("CLAUDE_CONFIG_DIR") or os.path.join(home(), ".claude")


def cache_file():
    return os.path.join(prmake_home(), "governance.json")


def queue_file():
    return os.path.join(prmake_home(), "governance-queue.jsonl")


def log_file():
    return os.path.join(prmake_home(), "governance.log")


def starts_dir():
    import tempfile
    user = re.sub(r"[^A-Za-z0-9_.-]", "_", os.environ.get("USER") or os.environ.get("USERNAME") or "u")
    return os.path.join(tempfile.gettempdir(), "prmake-governance-" + user)


def api_base():
    env = (os.environ.get("PRMAKE_API_BASE") or "").strip()
    if env:
        return env.rstrip("/")
    if API_BASE_INSTALLED and not API_BASE_INSTALLED.startswith("__"):
        return API_BASE_INSTALLED.rstrip("/")
    return DEFAULT_API_BASE


def token():
    t = (os.environ.get("PRMAKE_TOKEN") or "").strip()
    if t:
        return t
    candidates = [os.path.join(claude_home(), "prmake-token.txt"), os.path.join(home(), ".claude", "prmake-token.txt")]
    if os.environ.get("CLAUDE_PROJECT_DIR"):
        candidates.append(os.path.join(os.environ["CLAUDE_PROJECT_DIR"], ".claude", "prmake-token.txt"))
    for f in candidates:
        try:
            with open(f, encoding="utf-8") as fh:
                t = fh.read().strip()
            if t:
                return t
        except OSError:
            continue
    return None


def machine():
    try:
        return socket.gethostname().split(".")[0]
    except OSError:
        return None


def source():
    """Origem do evento: especialista do caso, executor do PRMake ou o terminal da pessoa (hook)."""
    if os.environ.get("PRMAKE_SPECIALIST") == "1":
        return "specialist"
    if os.environ.get("PRMAKE_EXECUTOR") == "1":
        return "executor"
    return "hook"


def now_iso(ts=None):
    dt = datetime.datetime.fromtimestamp(ts if ts is not None else time.time()).astimezone()
    return dt.isoformat(timespec="milliseconds")


def cut(text, limit):
    if text is None:
        return None
    text = str(text)
    return text if len(text) <= limit else text[: max(0, limit - 1)] + "…"


# ------------------------------------------------------------------------------------------------------------ log
def log(msg):
    try:
        os.makedirs(prmake_home(), exist_ok=True)
        path = log_file()
        try:
            if os.path.getsize(path) > LOG_MAX_BYTES:
                os.replace(path, path + ".1")
        except OSError:
            pass
        with open(path, "a", encoding="utf-8") as fh:
            fh.write("%s %s\n" % (now_iso(), cut(str(msg).replace("\n", " "), 400)))
    except OSError:
        pass


# ------------------------------------------------------------------------------------------------------ casamentos
def glob_match(glob, value):
    """Glob do PRMake (igual ao GovernanceRuleEngine.Glob): sem diferenciar maiúsculas, '*' = qualquer sequência,
    '?' = um caractere, '|' separa alternativas; vazio casa tudo."""
    if not glob:
        return True
    value = value or ""
    for part in str(glob).split("|"):
        rx = "^" + re.escape(part.strip()).replace(r"\*", ".*").replace(r"\?", ".") + "$"
        if re.match(rx, value, re.IGNORECASE | re.DOTALL):
            return True
    return False


def matches_any(globs, value):
    return any(glob_match(g, value) for g in (globs or []) if g is not None)


def is_abs(path):
    return os.path.isabs(path) or bool(re.match(r"^[A-Za-z]:[\\/]", path))


def norm_path(path):
    """Caminho como o servidor compara (barras normais; C:\\x\\y -> C:/x/y)."""
    return path.replace("\\", "/") if path else path


def git_dir_of(start):
    """(pasta .git, raiz do repositório) lendo o disco (sem subprocesso): pasta .git ou arquivo .git de worktree
    (gitdir: ...). Fora de um repositório: (None, None)."""
    if not start:
        return None, None
    d = os.path.abspath(start)
    for _ in range(40):
        dot = os.path.join(d, ".git")
        if os.path.isdir(dot):
            return dot, d
        if os.path.isfile(dot):
            try:
                with open(dot, encoding="utf-8") as fh:
                    m = re.match(r"\s*gitdir:\s*(.+?)\s*$", fh.read())
            except OSError:
                m = None
            if m:
                gd = m.group(1)
                return (gd if is_abs(gd) else os.path.normpath(os.path.join(d, gd))), d
        parent = os.path.dirname(d)
        if parent == d:
            return None, None
        d = parent
    return None, None


def branch_of(cwd):
    """Branch atual lendo o .git direto (sem subprocesso): pasta .git ou arquivo .git de worktree (gitdir: ...)."""
    gd, _ = git_dir_of(cwd)
    if not gd:
        return None
    try:
        with open(os.path.join(gd, "HEAD"), encoding="utf-8") as fh:
            return branch_from_head(fh.read())
    except OSError:
        return None


def repo_name_from_url(url):
    """Nome do repositório pela URL do remote: https://github.com/org/prform.api.git -> prform.api;
    https://dev.azure.com/org/proj/_git/edv-solvace-api -> edv-solvace-api; git@host:org/repo.git -> repo."""
    url = (url or "").strip().rstrip("/")
    if not url:
        return None
    name = re.split(r"[/:]", url)[-1]
    if name.lower().endswith(".git"):
        name = name[:-4]
    return name or None


def origin_repo(start):
    """Nome do repositório pelo remote origin (config do .git; worktree usa o commondir). None se não der."""
    gd, _ = git_dir_of(start)
    if not gd:
        return None
    common = gd
    try:
        with open(os.path.join(gd, "commondir"), encoding="utf-8") as fh:
            c = fh.read().strip()
        if c:
            common = c if is_abs(c) else os.path.normpath(os.path.join(gd, c))
    except OSError:
        pass
    try:
        with open(os.path.join(common, "config"), encoding="utf-8", errors="replace") as fh:
            config = fh.read()
    except OSError:
        return None
    section = None
    for line in config.splitlines():
        s = line.strip()
        if s.startswith("["):
            m = re.match(r'\[\s*remote\s+"([^"]+)"\s*\]', s)
            section = m.group(1) if m else None
            continue
        if section == "origin":
            m = re.match(r"url\s*=\s*(.+)$", s)
            if m:
                return repo_name_from_url(m.group(1))
    return None


def branch_from_head(content):
    m = re.match(r"\s*ref:\s*refs/heads/(\S+)", content or "")
    return m.group(1) if m else None


def card_from_branch(branch):
    m = CARD_BRANCH.search(branch or "")
    return int(m.group(1)) if m else None


def card_number(cwd):
    env = (os.environ.get("PRMAKE_CARD") or "").strip().lstrip("#")
    if env.isdigit():
        return int(env)
    return card_from_branch(branch_of(cwd))


def _shrink(value, limit=300):
    if isinstance(value, str):
        return value if len(value) <= limit else value[:limit] + "…(+%d)" % (len(value) - limit)
    if isinstance(value, dict):
        return {k: _shrink(v, limit) for k, v in value.items()}
    if isinstance(value, list):
        return [_shrink(v, limit) for v in value[:20]]
    return value


def tool_input_text(tool, tool_input, limit):
    """Bash: o comando; demais ferramentas: JSON resumido do tool_input (textos longos cortados)."""
    if not isinstance(tool_input, dict):
        return cut(tool_input, limit) if tool_input else None
    if tool == "Bash" and "command" in tool_input:
        return cut(tool_input.get("command") or "", limit)
    try:
        return cut(json.dumps(_shrink(tool_input), ensure_ascii=False, sort_keys=True), limit)
    except (TypeError, ValueError):
        return None


def tool_path(tool_input):
    if not isinstance(tool_input, dict):
        return None
    p = tool_input.get("file_path") or tool_input.get("notebook_path")
    return norm_path(p) if isinstance(p, str) and p else None


# ------------------------------------------------------------------------------------------------------------ HTTP
class Offline(Exception):
    def __init__(self, msg, code=None):
        super().__init__(msg)
        self.code = code   # HTTP de erro (None = rede/tempo)


def http(method, path, body=None, timeout_ms=1500):
    import urllib.error
    import urllib.request
    tk = token()
    if not tk:
        raise Offline("sem token do PRMake (PRMAKE_TOKEN ou ~/.claude/prmake-token.txt)")
    url = api_base() + "/" + path.lstrip("/")
    data = json.dumps(body, ensure_ascii=False).encode("utf-8") if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("x-api-key", tk)
    req.add_header("accept", "application/json")
    if data is not None:
        req.add_header("content-type", "application/json; charset=utf-8")
    try:
        with urllib.request.urlopen(req, timeout=max(0.2, timeout_ms / 1000.0)) as resp:
            raw = resp.read()
            status = resp.status
    except urllib.error.HTTPError as e:
        raise Offline("HTTP %s em %s %s" % (e.code, method, path), e.code)
    except (urllib.error.URLError, OSError, ValueError) as e:
        raise Offline("%s em %s %s" % (getattr(e, "reason", e), method, path))
    if not raw:
        return status, None
    try:
        return status, json.loads(raw.decode("utf-8"))
    except ValueError:
        return status, None


# ----------------------------------------------------------------------------------------------------------- cache
def read_json(path, default=None):
    try:
        with open(path, encoding="utf-8") as fh:
            return json.load(fh)
    except (OSError, ValueError):
        return default


def write_json(path, data):
    try:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        tmp = "%s.%d.tmp" % (path, os.getpid())
        with open(tmp, "w", encoding="utf-8") as fh:
            json.dump(data, fh, ensure_ascii=False)
        os.replace(tmp, path)
    except OSError as e:
        log("não gravei %s: %s" % (path, e))


def offline_recently(cache):
    return time.time() - float((cache or {}).get("failedAt") or 0) < OFFLINE_BACKOFF_S


def mark_offline(reason):
    cache = read_json(cache_file(), {}) or {}
    cache["failedAt"] = time.time()
    cache["lastError"] = cut(reason, 300)
    write_json(cache_file(), cache)
    log(reason)


def get_config(force=False):
    """Configuração dos hooks (cache local de cacheSeconds; sem rede usa a última; nunca vista: None)."""
    cache = read_json(cache_file(), {}) or {}
    cfg = cache.get("config") if isinstance(cache.get("config"), dict) else None
    ttl = 60
    if cfg:
        try:
            ttl = min(3600, max(5, int(cfg.get("cacheSeconds") or 60)))
        except (TypeError, ValueError):
            ttl = 60
    if not force and cfg and time.time() - float(cache.get("fetchedAt") or 0) < ttl:
        return cfg
    if not force and offline_recently(cache):
        return cfg
    timeout = int((cfg or DEFAULT_CONFIG).get("timeoutMs") or 1500)
    try:
        status, data = http("GET", "Governance/hooks/config", timeout_ms=timeout)
    except Offline as e:
        mark_offline("config: %s" % e)
        return cfg
    if status != 200 or not isinstance(data, dict):
        mark_offline("config: resposta inesperada (HTTP %s)" % status)
        return cfg
    merged = dict(DEFAULT_CONFIG)
    merged.update({k: v for k, v in data.items() if v is not None})
    if cfg and str(cfg.get("version")) != str(merged.get("version")):
        log("config nova: versão %s (antes %s), modo %s" % (merged.get("version"), cfg.get("version"), merged.get("mode")))
    write_json(cache_file(), {"fetchedAt": time.time(), "config": merged})
    return merged


def hooks_for(cfg, event, action=None, tool=None):
    out = []
    for h in (cfg or {}).get("hooks") or []:
        if not isinstance(h, dict) or h.get("enabled") is False:
            continue
        if str(h.get("event") or "").lower() != ("claude:" + event).lower():
            continue
        if action and str(h.get("action") or "").lower() != action:
            continue
        if tool is not None and not glob_match(h.get("matcher") or "", tool):
            continue
        out.append(h)
    return out


# ------------------------------------------------------------------------------------------------- início/duração
def _start_path(key):
    return os.path.join(starts_dir(), hashlib.sha1(key.encode("utf-8")).hexdigest()[:24] + ".json")


def save_start(key, ts=None):
    try:
        os.makedirs(starts_dir(), exist_ok=True)
        with open(_start_path(key), "w", encoding="utf-8") as fh:
            json.dump({"t": ts if ts is not None else time.time()}, fh)
    except OSError:
        pass


def pop_start(key):
    path = _start_path(key)
    data = read_json(path)
    try:
        os.remove(path)
    except OSError:
        pass
    try:
        return float(data["t"]) if data else None
    except (KeyError, TypeError, ValueError):
        return None


def clean_starts():
    """De vez em quando: apaga inícios esquecidos (ferramenta barrada ou sessão interrompida)."""
    try:
        limit = time.time() - STARTS_TTL_S
        for name in os.listdir(starts_dir()):
            p = os.path.join(starts_dir(), name)
            if os.path.getmtime(p) < limit:
                os.remove(p)
    except OSError:
        pass


def use_key(data):
    tid = data.get("tool_use_id")
    if tid:
        return "tool:" + str(tid)
    raw = json.dumps(data.get("tool_input"), sort_keys=True, ensure_ascii=False, default=str)
    return "tool:%s:%s:%s" % (data.get("session_id"), data.get("tool_name"), hashlib.sha1(raw.encode("utf-8")).hexdigest())


# ---------------------------------------------------------------------------------------------------- fila e envio
def enqueue(evt):
    try:
        os.makedirs(prmake_home(), exist_ok=True)
        line = json.dumps(dict(evt, _t=time.time()), ensure_ascii=False)
        with open(queue_file(), "a", encoding="utf-8") as fh:
            fh.write(line + "\n")
    except OSError as e:
        log("fila: %s" % e)


def _read_lines(path):
    try:
        with open(path, encoding="utf-8") as fh:
            return [ln for ln in fh.read().splitlines() if ln.strip()]
    except OSError:
        return []


def should_flush():
    lines = _read_lines(queue_file())
    if not lines:
        return False
    if len(lines) >= FLUSH_EVERY:
        return True
    try:
        return time.time() - float(json.loads(lines[0]).get("_t") or 0) >= FLUSH_AGE_S
    except (ValueError, AttributeError):
        return True


def _lock():
    path = os.path.join(prmake_home(), ".governance-flush.lock")
    for _ in range(2):
        try:
            os.makedirs(prmake_home(), exist_ok=True)
            os.mkdir(path)
            return path
        except FileExistsError:
            try:
                if time.time() - os.path.getmtime(path) > 30:  # trava de um envio que morreu
                    os.rmdir(path)
                    continue
            except OSError:
                pass
            return None
        except OSError:
            return None
    return None


def flush(cfg, force=False):
    """Envia a fila em lotes (≤ 200). Falha: o que não foi fica para a próxima; nunca lança."""
    cache = read_json(cache_file(), {}) or {}
    if not force and offline_recently(cache):
        return 0
    lock = _lock()
    if not lock:
        return 0
    sent = 0
    sending = queue_file() + ".sending"
    try:
        lines = _read_lines(sending)
        if os.path.exists(queue_file()):
            taking = "%s.%d.taking" % (queue_file(), os.getpid())
            try:
                os.replace(queue_file(), taking)
                lines += _read_lines(taking)
                os.remove(taking)
            except OSError:
                pass
        if len(lines) > QUEUE_MAX:
            log("fila passou de %d eventos: descartei os %d mais antigos" % (QUEUE_MAX, len(lines) - QUEUE_MAX))
            lines = lines[-QUEUE_MAX:]
        events = []
        for ln in lines:
            try:
                e = json.loads(ln)
                e.pop("_t", None)
                events.append(e)
            except ValueError:
                continue
        timeout = int((cfg or DEFAULT_CONFIG).get("timeoutMs") or 1500)
        batches = 0
        while events and batches < BATCHES_PER_FLUSH:
            batch = events[:BATCH_MAX]
            try:
                status, _ = http("POST", "Governance/hooks/events", batch, timeout_ms=max(timeout, 3000))
            except Offline as e:
                mark_offline("eventos: %s" % e)
                break
            if status >= 300:
                mark_offline("eventos: HTTP %s" % status)
                break
            events = events[len(batch):]
            sent += len(batch)
            batches += 1
        if events:
            write_lines = [json.dumps(dict(e, _t=time.time()), ensure_ascii=False) for e in events]
            tmp = sending + ".tmp"
            with open(tmp, "w", encoding="utf-8") as fh:
                fh.write("\n".join(write_lines) + "\n")
            os.replace(tmp, sending)
        elif os.path.exists(sending):
            os.remove(sending)
    except OSError as e:
        log("envio da fila: %s" % e)
    finally:
        try:
            os.rmdir(lock)
        except OSError:
            pass
    return sent


def pending_count():
    return len(_read_lines(queue_file())) + len(_read_lines(queue_file() + ".sending"))


# --------------------------------------------------------------------------------------------------------- avaliação
def evaluate_payload(event, tool, tool_input, cwd, session_id):
    payload = {
        "source": source(),
        "event": event,
        "tool": tool,
        "input": tool_input_text(tool, tool_input, INPUT_MAX),
        "path": tool_path(tool_input),
        "cardNumber": card_number(cwd),
        "sessionId": session_id,
        "machine": machine(),
        "cwd": norm_path(cwd),
    }
    return {k: v for k, v in payload.items() if v not in (None, "")}


def evaluate(cfg, payload):
    """Decisão do PRMake; sem resposta: None (quem chama aplica o failOpen)."""
    cache = read_json(cache_file(), {}) or {}
    if offline_recently(cache):
        return None
    try:
        status, data = http("POST", "Governance/evaluate", payload, timeout_ms=int(cfg.get("timeoutMs") or 1500))
    except Offline as e:
        mark_offline("evaluate: %s" % e)
        return None
    if status != 200 or not isinstance(data, dict):
        mark_offline("evaluate: resposta inesperada (HTTP %s)" % status)
        return None
    return data


def _rule_keys(resp, effects):
    keys = [r.get("key") for r in (resp.get("rules") or []) if isinstance(r, dict) and str(r.get("effect")).lower() in effects]
    return ", ".join(k for k in keys if k) or None


def _reason(resp, effects):
    if resp.get("reason"):
        return str(resp["reason"])
    for r in resp.get("rules") or []:
        if isinstance(r, dict) and str(r.get("effect")).lower() in effects and r.get("message"):
            return str(r["message"])
    return "ação não permitida pelas regras"


def block_message(resp):
    keys = _rule_keys(resp, ("block", "approval"))
    return "Barrado pela governança do PRMake: %s%s" % (_reason(resp, ("block", "approval")), " (regra %s)" % keys if keys else "")


def warn_message(resp):
    keys = _rule_keys(resp, ("warn",))
    return "Aviso da governança do PRMake: %s%s" % (_reason(resp, ("warn",)), " (regra %s)" % keys if keys else "")


def decision_output(resp, fail_open=True):
    """Saída do hook PreToolUse (JSON do Claude Code) para a resposta do evaluate; None = nada a dizer."""
    if resp is None:
        if fail_open:
            return None
        return {"hookSpecificOutput": {
            "hookEventName": "PreToolUse", "permissionDecision": "deny",
            "permissionDecisionReason": "Barrado pela governança do PRMake: não consegui consultar as regras e o "
                                        "administrador exige a checagem (failOpen desligado) — tente de novo em instantes."}}
    decision = str(resp.get("decision") or "allow").lower()
    if decision == "block":
        msg = block_message(resp) + ". Não contorne (nem por outro comando equivalente): registre no plano o que " \
                                    "precisa de uma pessoa e siga com o resto."
        return {"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                       "permissionDecisionReason": msg}}
    if decision == "warn":
        msg = warn_message(resp)
        return {"systemMessage": msg,
                "hookSpecificOutput": {"hookEventName": "PreToolUse",
                                       "additionalContext": msg + " — pode seguir; cite este aviso no resultado."}}
    return None


# ------------------------------------------------------------------------------------------- padrões (0101)
def standards_target(path):
    """'sql' (.sql) ou 'migration' (arquivo de código numa pasta migrations/Migrations) — os que o hook confere; None
    nos demais. Designer/ModelSnapshot do EF ficam fora (gerados: repetiriam os achados da migração)."""
    if not path:
        return None
    low = norm_path(path).lower()
    parts = low.split("/")
    name = parts[-1]
    if name.endswith(".sql"):
        return "sql"
    if "migrations" in parts[:-1]:
        if name.endswith(".designer.cs") or name.endswith("modelsnapshot.cs"):
            return None
        if name.endswith(MIGRATION_EXTS):
            return "migration"
    return None


def standards_wanted(cfg, tool):
    """Só com o hook "evaluate" ativo para a ferramenta (modo off já saiu antes); "standards": false na config desliga."""
    if (cfg or {}).get("standards") is False:
        return False
    return bool(hooks_for(cfg, "PreToolUse", "evaluate", tool))


def decode_text(raw):
    """Bytes do arquivo -> texto (UTF-16 com BOM, comum em script salvo pelo SSMS; senão UTF-8)."""
    if raw[:2] in (b"\xff\xfe", b"\xfe\xff"):
        return raw.decode("utf-16", "replace")
    return raw.decode("utf-8-sig", "replace")


def read_head(path):
    try:
        with open(path, "rb") as fh:
            return decode_text(fh.read(STANDARDS_MAX_BYTES))
    except OSError as e:
        log("padrões: não li %s: %s" % (path, e))
        return None


def standards_payload(text, target, path, cwd):
    """Corpo do POST Governance/standards/check: caminho relativo à raiz do repositório do arquivo, repositório pelo
    remote origin e card pela branch desse repositório (PRMAKE_CARD vence)."""
    full = path if is_abs(path) else os.path.join(cwd or os.getcwd(), path)
    folder = os.path.dirname(full)
    _, root = git_dir_of(folder)
    rel = norm_path(path)
    if root:
        try:
            rel = norm_path(os.path.relpath(full, root))
        except ValueError:  # outro drive no Windows
            rel = norm_path(path)
        if rel.startswith("../"):
            rel = norm_path(path)
    payload = {"text": text, "target": target, "path": rel, "repository": origin_repo(folder),
               "card": card_number(folder) or card_number(cwd), "source": source()}
    return {k: v for k, v in payload.items() if v not in (None, "")}


def standards_check(cfg, payload):
    """Resposta do PRMake ({decision, mode, wouldDecide, findings}) ou None. Sempre deixa passar sem resposta (não usa o
    failOpen das regras); 404/405/501 = PRMake sem o recurso, não pergunta de novo por 10 min. Tempo esgotado não marca
    a rede como fora (o evaluate das regras segue valendo)."""
    cache = read_json(cache_file(), {}) or {}
    if offline_recently(cache) or time.time() - float(cache.get("standardsMissingAt") or 0) < STANDARDS_MISSING_S:
        return None
    timeout = min(5000, max(3000, int((cfg or {}).get("timeoutMs") or 1500)))
    try:
        status, data = http("POST", "Governance/standards/check", payload, timeout_ms=timeout)
    except Offline as e:
        if e.code in (404, 405, 501):
            cache = read_json(cache_file(), {}) or {}
            cache["standardsMissingAt"] = time.time()
            write_json(cache_file(), cache)
        log("padrões: %s" % e)
        return None
    if status != 200 or not isinstance(data, dict):
        log("padrões: resposta inesperada (HTTP %s)" % status)
        return None
    return data


def standards_message(resp, path):
    """Texto curto para o Claude (até 10 achados: código, efeito, linha, título e dica); None sem achados."""
    findings = [f for f in (resp or {}).get("findings") or [] if isinstance(f, dict)]
    if not findings:
        return None
    decision = str(resp.get("decision") or "allow").lower()
    would = str(resp.get("wouldDecide") or "").lower()
    mode = resp.get("mode") or "?"
    head = "Padrões do PRMake (0101) em %s: %d achado(s) — decisão %s (modo %s%s)." % (
        os.path.basename(norm_path(path) or "") or "arquivo", len(findings), decision, mode,
        ", no enforce seria %s" % would if would and would != decision else "")
    lines = [head]
    for f in findings[:STANDARDS_MAX_FINDINGS]:
        line = "- %s [%s]%s: %s" % (f.get("code") or f.get("key") or "?", str(f.get("effect") or "monitor").lower(),
                                     " linha %s" % f["line"] if f.get("line") else "", cut(f.get("title") or "", 160))
        if f.get("hint"):
            line += " — dica: %s" % cut(str(f["hint"]).replace("\n", " "), 200)
        lines.append(line)
    if len(findings) > STANDARDS_MAX_FINDINGS:
        lines.append("- … e mais %d (lista completa: prmake-plan.sh standards-check <card> <arquivo>)"
                     % (len(findings) - STANDARDS_MAX_FINDINGS))
    if decision == "block":
        lines.append("Efeito block: corrija antes de gravar (o PRMake recusa o script/arquivo); não contorne.")
    elif decision == "warn":
        lines.append("Corrija os achados; se seguir assim, cite os códigos dos padrões no resultado.")
    else:
        lines.append("Padrões em monitorar/propostos: ajuste quando der (legado no dbo mantém os nomes atuais).")
    return "\n".join(lines)


def _stash_key(data):
    return "std:" + use_key(data)


def standards_pre(cfg, data, cwd):
    """PreToolUse do Write de .sql: confere o conteúdo; block -> saída de negação. A resposta fica guardada para o
    PostToolUse (não confere duas vezes nem grava o achado duas vezes na trilha)."""
    tool_input = data.get("tool_input") or {}
    path = tool_path(tool_input)
    if standards_target(path) != "sql" or not isinstance(tool_input.get("content"), str):
        return None
    text = tool_input["content"][:STANDARDS_MAX_BYTES]
    resp = standards_check(cfg, standards_payload(text, "sql", path, cwd))
    if resp is None:
        return None
    if str(resp.get("decision") or "").lower() == "block":
        msg = standards_message(resp, path) or "Padrões do PRMake (0101): o script não segue os padrões do card."
        return {"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                       "permissionDecisionReason": "Barrado pelos padrões do PRMake. " + msg}}
    try:
        os.makedirs(starts_dir(), exist_ok=True)
        with open(_start_path(_stash_key(data)), "w", encoding="utf-8") as fh:
            json.dump({"t": time.time(), "resp": resp}, fh)
    except OSError:
        pass
    return None


def standards_post(cfg, data, cwd):
    """PostToolUse de Write/Edit/MultiEdit em .sql/migração: confere o arquivo gravado (até 200 KB) e devolve os achados
    ao Claude (additionalContext; warn/block também à pessoa)."""
    tool_input = data.get("tool_input") or {}
    path = tool_path(tool_input)
    target = standards_target(path)
    stash = read_json(_start_path(_stash_key(data)))
    try:
        os.remove(_start_path(_stash_key(data)))
    except OSError:
        pass
    if not target:
        return None
    resp = stash.get("resp") if isinstance(stash, dict) and isinstance(stash.get("resp"), dict) else None
    if resp is None:
        full = path if is_abs(path) else os.path.join(cwd or os.getcwd(), path)
        text = read_head(full)
        if not text or not text.strip():
            return None
        resp = standards_check(cfg, standards_payload(text, target, path, cwd))
    msg = standards_message(resp, path)
    if not msg:
        return None
    out = {"hookSpecificOutput": {"hookEventName": "PostToolUse", "additionalContext": msg}}
    if str(resp.get("decision") or "").lower() in ("warn", "block"):
        codes = [str(f.get("code") or f.get("key")) for f in resp.get("findings") or [] if isinstance(f, dict)]
        out["systemMessage"] = "%s Padrões: %s." % (msg.splitlines()[0], ", ".join(codes[:STANDARDS_MAX_FINDINGS]))
    return out


# ------------------------------------------------------------------------------------------------------------- hook
def emit(obj):
    if obj:
        sys.stdout.write(json.dumps(obj, ensure_ascii=True))
        sys.stdout.flush()


def base_event(event, data, cwd):
    return {"event": event, "sessionId": data.get("session_id"), "machine": machine(), "cardNumber": card_number(cwd),
            "source": source()}


def record(evt):
    enqueue({k: v for k, v in evt.items() if v not in (None, "")})


def handle(data):
    """Um evento do Claude Code. Devolve o JSON de saída (ou None)."""
    event = str(data.get("hook_event_name") or "")
    cfg = get_config()
    if not cfg or str(cfg.get("mode") or "").lower() == "off":
        return None
    cwd = data.get("cwd") or os.getcwd()
    tool = data.get("tool_name") or ""
    tool_input = data.get("tool_input")
    out = None
    force_flush = False

    if event == "PreToolUse":
        if hooks_for(cfg, "PostToolUse", "record", tool):
            save_start(use_key(data))
        if hooks_for(cfg, "PreToolUse", "record", tool):
            record(dict(base_event(event, data, cwd), tool=tool, input=tool_input_text(tool, tool_input, RECORD_INPUT_MAX),
                        startedAt=now_iso()))
        matchers = cfg.get("toolMatchers")
        wants = bool(hooks_for(cfg, "PreToolUse", "evaluate", tool)) and not tool.startswith("mcp__prmake__") \
            and (matchers is None or matches_any(matchers, tool))
        if wants:
            payload = evaluate_payload(event, tool, tool_input, cwd, data.get("session_id"))
            resp = evaluate(cfg, payload)
            out = decision_output(resp, bool(cfg.get("failOpen", True)))
            if resp is None:
                log("evaluate sem resposta (%s): %s" % ("deixei passar" if cfg.get("failOpen", True) else "barrei",
                                                       cut(payload.get("input"), 120)))
        # 0101: Write de .sql passa pelos padrões antes de gravar (block nega; o resto o PostToolUse devolve).
        denied = bool(out and out.get("hookSpecificOutput", {}).get("permissionDecision") == "deny")
        if tool == "Write" and not denied and standards_wanted(cfg, tool):
            out = standards_pre(cfg, data, cwd) or out
    elif event == "PostToolUse":
        if tool in STANDARDS_TOOLS and standards_wanted(cfg, tool):
            resp = data.get("tool_response")
            if not (isinstance(resp, dict) and (resp.get("is_error") is True or bool(resp.get("error")))):
                out = standards_post(cfg, data, cwd)
        started = pop_start(use_key(data))
        if hooks_for(cfg, "PostToolUse", "record", tool):
            resp = data.get("tool_response")
            failed = isinstance(resp, dict) and (resp.get("is_error") is True or resp.get("interrupted") is True
                                                 or bool(resp.get("error")))
            fin = time.time()
            record(dict(base_event(event, data, cwd), tool=tool, input=tool_input_text(tool, tool_input, RECORD_INPUT_MAX),
                        startedAt=now_iso(started if started else fin), finishedAt=now_iso(fin),
                        durationMs=int((fin - started) * 1000) if started else None,
                        outcome="error" if failed else "ok"))
    elif event == "UserPromptSubmit":
        save_start("turn:%s" % data.get("session_id"))
        if hooks_for(cfg, "UserPromptSubmit", "record"):
            prompt = data.get("prompt") or ""
            record(dict(base_event(event, data, cwd), startedAt=now_iso(), detail="pedido com %d caracteres" % len(prompt)))
    elif event in ("Stop", "SubagentStop"):
        started = pop_start("turn:%s" % data.get("session_id")) if event == "Stop" else None
        if hooks_for(cfg, event, "record"):
            fin = time.time()
            record(dict(base_event(event, data, cwd), startedAt=now_iso(started if started else fin), finishedAt=now_iso(fin),
                        durationMs=int((fin - started) * 1000) if started else None, outcome="ok"))
        force_flush = event == "Stop"
    elif event == "SessionStart":
        if hooks_for(cfg, "SessionStart", "record"):
            record(dict(base_event(event, data, cwd), startedAt=now_iso(),
                        detail=cut("%s · %s" % (data.get("source") or "startup", norm_path(cwd)), 500)))
        force_flush = True
        clean_starts()

    if force_flush or should_flush():
        flush(cfg)
    return out


def run_hook(argv):
    # Dentro do executor 1.7.0 o hook dele (--from-executor) faz o trabalho; o do ~/.claude/settings.json não repete.
    if os.environ.get("PRMAKE_GOVERNANCE_HOOK") == "executor" and "--from-executor" not in argv:
        return 0
    try:
        raw = sys.stdin.buffer.read() if hasattr(sys.stdin, "buffer") else sys.stdin.read().encode("utf-8")
        data = json.loads(raw.decode("utf-8", "replace") or "{}")
        if not isinstance(data, dict):
            return 0
    except (ValueError, OSError):
        return 0
    try:
        emit(handle(data))
    except Exception as e:  # o hook nunca derruba a sessão
        log("erro no hook %s: %r" % (data.get("hook_event_name"), e))
    return 0


# -------------------------------------------------------------------------------------------------- linha de comando
def cmd_status():
    cache = read_json(cache_file(), {}) or {}
    cfg = cache.get("config") or {}
    print("governança do PRMake (script %s)" % TOOL_VERSION)
    print("  API:        %s" % api_base())
    print("  token:      %s" % ("ok" if token() else "NÃO encontrado (PRMAKE_TOKEN ou ~/.claude/prmake-token.txt)"))
    if cfg:
        age = int(time.time() - float(cache.get("fetchedAt") or 0))
        hooks = ", ".join("%s(%s)" % (h.get("key"), h.get("action")) for h in cfg.get("hooks") or [] if isinstance(h, dict))
        print("  config:     versão %s, modo %s, failOpen %s, timeout %s ms, cache %s s (lida há %d s)" % (
            cfg.get("version") or "?", cfg.get("mode"), cfg.get("failOpen"), cfg.get("timeoutMs"), cfg.get("cacheSeconds"), age))
        print("  hooks:      %s" % (hooks or "nenhum ativo"))
        print("  ferramentas avaliadas: %s" % (", ".join(cfg.get("toolMatchers") or []) or "nenhuma"))
    else:
        print("  config:     ainda não lida (rode: prmake-governance.py refresh)")
    if cache.get("lastError"):
        print("  último erro: %s" % cache["lastError"])
    print("  fila:       %d evento(s) a enviar (%s)" % (pending_count(), queue_file()))
    print("  log:        %s" % log_file())
    return 0


def cmd_test(command):
    cfg = get_config(force=True) or dict(DEFAULT_CONFIG)
    payload = evaluate_payload("PreToolUse", "Bash", {"command": command}, os.getcwd(), None)
    payload["source"] = "hook"
    try:
        status, resp = http("POST", "Governance/evaluate", payload, timeout_ms=max(3000, int(cfg.get("timeoutMs") or 1500)))
    except Offline as e:
        print("não consegui consultar o PRMake: %s" % e)
        print("no hook: %s" % ("deixaria passar (failOpen)" if cfg.get("failOpen", True) else "barraria (failOpen desligado)"))
        return 1
    if not isinstance(resp, dict):
        print("resposta inesperada do PRMake (HTTP %s)" % status)
        return 1
    print("decisão: %s (modo %s%s)" % (resp.get("decision"), resp.get("mode"),
                                       ", no enforce seria %s" % resp["wouldDecide"] if resp.get("wouldDecide") else ""))
    for r in resp.get("rules") or []:
        print("  regra %s [%s]: %s" % (r.get("key"), r.get("effect"), r.get("message") or r.get("name")))
    out = decision_output(resp)
    if out:
        print("o Claude veria: %s" % (out["hookSpecificOutput"].get("permissionDecisionReason")
                                      or out["hookSpecificOutput"].get("additionalContext")))
    return 0


def main(argv):
    args = argv[1:]
    sub = args[0] if args and not args[0].startswith("-") else None
    if sub is None:
        return run_hook(args)
    if sub == "status":
        return cmd_status()
    if sub == "refresh":
        cfg = get_config(force=True)
        print("config %s" % ("lida: versão %s, modo %s" % (cfg.get("version"), cfg.get("mode")) if cfg else "não lida (veja o log)"))
        return 0 if cfg else 1
    if sub == "flush":
        n = flush(get_config() or dict(DEFAULT_CONFIG), force=True)
        print("%d evento(s) enviado(s); %d na fila" % (n, pending_count()))
        return 0
    if sub == "test":
        if len(args) < 2:
            print('uso: prmake-governance.py test "<comando>"')
            return 1
        return cmd_test(" ".join(args[1:]))
    print("uso: prmake-governance.py [--from-executor] | status | refresh | flush | test \"<comando>\"")
    return 1


if __name__ == "__main__":
    try:
        if hasattr(sys.stdout, "reconfigure"):
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (OSError, ValueError):
        pass
    sys.exit(main(sys.argv))
