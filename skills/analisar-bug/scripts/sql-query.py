#!/usr/bin/env python3
"""Executor SQL Server SOMENTE LEITURA para dar contexto de dados na analise de bugs.

Seguranca contra escrita (em camadas):
  1) Valida a query: cada statement deve comecar com SELECT ou WITH; qualquer
     palavra-chave de escrita/DDL/exec e recusada (comentarios sao removidos antes).
  2) Executa dentro de transacao com autocommit desligado e SEMPRE faz ROLLBACK
     — nada e persistido, mesmo que algo escape da validacao.
  3) Isolamento READ UNCOMMITTED (nao bloqueia a producao) e limite de linhas.

--ping (0037): so testa o acesso (SELECT 1) — rode no inicio da analise para descobrir cedo VPN desligada,
credencial ausente ou o Claude Code barrando o comando. Exit 0 = ok; 2 = sem conexao (VPN?);
3 = login recusado; 4 = credenciais nao encontradas.

Credenciais: resolvidas por host a partir de ~/.claude/sqlserver-credentials.json
(ou env SQLSERVER_CREDENTIALS). NUNCA sao impressas.
"""
import argparse
import json
import os
import re
import sys

CREDS_PATHS = [
    os.environ.get("SQLSERVER_CREDENTIALS", ""),
    os.path.expanduser("~/.claude/sqlserver-credentials.json"),
]

# palavras que, se presentes como token, indicam escrita/DDL/side-effect -> recusa
FORBIDDEN = {
    "insert", "update", "delete", "merge", "upsert", "drop", "alter", "create",
    "truncate", "exec", "execute", "grant", "revoke", "deny", "backup", "restore",
    "dbcc", "shutdown", "reconfigure", "into", "bulk", "openrowset", "openquery",
    "openxml", "waitfor", "updatetext", "writetext", "readtext", "kill",
    "checkpoint", "sp_", "xp_",
}


NO_CREDS_HINT = ("o usuario cadastra no terminal DELE (nunca no chat): "
                 "bash ~/.claude/skills/.prmake/prmake-skills.sh db-credentials")


def load_creds():
    for p in CREDS_PATHS:
        if p and os.path.isfile(p):
            with open(p) as f:
                return json.load(f)
    print("ERRO: sem credenciais de banco nesta maquina (~/.claude/sqlserver-credentials.json) — " + NO_CREDS_HINT, file=sys.stderr)
    sys.exit(4)


def connection_problem(e):
    """Diz o que fazer com uma falha de conexao: VPN/rede (exit 2) ou login (exit 3)."""
    text = str(e).lower()
    if any(k in text for k in ("login failed", "18456", "password", "authentication")):
        return 3, "login recusado pelo SQL Server — confira o usuario/senha do host em ~/.claude/sqlserver-credentials.json"
    return 2, "sem conexao com o host — a VPN esta conectada? (os hosts RDS so respondem com a VPN)"


def ping(creds, srv, database):
    import pytds
    host = srv["hosts"][0]
    port = int(creds.get("port", 1433))
    try:
        conn = pytds.connect(server=host, port=port, database=database, user=srv["user"], password=srv["password"],
                             autocommit=False, login_timeout=15, timeout=15, appname="analisar-bug-readonly")
    except Exception as e:
        code, hint = connection_problem(e)
        print(f"ERRO ping {srv.get('alias', host)}/{database}: {hint}\n  detalhe: {str(e) or repr(e)}", file=sys.stderr)
        sys.exit(code)
    try:
        cur = conn.cursor()
        cur.execute("SELECT 1")
        cur.fetchall()
    finally:
        try:
            conn.rollback()
        except Exception:
            pass
        conn.close()
    print(f"OK ping {srv.get('alias', host)}/{database}: acesso de leitura funcionando")


def resolve_server(creds, host):
    """host pode ser hostname completo ou alias (prod/prod3/prod4) ou substring."""
    servers = creds.get("servers", [])
    h = host.lower()
    for s in servers:
        if s.get("alias", "").lower() == h:
            return s
    for s in servers:
        if any(hh.lower() == h for hh in s.get("hosts", [])):
            return s
    matches = [s for s in servers if any(h in hh.lower() for hh in s.get("hosts", []))
               or h in s.get("alias", "").lower()]
    if len(matches) == 1:
        return matches[0]
    if not matches:
        known = ", ".join(s.get("alias", "?") for s in servers) or "nenhum"
        print(f"ERRO: sem credencial para o servidor '{host}' nesta maquina (cadastrados: {known}) — {NO_CREDS_HINT}", file=sys.stderr)
        sys.exit(4)
    sys.exit(f"ERRO: host/alias '{host}' ambiguo; use o hostname completo ou o alias exato")


def strip_comments(sql):
    sql = re.sub(r"/\*.*?\*/", " ", sql, flags=re.DOTALL)  # /* ... */
    sql = re.sub(r"--[^\n]*", " ", sql)                       # -- ...
    return sql


def assert_readonly(sql):
    clean = strip_comments(sql).strip()
    if not clean:
        sys.exit("ERRO: query vazia")
    statements = [s.strip() for s in clean.split(";") if s.strip()]
    tokens = set(re.findall(r"[A-Za-z_][A-Za-z0-9_]*", clean.lower()))
    hit = tokens & FORBIDDEN
    # sp_/xp_ sao prefixos: checa qualquer token que comece com eles
    if any(t.startswith("sp_") or t.startswith("xp_") for t in tokens):
        hit = hit | {t for t in tokens if t.startswith(("sp_", "xp_"))}
    if hit:
        sys.exit(f"ERRO (guarda read-only): palavra(s) proibida(s) na query: {', '.join(sorted(hit))}")
    for st in statements:
        first = re.match(r"[A-Za-z]+", st)
        if not first or first.group(0).lower() not in ("select", "with"):
            sys.exit(f"ERRO (guarda read-only): so SELECT/WITH e permitido. Statement: '{st[:60]}...'")


def main():
    ap = argparse.ArgumentParser(description="Consulta SQL Server SOMENTE LEITURA")
    ap.add_argument("--host", required=True, help="hostname completo ou alias (prod/prod3/prod4)")
    ap.add_argument("--database", "-d", default="master", help="database (default master)")
    ap.add_argument("--query", "-q", help="SQL (SELECT/WITH). Se omitido, le do STDIN ou --file")
    ap.add_argument("--file", "-f", help="arquivo com a SQL")
    ap.add_argument("--max-rows", type=int, default=1000, help="limite de linhas (default 1000)")
    ap.add_argument("--timeout", type=int, default=60, help="timeout da query em s (default 60)")
    ap.add_argument("--json", action="store_true", help="saida em JSON (default: tabela)")
    ap.add_argument("--ping", action="store_true", help="so testa o acesso (SELECT 1): VPN, credencial, permissao")
    args = ap.parse_args()

    if args.ping:
        creds = load_creds()
        ping(creds, resolve_server(creds, args.host), args.database)
        return

    if args.file:
        with open(args.file) as f:
            sql = f.read()
    elif args.query:
        sql = args.query
    else:
        sql = sys.stdin.read()

    assert_readonly(sql)

    import pytds

    creds = load_creds()
    srv = resolve_server(creds, args.host)
    host = srv["hosts"][0]
    port = int(creds.get("port", 1433))

    try:
        conn = pytds.connect(
            server=host, port=port, database=args.database,
            user=srv["user"], password=srv["password"],
            autocommit=False, login_timeout=15, timeout=args.timeout,
            as_dict=True, appname="analisar-bug-readonly",
        )
    except Exception as e:
        code, hint = connection_problem(e)
        print(f"ERRO de conexao a {srv.get('alias', host)}/{args.database}: {hint}\n  detalhe: {str(e) or repr(e)}", file=sys.stderr)
        sys.exit(code)

    err = None
    rows, cols, truncated = [], [], False
    try:
        cur = conn.cursor()
        cur.execute("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;")
        cur.execute(sql)
        rows = cur.fetchmany(args.max_rows) if cur.description else []
        cols = [c[0] for c in cur.description] if cur.description else []
        truncated = len(rows) == args.max_rows
    except Exception as e:
        err = e
    finally:
        try:
            conn.rollback()  # garante que NADA foi persistido
        except Exception:
            pass
        conn.close()

    if err is not None:
        sys.exit(f"ERRO ao executar a query em {srv.get('alias', host)}/{args.database}: {err}")

    if args.json:
        print(json.dumps({"columns": cols, "rowCount": len(rows),
                          "truncated": truncated, "rows": rows},
                         default=str, ensure_ascii=False, indent=2))
        return

    if not cols:
        print("(sem resultado)")
        return
    widths = {c: len(c) for c in cols}
    for r in rows:
        for c in cols:
            widths[c] = max(widths[c], len(str(r.get(c, ""))))
    line = " | ".join(c.ljust(widths[c]) for c in cols)
    print(line)
    print("-+-".join("-" * widths[c] for c in cols))
    for r in rows:
        print(" | ".join(str(r.get(c, "")).ljust(widths[c]) for c in cols))
    print(f"\n({len(rows)} linha(s){' — TRUNCADO, use --max-rows' if truncated else ''})")


if __name__ == "__main__":
    main()
