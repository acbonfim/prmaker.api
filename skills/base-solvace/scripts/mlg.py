#!/usr/bin/env python3
"""Termos do Multilingual do produto (0107) — antes de escrever texto que o usuario final le.

Regra (card 79047): mensagem, rotulo ou aviso novo na tela/e-mail usa um termo que JA EXISTE no Multilingual (aprovado
pelo time do produto). Termo novo so com aprovacao do produto. Este script acha os termos existentes mais parecidos com
o texto que voce quer mostrar e confere os termos que o codigo novo usa.

  mlg.py search "<texto que voce quer mostrar>" [--limit 8] [--json]   termos parecidos (pt, en, es, revisado)
  mlg.py exists "<termo exato>" [...]                                  o termo esta cadastrado? (por termo)
  mlg.py check [--base origin/master] [--repo <pasta>] [--diff <arquivo>|-]
        termos das chamadas GetLanguageBy*(..., "termo") nas linhas NOVAS do diff — cadastrado ou nao, com sugestoes

Fonte: Multilingual do revamp (PostgreSQL, schema `multilingual`: term_key.term_key_portuguese_br = o termo/chave,
translation por idioma). Somente leitura (sessao read-only). Credencial so na maquina, nunca impressa:
  ~/.claude/multilingual-credentials.json  {"prod": {"host","port","dbname","username","password"}} (ou o JSON do secret
  multilingual/production como vem da AWS); reserva: ~/.claude/postgres-credentials-dev.json (Multilingual de dev).
  MLG_CREDENTIALS / MLG_ENV trocam o arquivo/bloco. Sem psycopg usa o `psql` (MLG_PSQL ou no PATH).
Codigos de saida: 0 ok · 1 check com termo nao cadastrado · 3 sem credencial · 4 sem conexao (VPN?) · 2 uso.
"""
import difflib
import json
import os
import re
import shutil
import subprocess
import sys
import unicodedata

SCHEMA = "multilingual"
LANGS = {"pt-BR": "pt", "en-US": "en", "es-ES": "es"}
STOP = set("a o as os um uma uns umas de da do das dos de em no na nos nas ao aos por para com sem e ou que se ja nao "
           "seu sua seus suas este esta estes estas esse essa isso isto ele ela eles elas voce voces the of to in on for "
           "and or is are be this that it you your with from not el la los las del al con por para que es su sus "
           "qual quais tem pois mais muito sobre entre ser foi sao has have does was were which".split())
# Sinonimos do produto: o texto que voce quer mostrar raramente usa a mesma palavra do termo cadastrado (site x planta).
SYNONYMS = [
    "site planta plant unidade fabrica sitio plantas sites",
    "sala room salas rooms",
    "acesso acessar access permissao permission permitido allowed autorizado negado denied",
    "pertence pertencer belongs belong pertenece",
    "diferente outra outro another different other",
    "encontrado encontrada encontrar found nenhum nenhuma",
    "registro record item registros",
    "usuario user usuarios users pessoa",
    "erro error falha falhou failed",
    "excluido excluida removido removida deleted removed apagado",
]
FOLD_FROM = "áàâãäéèêëíìîïóòôõöúùûüçñ"
FOLD_TO = "aaaaaeeeeiiiiooooouuuucn"
LANG_CALL = re.compile(r'GetLanguageBy\w*\s*\((?:[^()"]*,\s*)?(?:@)?"((?:[^"\\]|\\.)+)"')


def die(msg, code=2):
    print(f"ERRO: {msg}", file=sys.stderr)
    sys.exit(code)


def fold(text):
    t = unicodedata.normalize("NFKD", (text or "").lower())
    return "".join(c for c in t if not unicodedata.combining(c))


def tokens(text):
    return [w for w in re.findall(r"[a-z0-9]+", fold(re.sub(r"\{\d+\}", " ", text or ""))) if len(w) >= 3 and w not in STOP]


def stem(word):
    return word[:6]


GROUP = {}
for _i, _line in enumerate(SYNONYMS):
    for _w in _line.split():
        GROUP.setdefault(stem(_w), f"g{_i}")


def concepts(words):
    """Conceitos do texto: o grupo de sinonimos da palavra (ou a propria raiz)."""
    return {GROUP.get(stem(w), stem(w)) for w in words}


def expand(words):
    """Raizes para o filtro do banco: as da palavra e as dos sinonimos dela."""
    out = []
    for w in words:
        g = GROUP.get(stem(w))
        same = [stem(x) for line in SYNONYMS for x in line.split() if GROUP.get(stem(x)) == g] if g else [stem(w)]
        for s in [stem(w)] + same:
            if len(s) >= 3 and s not in out:
                out.append(s)
    return out


# ── conexao ──────────────────────────────────────────────────────────────────────────────────────

def load_credentials():
    env = os.environ.get("MLG_ENV") or "prod"
    files = [os.environ.get("MLG_CREDENTIALS") or "~/.claude/multilingual-credentials.json", "~/.claude/postgres-credentials-dev.json"]
    for i, path in enumerate(files):
        path = os.path.expanduser(path)
        if not os.path.isfile(path):
            continue
        data = json.load(open(path, encoding="utf-8")) or {}
        block = data.get(env) or (data if any(k in data for k in ("host", "Host")) else {})
        pick = lambda *keys: next((block[k] for k in keys if block.get(k) not in (None, "")), None)
        c = {"host": pick("host", "Host"), "port": int(pick("port", "Port") or 5432), "dbname": pick("dbname", "DbName", "database"),
             "user": pick("username", "UserName", "user"), "password": pick("password", "Password")}
        if not (c["host"] and c["user"] and c["password"] and c["dbname"]):
            continue
        if i > 0 and "multilingual" not in c["dbname"].lower():
            continue
        c["label"] = env if i == 0 else "dev (reserva)"
        return c
    die("sem credencial do Multilingual — grave ~/.claude/multilingual-credentials.json com o bloco \"prod\" (JSON do secret "
        "multilingual/production: aws secretsmanager get-secret-value --secret-id multilingual/production --query SecretString "
        "--output text) ou tenha ~/.claude/postgres-credentials-dev.json do Multilingual de dev. Sem isso, diga na resposta que "
        "NAO conferiu o Multilingual e peca a pessoa para confirmar o termo.", 3)


class Db:
    def __init__(self):
        self.c = load_credentials()
        self.conn = None
        try:
            import psycopg  # noqa: F401
            self.mode = "psycopg"
        except ImportError:
            self.psql = os.environ.get("MLG_PSQL") or shutil.which("psql")
            if not self.psql:
                die("sem psycopg (venv da skill base-solvace: prmake-skills.sh update --force base-solvace) e sem psql no PATH", 2)
            self.mode = "psql"

    def query(self, sql, params=()):
        if self.mode == "psycopg":
            import psycopg
            if self.conn is None:
                try:
                    self.conn = psycopg.connect(host=self.c["host"], port=self.c["port"], user=self.c["user"], password=self.c["password"],
                                                dbname=self.c["dbname"], connect_timeout=10,
                                                options="-c default_transaction_read_only=on -c statement_timeout=30000")
                    self.conn.read_only = True
                except Exception as e:  # noqa: BLE001 — a mensagem do driver nao traz a senha
                    die(f"sem conexao com o Multilingual ({self.c['label']}): {str(e).splitlines()[0][:200]} — a VPN esta ligada?", 4)
            with self.conn.cursor() as cur:
                cur.execute(sql, params)
                return cur.fetchall()
        # psql: parametros como literais (aspas dobradas; arrays como ARRAY['..'])
        def lit(v):
            if isinstance(v, (list, tuple)):
                return "ARRAY[" + ",".join(lit(x) for x in v) + "]::text[]" if v else "ARRAY[]::text[]"
            if isinstance(v, int):
                return str(v)
            return "'" + str(v).replace("'", "''") + "'"
        # uma passada so: o valor ja colocado pode ter "%s" (padroes LIKE) e nao pode ser trocado de novo
        parts = sql.replace("%%", "\x00").split("%s")
        if len(parts) - 1 != len(params):
            die("consulta com numero de parametros errado", 2)
        text = parts[0] + "".join(lit(p) + rest for p, rest in zip(params, parts[1:]))
        text = text.replace("\x00", "%")
        env = dict(os.environ, PGHOST=self.c["host"], PGPORT=str(self.c["port"]), PGUSER=self.c["user"], PGPASSWORD=self.c["password"],
                   PGDATABASE=self.c["dbname"], PGCONNECT_TIMEOUT="10",
                   PGOPTIONS="-c default_transaction_read_only=on -c statement_timeout=30000")
        r = subprocess.run([self.psql, "-X", "-q", "-tA", "-F", "\x1f", "-R", "\x1e", "-c", text], env=env, capture_output=True, text=True)
        if r.returncode != 0:
            msg = (r.stderr or "").strip().splitlines()
            if r.returncode != 2:  # 2 = conexao; o resto e a consulta
                die(f"consulta ao Multilingual falhou: {(msg[0] if msg else 'psql falhou')[:200]}", 2)
            die(f"sem conexao com o Multilingual ({self.c['label']}): {(msg[0] if msg else 'psql falhou')[:200]} — a VPN esta ligada?", 4)
        rows = [row for row in r.stdout.split("\x1e") if row.strip("\n")]
        return [tuple(None if f == "" else f for f in row.strip("\n").split("\x1f")) for row in rows]


def folded(col):
    return f"translate(lower({col}), '{FOLD_FROM}', '{FOLD_TO}')"


def translations(db, ids):
    if not ids:
        return {}
    rows = db.query(
        f"SELECT t.term_key_id::text, l.language_tag, t.translation_content, coalesce(t.is_reviewed, false)::text "
        f"FROM {SCHEMA}.translation t JOIN {SCHEMA}.\"language\" l ON l.language_id = t.language_id "
        f"WHERE t.term_key_id::text = ANY(%s) AND l.language_tag = ANY(%s)", ([str(i) for i in ids], list(LANGS)))
    out = {}
    for tid, tag, content, reviewed in rows:
        d = out.setdefault(str(tid), {})
        d[LANGS[tag]] = content
        d.setdefault("reviewed", []).append(str(reviewed).lower() in ("true", "t", "1"))
    return out


def row_of(tid, term, tr):
    t = tr.get(str(tid), {})
    rev = t.get("reviewed") or []
    return {"term": term, "en": t.get("en"), "es": t.get("es"), "reviewed": bool(rev) and all(rev)}


# ── comandos ─────────────────────────────────────────────────────────────────────────────────────

def search(db, text, limit):
    words = tokens(text)
    if not words:
        die("texto sem palavras para buscar")
    patterns = [f"%{s}%" for s in expand(words)][:24]
    rows = db.query(
        f"SELECT tk.term_key_id::text, tk.term_key_portuguese_br FROM {SCHEMA}.term_key tk "
        f"WHERE {folded('tk.term_key_portuguese_br')} LIKE ANY(%s) "
        f"OR EXISTS (SELECT 1 FROM {SCHEMA}.translation t WHERE t.term_key_id = tk.term_key_id AND {folded('t.translation_content')} LIKE ANY(%s)) "
        f"LIMIT 20000", (patterns, patterns))
    qc = concepts(words)
    qfold = fold(text)

    def score_of(candidate):
        cc = concepts(tokens(candidate))
        if not cc:
            return 0.0
        hit = len(qc & cc)
        # conceitos cobertos pesam mais que a grafia: "pertence a uma planta diferente" responde "pertence a outro site"
        return 0.6 * hit / len(qc) + 0.15 * hit / len(cc) + 0.25 * difflib.SequenceMatcher(None, qfold, fold(candidate)).ratio()

    scored = {}
    for tid, term in rows:
        if not term:
            continue
        score = score_of(term)
        key = fold(term).strip()
        if score > 0 and (key not in scored or scored[key][0] < score):
            scored[key] = (score, tid, term)
    top = sorted(scored.values(), key=lambda s: -s[0])[: max(limit * 3, limit)]
    tr = translations(db, [t[1] for t in top])
    # o texto pode estar em ingles (cards de clientes): reavalia pelo en tambem
    rescored = []
    for score, tid, term in top:
        en = tr.get(str(tid), {}).get("en")
        rescored.append((max(score, score_of(en)) if en else score, tid, term))
    return [dict(row_of(tid, term, tr), score=round(score, 2)) for score, tid, term in sorted(rescored, key=lambda s: -s[0])[:limit]]


def exists(db, terms):
    out = []
    for term in terms:
        rows = db.query(f"SELECT tk.term_key_id::text, tk.term_key_portuguese_br FROM {SCHEMA}.term_key tk "
                        f"WHERE lower(trim(tk.term_key_portuguese_br)) = lower(trim(%s)) LIMIT 1", (term,))
        if rows:
            out.append(dict(row_of(rows[0][0], rows[0][1], translations(db, [rows[0][0]])), exists=True, asked=term))
        else:
            out.append({"asked": term, "exists": False})
    return out


def diff_terms(diff_text):
    terms = []
    for line in diff_text.splitlines():
        if not line.startswith("+") or line.startswith("+++"):
            continue
        for m in LANG_CALL.finditer(line):
            t = m.group(1).replace('\\"', '"')
            if t not in terms:
                terms.append(t)
    return terms


def arg(name, default=None):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv and sys.argv.index(name) + 1 < len(sys.argv) else default


def positional():
    out, skip = [], False
    for a in sys.argv[2:]:
        if skip:
            skip = False
            continue
        if a in ("--limit", "--base", "--repo", "--diff"):
            skip = True
            continue
        if a.startswith("--"):
            continue
        out.append(a)
    return out


def show(rows, as_json, title):
    if as_json:
        print(json.dumps(rows, ensure_ascii=False, indent=1))
        return
    print(title)
    if not rows:
        print("  (nenhum termo parecido)")
    for i, r in enumerate(rows, 1):
        extra = " · ".join(f"{k}: \"{r[k]}\"" for k in ("en", "es") if r.get(k))
        print(f"  {i}. [{r.get('score', '')}] \"{r['term']}\"{(' · ' + extra) if extra else ''} · revisado: {'sim' if r.get('reviewed') else 'nao'}")


def main():
    if len(sys.argv) < 2 or sys.argv[1] in ("-h", "--help"):
        print(__doc__)
        return 0
    cmd, as_json, limit = sys.argv[1], "--json" in sys.argv, int(arg("--limit", "8"))
    if cmd == "search":
        text = " ".join(positional())
        if not text.strip():
            die("uso: mlg.py search \"<texto>\"")
        db = Db()
        show(search(db, text, limit), as_json, f"Termos do Multilingual ({db.c['label']}) parecidos com \"{text}\" — use um destes, do jeito que esta cadastrado:")
        return 0
    if cmd == "exists":
        terms = positional()
        if not terms:
            die("uso: mlg.py exists \"<termo>\" [...]")
        db = Db()
        rows = exists(db, terms)
        if as_json:
            print(json.dumps(rows, ensure_ascii=False, indent=1))
        else:
            for r in rows:
                print(f"{'CADASTRADO' if r['exists'] else 'NAO CADASTRADO'}: \"{r['asked']}\"" +
                      (f" · en: \"{r.get('en')}\" · revisado: {'sim' if r.get('reviewed') else 'nao'}" if r["exists"] else ""))
        return 0 if all(r["exists"] for r in rows) else 1
    if cmd == "check":
        src = arg("--diff")
        if src == "-":
            diff = sys.stdin.read()
        elif src:
            diff = open(src, encoding="utf-8", errors="replace").read()
        else:
            repo, base = arg("--repo", "."), arg("--base", "origin/master")
            r = subprocess.run(["git", "-C", repo, "diff", f"{base}...HEAD"], capture_output=True, text=True, encoding="utf-8", errors="replace")
            if r.returncode != 0:
                die(f"git diff falhou: {r.stderr.strip()[:200]}")
            diff = r.stdout
        terms = diff_terms(diff)
        if not terms:
            print("Nenhum termo novo (GetLanguageBy*) nas linhas adicionadas.")
            return 0
        db = Db()
        rows = exists(db, terms)
        missing = [r for r in rows if not r["exists"]]
        for r in rows:
            if r["exists"]:
                print(f"ok  \"{r['asked']}\" (cadastrado{', revisado' if r.get('reviewed') else ', traducao nao revisada'})")
        for r in missing:
            print(f"NAO CADASTRADO \"{r['asked']}\" — troque por um termo existente (ou peca a aprovacao do produto):")
            for i, s in enumerate(search(db, r["asked"], 5), 1):
                print(f"    {i}. [{s['score']}] \"{s['term']}\"" + (f" · en: \"{s['en']}\"" if s.get("en") else ""))
        if as_json:
            print(json.dumps(rows, ensure_ascii=False))
        return 1 if missing else 0
    die(f"comando desconhecido: {cmd} (search | exists | check)")


if __name__ == "__main__":
    sys.exit(main())
