#!/usr/bin/env python3
"""Traducoes do glossario da engenharia reversa (0056) — do Multilingual do revamp (Aurora PostgreSQL).

As traducoes do produto ficam no Multilingual (schema `multilingual`): `term_key` (o termo — a chave e o texto em
portugues, `term_key_portuguese_br`, igual ao TERM_NAME do legado), `translation` (texto por idioma) e `language`
(`language_tag` pt-BR/en-US/es-ES). O antigo TB_WCM_LANGUAGE do SQL Server e legado.

Somente leitura: sessao `default_transaction_read_only=on`. Credencial SO na maquina, nunca impressa:
  ~/.claude/multilingual-credentials.json  {"prod": {"host", "port", "dbname", "username", "password"}}
  (aceita tambem o JSON do secret como vem da AWS: {"prod": {"Host", "Port", "DbName", "UserName", "Password"}})

Uso (no venv da skill base-solvace, que tem o psycopg):
  re_traducoes.py --settings settings.json --inventario inventario.json [--catalogo banco/catalogo.json] --out traducoes.json
Le so os termos do modulo (rotulos do codigo, menus e aplicacao do catalogo) em lotes — nunca a tabela inteira.
Saida: [{"term", "pt", "en", "es"}] (o formato que o `re_tool.py termos` ja usa).
Codigos de saida: 0 ok · 3 sem credencial · 4 sem conexao (VPN?) · 2 uso/configuracao.
"""
import json
import os
import re
import sys

BATCH = 500


def die(msg, code=2):
    print(f"ERRO: {msg}", file=sys.stderr)
    sys.exit(code)


def arg(name, default=None):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv and sys.argv.index(name) + 1 < len(sys.argv) else default


def module_terms(inv_path, cat_path):
    """Textos que o modulo mostra na tela: rotulos do codigo (GetLanguageByName, i18n), menus e o nome da aplicacao."""
    terms = []
    if inv_path and os.path.exists(inv_path):
        for it in json.load(open(inv_path, encoding="utf-8")).get("items", []):
            if it.get("cat") == "rotulo" and it.get("name"):
                terms.append(it["name"])
    if cat_path and os.path.exists(cat_path):
        cat = json.load(open(cat_path, encoding="utf-8"))
        for m in cat.get("menus") or []:
            terms += [m.get("item"), m.get("grp")]
        app = cat.get("application") or {}
        terms += [app.get("name"), app.get("alias")]
    seen, out = set(), []
    for t in terms:
        t = (t or "").strip()
        if t and len(t) <= 3000 and t.lower() not in seen:
            seen.add(t.lower())
            out.append(t)
    return out


def credentials(path, env):
    path = os.path.expanduser(path)
    if not os.path.isfile(path):
        return None
    data = json.load(open(path, encoding="utf-8")) or {}
    # 0064: aceita tambem o arquivo "solto" (sem o bloco do ambiente), como o ~/.claude/postgres-credentials-dev.json
    block = data.get(env) or (data if any(k in data for k in ("host", "Host")) else {})
    pick = lambda *keys: next((block[k] for k in keys if block.get(k) not in (None, "")), None)
    creds = {"host": pick("host", "Host"), "port": pick("port", "Port") or 5432, "dbname": pick("dbname", "DbName", "database"),
             "user": pick("username", "UserName", "user"), "password": pick("password", "Password")}
    return creds if creds["host"] and creds["user"] and creds["password"] and creds["dbname"] else None


def main():
    settings_path, out = arg("--settings"), arg("--out")
    if not settings_path or not out:
        die("uso: re_traducoes.py --settings settings.json --inventario inventario.json [--catalogo catalogo.json] --out traducoes.json")
    cfg = (json.load(open(settings_path, encoding="utf-8")) or {}).get("translations") or {}
    if (cfg.get("source") or "multilingual") != "multilingual":
        die(f"fonte de traducoes nao suportada: {cfg.get('source')}")
    env = arg("--env") or cfg.get("environment") or "prod"
    schema = cfg.get("schema") or "multilingual"
    if not re.fullmatch(r"[a-z_][a-z0-9_]*", schema):
        die(f"schema invalido na configuracao: {schema}")
    langs = cfg.get("languages") or {"pt": "pt-BR", "en": "en-US", "es": "es-ES"}
    cred_file = arg("--credentials") or os.environ.get("RE_MLG_CREDENTIALS") or cfg.get("credentials") or "~/.claude/multilingual-credentials.json"

    terms = module_terms(arg("--inventario"), arg("--catalogo"))
    if not terms:
        json.dump([], open(out, "w", encoding="utf-8"))
        print("Traducoes (Multilingual): nenhum rotulo do modulo para traduzir (rode o inventario antes).")
        return 0
    creds = credentials(cred_file, env)
    if not creds:
        # 0064: sem a credencial de producao, tenta as de reserva da configuracao (padrao: o Multilingual de DEV da maquina) —
        # os termos do produto sao os mesmos; avisa que veio de outro ambiente.
        for fb in cfg.get("fallbackCredentials") or ["~/.claude/postgres-credentials-dev.json"]:
            c = credentials(fb, env)
            if c and "multilingual" in (c["dbname"] or "").lower():
                print(f"AVISO: sem a credencial de {env} ({cred_file}); usando {fb} (banco {c['dbname']} em {c['host'].split('.')[0]}) — confira se e o ambiente esperado")
                creds, env = c, f"{env}->reserva"
                break
    if not creds:
        secret = cfg.get("secretId") or "multilingual/production"
        die(f"sem credencial do Multilingual ({env}) em {cred_file} — grave o bloco \"{env}\" com o JSON do secret "
            f"{secret} (aws secretsmanager get-secret-value --secret-id {secret} --query SecretString --output text); "
            "o glossario segue so com os rotulos do codigo", 3)
    try:
        import psycopg
    except ImportError:
        die("psycopg nao instalado no venv da skill base-solvace — rode: prmake-skills.sh update --force base-solvace", 2)
    try:
        conn = psycopg.connect(host=creds["host"], port=int(creds["port"]), user=creds["user"], password=creds["password"],
                               dbname=creds["dbname"], connect_timeout=10,
                               options="-c default_transaction_read_only=on -c statement_timeout=30000")
        conn.read_only = True
    except Exception as e:  # noqa: BLE001 — a mensagem do driver nao traz a senha
        die(f"sem conexao com o Multilingual ({env}): {str(e).splitlines()[0][:200]} — a VPN esta ligada?", 4)

    tags = {tag: key for key, tag in langs.items()}
    by_term = {}
    lower_to_term = {t.lower(): t for t in terms}
    with conn:
        with conn.cursor() as cur:
            for i in range(0, len(terms), BATCH):
                batch = [t.lower() for t in terms[i:i + BATCH]]
                cur.execute(
                    f'SELECT tk.term_key_portuguese_br, l.language_tag, t.translation_content '
                    f'FROM {schema}.term_key tk '
                    f'JOIN {schema}.translation t ON t.term_key_id = tk.term_key_id '
                    f'JOIN {schema}."language" l ON l.language_id = t.language_id '
                    f'WHERE lower(tk.term_key_portuguese_br) = ANY(%s) AND l.language_tag = ANY(%s)',
                    (batch, list(tags)))
                for term_pt, tag, content in cur.fetchall():
                    term = lower_to_term.get((term_pt or "").lower())
                    if not term or not content:
                        continue
                    row = by_term.setdefault(term, {"term": term, "pt": term})
                    row[tags[tag]] = content
    rows = list(by_term.values())
    with open(out, "w", encoding="utf-8") as fh:
        json.dump(rows, fh, ensure_ascii=False)
    print(f"Traducoes (Multilingual {env}): {len(rows)} de {len(terms)} termos do modulo com traducao -> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
