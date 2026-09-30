#!/usr/bin/env python3
"""Knowledge Center (base de regras de negocio da Solvace) para as skills — feature 0033.

Uso: kc.sh <comando> [args]
  check                 autoteste: piso do filtro (igual ao backend), conexao somente leitura e contagens
  sync [--full] [--dry-run] [--quiet]
                        le o que mudou no KC (desde a marca d'agua do PRMake) e envia ao PRMake. Carga completa
                        quando nao ha marca, quando passou FullSyncHours ou com --full. Sem credencial: avisa e sai 0.
  search <termos> [--limit N]
                        busca nos artigos (espelho local ~/.claude/solvace-kb; sem espelho, pela API do PRMake)
  article <n|ART-n>     texto inteiro de um artigo

Garantias (requisito do usuario): o PISO do filtro de teste abaixo e fixo — nao existe opcao para desliga-lo; o
plugin "Knowledge Center Configurations" so ACRESCENTA regras. Conteudo de artigo de teste nunca sai da maquina (vai
so o id, para o PRMake remover se estava guardado). A sessao com o banco e sempre somente leitura.
Ambiente (dev|prod), host/database/schema: plugin no PRMake (GET /Skills/config). Credencial: so local, em
~/.claude/knowledgecenter-credentials.json ({"dev": {"username","password"[,"host","port","dbname"]}, "prod": {...}}).
"""
import datetime as dt
import json
import os
import re
import sys
import unicodedata
import urllib.error
import urllib.parse
import urllib.request

BASE = os.environ.get("PRMAKE_API_BASE", "https://api.softhouse.app.br/api/v1").rstrip("/")
CREDENTIALS = os.path.expanduser(os.environ.get("KC_CREDENTIALS", "~/.claude/knowledgecenter-credentials.json"))
MIRROR = os.path.expanduser(os.environ.get("SOLVACE_KB_DIR", "~/.claude/solvace-kb"))

# ── Piso do filtro (IGUAL a KnowledgeNoiseFilter.cs — mudar os dois juntos) ─────────────────────────────────────────
PUBLISHED_STATUS_ID = 30
FLOOR_MIN_TEXT_LENGTH = 200
FLOOR_PATTERNS = [
    r"^title-",
    r"\btest",
    r"\bqa\b",
    r"\bprobe\b",
    r"\beditad[oa]s?\b",
    r"\btmp\b",
    r"\btemp\b",
    r"\bxpto\b",
    r"\blorem\b",
    r"\bdummy\b",
    r"categoryname",
    r"^[\d\W]+$",
]
_FLOOR = [re.compile(p, re.IGNORECASE) for p in FLOOR_PATTERNS]


def die(msg, code=1):
    print(f"ERRO: {msg}", file=sys.stderr)
    sys.exit(code)


def warn(msg):
    print(f"AVISO: {msg}", file=sys.stderr)


def normalize(value):
    if not value or not str(value).strip():
        return ""
    out = []
    for ch in unicodedata.normalize("NFD", str(value)):
        cat = unicodedata.category(ch)
        if cat in ("Mn", "Cf"):
            continue
        out.append(" " if ch == "_" else ch.lower())
    return re.sub(r"\s+", " ", "".join(out)).strip()


def compile_extra(patterns):
    result = []
    for p in patterns or []:
        if isinstance(p, str) and p.strip():
            try:
                result.append(re.compile(p.strip(), re.IGNORECASE))
            except re.error:
                warn(f"padrao extra invalido no plugin (ignorado): {p}")
    return result


def article_numbers(values):
    nums = set()
    for v in values or []:
        if isinstance(v, int):
            nums.add(v)
        elif isinstance(v, str) and re.search(r"\d+", v):
            nums.add(int(re.search(r"\d+", v).group()))
    return nums


def evaluate(a, opts):
    """(aceito, motivo) — mesma ordem de regras do backend."""
    if a.get("isDeleted"):
        return False, "removido"
    if a.get("statusId") != PUBLISHED_STATUS_ID:
        return False, "nao publicado"
    n = a.get("articleNumber")
    if n in opts["exclude"]:
        return False, "excluido pelo administrador"
    if n in opts["allow"]:
        return True, None
    if not a.get("categoryActive", True):
        return False, "categoria inativa"
    if not a.get("subcategoryActive", True):
        return False, "subcategoria inativa"
    for field in ("title", "category", "subcategory"):
        text = normalize(a.get(field))
        if text and any(r.search(text) for r in _FLOOR + opts["extra"]):
            return False, f"{field} parece teste"
    minimum = max(FLOOR_MIN_TEXT_LENGTH, opts["min"])
    if len(normalize(a.get("content"))) < minimum:
        return False, "texto curto"
    return True, None


# ── PRMake ──────────────────────────────────────────────────────────────────────────────────────────────────────────
def token():
    if os.environ.get("PRMAKE_TOKEN"):
        return os.environ["PRMAKE_TOKEN"].strip()
    for f in (os.path.expanduser("~/.claude/prmake-token.txt"),):
        if os.path.isfile(f):
            return open(f).read().strip()
    die("token do PRMake nao encontrado (PRMAKE_TOKEN ou ~/.claude/prmake-token.txt)")


def api(method, path, body=None, timeout=60):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(f"{BASE}{path}", data=data, method=method,
                                 headers={"x-api-key": token(), "accept": "application/json",
                                          **({"content-type": "application/json"} if data else {})})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw = r.read().decode()
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors="replace")[:300]
        die(f"HTTP {e.code} em {method} {path}: {detail}")
    except (urllib.error.URLError, TimeoutError) as e:
        die(f"sem conexao com o PRMake ({e})")


def kc_config():
    cfg = (api("GET", "/Skills/config") or {}).get("knowledge") or {}
    if not cfg.get("available"):
        warn("plugin 'Knowledge Center Configurations' nao encontrado no PRMake — usando dev e so o piso do filtro")
    env = (cfg.get("environment") or "dev").lower()
    f = cfg.get("filter") or {}
    opts = {"extra": compile_extra(f.get("excludePatterns")), "exclude": article_numbers(f.get("excludeArticles")),
            "allow": article_numbers(f.get("allowArticles")), "min": int(f.get("minTextLength") or 0)}
    backend_floor = (f.get("floor") or {}).get("patterns")
    return cfg, env, opts, backend_floor


def floor_matches_backend(backend_floor):
    if backend_floor is None:
        return True
    if list(backend_floor) != FLOOR_PATTERNS:
        warn("o piso do filtro desta skill difere do backend — atualize as skills (prmake-skills.sh update); "
             "o backend continua reaplicando o piso dele")
        return False
    return True


# ── Banco do KC (somente leitura) ───────────────────────────────────────────────────────────────────────────────────
def connect(cfg, env):
    try:
        import psycopg
    except ImportError:
        die("psycopg nao instalado no venv da skill — rode: prmake-skills.sh update --force base-solvace", 2)
    if not os.path.isfile(CREDENTIALS):
        return None, None
    creds = (json.load(open(CREDENTIALS)) or {}).get(env)
    if not creds or not creds.get("username") or not creds.get("password"):
        return None, None
    where = cfg.get(env) or {}
    host = where.get("host") or creds.get("host")
    dbname = where.get("database") or creds.get("dbname") or "KnowledgeCenter"
    schema = where.get("schema") or creds.get("schema") or "knowledge_center"
    if not re.fullmatch(r"[a-z_][a-z0-9_]*", schema):
        die(f"schema invalido na configuracao: {schema}")
    if not host:
        die(f"host do KC ({env}) nao configurado no plugin nem na credencial")
    conn = psycopg.connect(host=host, port=int(creds.get("port") or 5432), user=creds["username"],
                           password=creds["password"], dbname=dbname, connect_timeout=10,
                           options="-c default_transaction_read_only=on -c statement_timeout=30000")
    conn.read_only = True
    return conn, schema


def read_articles(conn, schema, since=None):
    S = schema
    changed = (f"greatest(a.create_date, a.last_update_date, a.deleted_date, s.last_update_date, c.last_update_date)")
    sql = f"""
        select a.article_unique_id::text, a.article_id, coalesce(a.title,''), coalesce(a.content_plain_text,''),
               a.status_id, coalesce(a.is_deleted,false),
               c.category_name, coalesce(c.is_active,false) and not coalesce(c.is_deleted,false),
               s.subcategory_name, coalesce(s.is_active,false) and not coalesce(s.is_deleted,false),
               {changed},
               coalesce((select array_agg(t.tag_name order by t.tag_name) from {S}.article_tags x
                         join {S}.tags t on t.tag_unique_id = x.tag_unique_id
                         where x.article_unique_id = a.article_unique_id and not coalesce(t.is_deleted,false)), '{{}}')
        from {S}.articles a
        left join {S}.subcategories s on s.subcategory_unique_id = a.subcategory_unique_id
        left join {S}.categories c on c.category_unique_id = s.category_unique_id
        {"where " + changed + " > %s" if since else ""}
        order by a.article_id"""
    rows = conn.execute(sql, (since,) if since else None).fetchall()
    out = []
    for r in rows:
        when = r[10]
        if isinstance(when, dt.datetime) and when.tzinfo is None:
            when = when.replace(tzinfo=dt.timezone.utc)
        out.append({"sourceId": r[0], "articleNumber": r[1], "title": r[2], "content": r[3], "statusId": r[4],
                    "isDeleted": r[5], "category": r[6], "categoryActive": bool(r[7]), "subcategory": r[8],
                    "subcategoryActive": bool(r[9]), "tags": list(r[11] or []),
                    "sourceUpdatedAt": when.isoformat() if when else None})
    return out


def parse_time(value):
    if not value:
        return None
    return dt.datetime.fromisoformat(value.replace("Z", "+00:00"))


# ── Comandos ────────────────────────────────────────────────────────────────────────────────────────────────────────
SELF_TEST = [
    # (titulo, categoria, subcategoria, texto, aceito?)
    ("Finding and Organizing Your Action Plans", "Action Plan", "Features", "x" * 650, True),
    ("Modular by Design. Complete by Nature.​", "Action Plan", "Features", "x" * 431, True),
    ("title-Haroldo", "Test", "Subcategoria cadastro 01", "x" * 650, False),
    ("teste qa editado 2", "Bell", "QA Test", "x" * 650, False),
    ("Modular by Design editado", "E-mail", "012345678 012345678", "x" * 650, False),
    ("Probe embedded media", "Action Plan", "Features", "x" * 650, False),
    ("Creating and Editing an Action Plan", "Action Plan", "Features", "curto", False),
]


def cmd_check():
    cfg, env, opts, backend_floor = kc_config()
    ok = floor_matches_backend(backend_floor)
    for title, cat, sub, text, expected in SELF_TEST:
        got, why = evaluate({"articleNumber": 1, "title": title, "category": cat, "subcategory": sub, "content": text,
                             "statusId": PUBLISHED_STATUS_ID, "isDeleted": False}, opts)
        if got != expected:
            ok = False
            print(f"FALHOU piso: '{title}' -> {'aceito' if got else 'recusado (' + str(why) + ')'}")
    print(f"piso do filtro: {'ok' if ok else 'COM PROBLEMA'} ({len(FLOOR_PATTERNS)} padroes, texto >= {FLOOR_MIN_TEXT_LENGTH})"
          f" · extras do plugin: {len(opts['extra'])} padroes, {len(opts['exclude'])} excluidos, {len(opts['allow'])} liberados")
    conn, schema = connect(cfg, env)
    if conn is None:
        print(f"ambiente {env}: sem credencial local em {CREDENTIALS} — sincronizacao indisponivel nesta maquina")
        return 0 if ok else 1
    with conn:
        arts = read_articles(conn, schema)
    accepted = [a for a in arts if evaluate(a, opts)[0]]
    print(f"ambiente {env}: conexao somente leitura ok · {len(arts)} artigos no KC · "
          f"{sum(1 for a in arts if a['statusId'] == PUBLISHED_STATUS_ID and not a['isDeleted'])} publicados · "
          f"{len(accepted)} passam no filtro")
    for a in accepted:
        print(f"   ART-{a['articleNumber']} {a['title'][:80]}")
    return 0 if ok else 1


def cmd_sync(args):
    full = "--full" in args
    dry = "--dry-run" in args
    quiet = "--quiet" in args
    cfg, env, opts, backend_floor = kc_config()
    floor_matches_backend(backend_floor)
    conn, schema = connect(cfg, env)
    if conn is None:
        if not quiet:
            print(f"KC ({env}): sem credencial local — sincronizacao pulada (a base do PRMake continua valendo)")
        return 0
    state = api("GET", f"/Knowledge/state?environment={env}") or {}
    watermark = parse_time(state.get("watermark"))
    last_full = parse_time(state.get("lastFullSyncAt"))
    hours = int(cfg.get("fullSyncHours") or 24)
    now = dt.datetime.now(dt.timezone.utc)
    # Carga completa: sem marca d'agua (1a vez ou ambiente novo), periodica, ou regras do filtro mudaram no plugin
    # (um artigo liberado de novo so volta com a carga completa).
    if watermark is None or last_full is None or now - last_full > dt.timedelta(hours=hours) or state.get("filterChanged"):
        full = True
    since = None if full else (watermark - dt.timedelta(minutes=5)).astimezone(dt.timezone.utc).replace(tzinfo=None)
    with conn:
        arts = read_articles(conn, schema, since)
    payload, rejected = [], 0
    for a in arts:
        accepted, _ = evaluate(a, opts)
        if accepted:
            payload.append(a)
        else:
            rejected += 1
            # Conteudo de teste nunca sai da maquina: so o id (o PRMake remove se estava guardado).
            payload.append({"sourceId": a["sourceId"], "articleNumber": a["articleNumber"], "statusId": a["statusId"],
                            "isDeleted": a["isDeleted"], "title": "", "content": "", "categoryActive": False,
                            "subcategoryActive": False, "sourceUpdatedAt": a["sourceUpdatedAt"]})
    if dry:
        print(f"KC ({env}) {'completa' if full else 'incremental'}: {len(arts)} lidos, {len(arts) - rejected} passam, {rejected} filtrados (dry-run)")
        return 0
    if not full and not arts:
        if not quiet:
            print(f"KC ({env}): nada mudou desde {state.get('watermark')}")
        return 0
    res = api("POST", "/Knowledge/sync", {"environment": env, "full": full, "articles": payload}, timeout=120) or {}
    if not quiet or res.get("changed") or res.get("removed"):
        print(f"KC ({env}) {'completa' if full else 'incremental'}: {res.get('received')} enviados · "
              f"{res.get('accepted')} aceitos ({res.get('changed')} novos/alterados) · {res.get('rejected')} filtrados · "
              f"{res.get('removed')} removidos · total na base: {res.get('total')}")
    return 0


def mirror_articles():
    folder = os.path.join(MIRROR, "knowledge")
    if not os.path.isdir(folder):
        return None
    arts = []
    for name in sorted(os.listdir(folder)):
        m = re.fullmatch(r"ART-(\d+)\.md", name)
        if not m:
            continue
        text = open(os.path.join(folder, name), encoding="utf-8").read()
        title = text.splitlines()[0].split(" — ", 1)[-1] if text else ""
        arts.append({"articleNumber": int(m.group(1)), "title": title, "text": text})
    return arts


def cmd_search(args):
    limit = 5
    if "--limit" in args:
        i = args.index("--limit")
        limit = int(args[i + 1])
        args = args[:i] + args[i + 2:]
    term = " ".join(args).strip()
    words = [w for w in normalize(term).split() if len(w) > 1]
    arts = mirror_articles()
    if arts is None:
        res = api("GET", f"/Knowledge/articles?q={urllib.parse.quote(term)}&limit={limit}") or []
        for a in res:
            print(f"ART-{a['articleNumber']} | {a['title']} | {a.get('category') or ''}\n   {a['content'][:300].strip()}")
        if not res:
            print("nenhum artigo encontrado")
        return 0
    scored = []
    for a in arts:
        text = normalize(a["text"])
        title = normalize(a["title"])
        score = sum((5 if w in title else 0) + (1 if w in text else 0) for w in words) if words else 1
        if score:
            scored.append((score, a))
    scored.sort(key=lambda x: (-x[0], x[1]["articleNumber"]))
    for _, a in scored[:limit]:
        body = a["text"].split("\n\n", 2)[-1].replace("\n", " ")
        hit = next((body.lower().find(w) for w in words if body.lower().find(w) >= 0), 0)
        start = max(0, hit - 80)
        print(f"ART-{a['articleNumber']} | {a['title']}\n   …{body[start:start + 300].strip()}…")
    if not scored:
        print("nenhum artigo encontrado (registre a lacuna: regra sem artigo no Knowledge Center)")
    return 0


def cmd_article(args):
    if not args or not re.search(r"\d+", args[0]):
        die("uso: kc.sh article <n|ART-n>")
    n = int(re.search(r"\d+", args[0]).group())
    path = os.path.join(MIRROR, "knowledge", f"ART-{n}.md")
    if os.path.isfile(path):
        print(open(path, encoding="utf-8").read())
        return 0
    a = api("GET", f"/Knowledge/articles/{n}")
    print(f"# ART-{a['articleNumber']} — {a['title']}\n\nCategoria: {a.get('category')}\n\n{a['content']}")
    return 0


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    cmd, args = sys.argv[1], sys.argv[2:]
    if cmd == "check":
        return cmd_check()
    if cmd == "sync":
        return cmd_sync(args)
    if cmd == "search":
        return cmd_search(args)
    if cmd == "article":
        return cmd_article(args)
    print(__doc__)
    return 1


if __name__ == "__main__":
    sys.exit(main())
