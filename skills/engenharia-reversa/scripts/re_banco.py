#!/usr/bin/env python3
"""Catalogo do banco de referencia (DEMO) para a engenharia reversa de um modulo (feature 0053) — SOMENTE LEITURA.

Tudo passa pelo sql-query.sh da analisar-bug (guarda read-only + transacao com ROLLBACK). Nunca le dados de tabelas de
negocio: so metadados (sys.*), definicoes de objetos (sys.sql_modules), jobs (msdb) e os textos da interface
(TB_WCM_MENU, TB_SYS_Application; as traducoes vem do Multilingual do revamp — re_traducoes.py, 0056). Para nao esbarrar na guarda (palavras como "delete" ou nomes "sp_"),
as consultas nao levam nomes de objetos: le as listas inteiras e filtra aqui; definicoes sao buscadas por object_id.

  re_banco.py catalogo --sql <sql-query.sh> --host prod --global DB --local DB [--local DB ...]
                       --prefix TB_SA3_ [--prefix ...] --sigla SA3 [--inventario inventario.json] --out <pasta-banco>
  re_banco.py diff <catalogo-antigo.json|snapshot.json> <catalogo-novo.json>
  re_banco.py snapshot <catalogo.json>          (versao enxuta para guardar na revisao publicada)
"""
import hashlib
import json
import os
import re
import subprocess
import sys
from collections import defaultdict

TYPE_KIND = {"U": "tabela", "V": "view", "P": "procedure", "FN": "function", "IF": "function", "TF": "function", "TR": "trigger"}
KIND_DIR = {"view": "views", "procedure": "procedures", "function": "functions", "trigger": "triggers"}
SHARED_PREFIXES = {"WCM", "SYS", "GLB", "EMP", "MLG", "CMN", "AUD", "LOG"}
SECRET = re.compile(r"(?i)((?:password|pwd|senha|secret|api[_-]?key|token)\s*[=:]\s*)('[^']*'|\"[^\"]*\"|[^\s;,]+)|(\s-P\s+)(\S+)|(/PASSWORD\s+)(\S+)")


def die(msg, code=2):
    print(f"ERRO: {msg}", file=sys.stderr)
    sys.exit(code)


def mask(text):
    if not text:
        return text
    return SECRET.sub(lambda m: (m.group(1) or m.group(3) or m.group(5)) + "***", text)


class Sql:
    def __init__(self, script, host):
        self.script, self.host = script, host

    def rows(self, db, sql, max_rows=200000, timeout=180):
        out = subprocess.run(["bash", self.script, "--host", self.host, "-d", db, "--json", "--max-rows", str(max_rows),
                              "--timeout", str(timeout), "-q", sql], capture_output=True, text=True)
        if out.returncode != 0:
            msg = (out.stderr or out.stdout).strip().splitlines()
            raise RuntimeError(f"{db}: {msg[-1] if msg else 'falhou'}")
        data = json.loads(out.stdout)
        if data.get("truncated"):
            print(f"AVISO: {db}: resultado cortado em {max_rows} linhas", file=sys.stderr)
        return data["rows"]

    def by_ids(self, db, template, ids, chunk=300):
        rows, ids = [], sorted(set(int(i) for i in ids))
        for i in range(0, len(ids), chunk):
            rows += self.rows(db, template.format(ids=",".join(str(x) for x in ids[i:i + chunk])))
        return rows


def norm_def(text):
    return re.sub(r"\s+", " ", (text or "").strip()).lower()


def sha(text):
    return hashlib.sha1(norm_def(text).encode("utf-8")).hexdigest()[:16]


def schedule_text(s):
    freq = {1: "uma vez", 4: "diário", 8: "semanal", 16: "mensal", 32: "mensal relativo", 64: "ao iniciar o SQL Agent", 128: "quando ocioso"}
    sub = {1: "", 2: "segundos", 4: "minutos", 8: "horas"}
    t = str(s.get("active_start_time") or 0).zfill(6)
    text = freq.get(int(s.get("freq_type") or 0), f"freq {s.get('freq_type')}")
    if int(s.get("freq_subday_type") or 1) > 1:
        text += f", a cada {s.get('freq_subday_interval')} {sub.get(int(s.get('freq_subday_type')), '')}"
    return f"{text}, a partir de {t[:2]}:{t[2:4]}" + ("" if s.get("enabled", 1) else " (agenda desativada)")


def inventory_names(path):
    tables, procs = set(), set()
    if path and os.path.exists(path):
        inv = json.load(open(path, encoding="utf-8"))
        for it in inv.get("items", []):
            if it["cat"] == "tabela" and it["name"].upper().startswith("TB_"):
                tables.add(it["name"].upper())
            elif it["cat"] == "procedure":
                procs.add(it["name"].upper())
    return tables, procs


def catalogo(args):
    sql = Sql(args["sql"], args["host"])
    prefixes = [p.upper() for p in args["prefix"]]
    sigla = (args.get("sigla") or "").upper()
    cited_tables, cited_procs = inventory_names(args.get("inventario"))
    out = args["out"]
    dbs = [("global", args["global"])] + [("local", d) for d in args["local"]]
    per_db = {}
    problems = []
    for scope, db in dbs:
        try:
            objs = sql.rows(db, "SELECT o.object_id AS id, o.name, o.type, SCHEMA_NAME(o.schema_id) AS sch, o.parent_object_id AS parent, "
                                "CONVERT(varchar(19), o.create_date, 120) AS created, CONVERT(varchar(19), o.modify_date, 120) AS modified "
                                "FROM sys.objects o WHERE o.is_ms_shipped = 0 AND o.type IN ('U','V','P','FN','IF','TF','TR')")
            deps = sql.rows(db, "SELECT d.referencing_id AS src, d.referenced_id AS dst, d.referenced_entity_name AS name "
                                "FROM sys.sql_expression_dependencies d")
        except RuntimeError as e:
            problems.append(str(e))
            print(f"AVISO: sem acesso a {db}: {e}", file=sys.stderr)
            continue
        per_db[db] = {"scope": scope, "objects": {o["id"]: o for o in objs}, "deps": deps}
        print(f"  {db}: {len(objs)} objetos, {len(deps)} dependências", file=sys.stderr)
    if not per_db:
        die("nenhum banco da DEMO respondeu — VPN ligada? credencial do alias '%s' em ~/.claude/sqlserver-credentials.json? (%s)"
            % (args["host"], "; ".join(problems)), 3)

    def is_module_table(name):
        n = name.upper()
        return any(n.startswith(p) for p in prefixes) or n in cited_tables

    def owner(name):
        n = name.upper()
        if sigla and re.search(rf"(^|_){re.escape(sigla)}(_|$)|{re.escape(sigla)}", n):
            return "modulo"
        if any(n.startswith(p) for p in prefixes):
            return "modulo"
        return "outro"

    selected = {}  # (db, id) -> motivo
    for db, info in per_db.items():
        objs = info["objects"]
        by_name = defaultdict(list)
        for o in objs.values():
            by_name[o["name"].upper()].append(o["id"])
        tables = {i for i, o in objs.items() if o["type"].strip() == "U" and is_module_table(o["name"])}
        for i in tables:
            selected[(db, i)] = "tabela do módulo"
        referencing = defaultdict(set)
        for d in info["deps"]:
            dst = d.get("dst")
            if dst is None and d.get("name"):
                ids = by_name.get(d["name"].upper(), [])
                dst = ids[0] if ids else None
            if dst is not None:
                referencing[dst].add(d["src"])
        # objetos que usam as tabelas do módulo (e os que usam esses — 2 níveis)
        frontier = set(tables)
        for level in range(2):
            nxt = set()
            for t in frontier:
                for src in referencing.get(t, ()):
                    if src in objs and (db, src) not in selected:
                        selected[(db, src)] = "usa tabela do módulo" if level == 0 else "usa objeto do módulo"
                        nxt.add(src)
            frontier = nxt
        for i, o in objs.items():
            t = o["type"].strip()
            if t == "U":
                continue
            n = o["name"].upper()
            if sigla and sigla in n and (db, i) not in selected:
                selected[(db, i)] = "sigla no nome"
            if n in cited_procs and (db, i) not in selected:
                selected[(db, i)] = "chamado pelo código"
            if t == "TR" and o.get("parent") in tables:
                selected[(db, i)] = "trigger em tabela do módulo"
        info["referencing"] = referencing

    # definições, eventos de trigger, colunas, chaves, checks
    defs, events = {}, defaultdict(list)
    for db, info in per_db.items():
        ids = [i for (d, i) in selected if d == db and info["objects"][i]["type"].strip() != "U"]
        for r in sql.by_ids(db, "SELECT m.object_id AS id, m.definition FROM sys.sql_modules m WHERE m.object_id IN ({ids})", ids):
            defs[(db, r["id"])] = mask(r["definition"] or "")
        trg = [i for i in ids if info["objects"][i]["type"].strip() == "TR"]
        for r in sql.by_ids(db, "SELECT e.object_id AS id, e.type_desc AS ev FROM sys.trigger_events e WHERE e.object_id IN ({ids})", trg):
            events[(db, r["id"])].append(r["ev"])
        tids = [i for (d, i) in selected if d == db and info["objects"][i]["type"].strip() == "U"]
        info["columns"] = sql.by_ids(db, "SELECT c.object_id AS tid, c.column_id AS pos, c.name, t.name AS tipo, c.max_length AS len, c.precision AS prec, "
                                         "c.scale, c.is_nullable AS nulo, c.is_identity AS ident, c.is_computed AS calc, dc.definition AS padrao, "
                                         "cc.definition AS formula FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id "
                                         "LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id "
                                         "LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id "
                                         "WHERE c.object_id IN ({ids})", tids)
        info["indexes"] = sql.by_ids(db, "SELECT i.object_id AS tid, i.name AS idx, i.is_primary_key AS pk, i.is_unique AS uq, c.name AS col, "
                                         "ic.key_ordinal AS ord FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id "
                                         "AND ic.index_id = i.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id "
                                         "WHERE (i.is_primary_key = 1 OR i.is_unique = 1) AND i.object_id IN ({ids})", tids)
        info["fks"] = sql.by_ids(db, "SELECT fk.name, fk.parent_object_id AS tid, OBJECT_NAME(fk.referenced_object_id) AS ref, pc.name AS col, "
                                     "rc.name AS refcol FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id "
                                     "JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id "
                                     "JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id "
                                     "WHERE fk.parent_object_id IN ({ids})", tids)
        info["checks"] = sql.by_ids(db, "SELECT k.parent_object_id AS tid, k.name, k.definition FROM sys.check_constraints k "
                                        "WHERE k.parent_object_id IN ({ids})", tids)

    # agrupa por nome: global x locais, igual x divergente entre locais
    grouped = defaultdict(list)
    for (db, i), why in selected.items():
        o = per_db[db]["objects"][i]
        grouped[(o["name"].upper(), o["type"].strip())].append((db, i, why))
    names_by_db_id = {(db, i): per_db[db]["objects"][i]["name"] for (db, i) in selected}
    objects, files = [], 0
    for kind_dir in list(KIND_DIR.values()) + ["tabelas", "jobs"]:
        os.makedirs(os.path.join(out, kind_dir), exist_ok=True)
    for (name_up, typ), occ in sorted(grouped.items()):
        kind = TYPE_KIND.get(typ, typ)
        first_db, first_id, why = occ[0]
        o = per_db[first_db]["objects"][first_id]
        scopes = sorted({per_db[db]["scope"] for db, _, _ in occ})
        hashes = {db: sha(defs.get((db, i), "")) for db, i, _ in occ} if kind != "tabela" else {}
        local_hashes = {db: h for db, h in hashes.items() if per_db[db]["scope"] == "local"}
        divergent = len(set(local_hashes.values())) > 1
        uses = set()
        for db, i, _ in occ:
            info = per_db[db]
            for d in info["deps"]:
                if d["src"] == i and d.get("name"):
                    uses.add(d["name"])
        used_by = set()
        for db, i, _ in occ:
            for src in per_db[db]["referencing"].get(i, ()):
                if (db, src) in names_by_db_id:
                    used_by.add(names_by_db_id[(db, src)])
                elif src in per_db[db]["objects"]:
                    used_by.add(per_db[db]["objects"][src]["name"])
        entry = {
            "name": o["name"], "schema": o.get("sch") or "dbo", "type": typ, "kind": kind, "reason": why, "owner": owner(o["name"]),
            "scope": "global e local" if len(scopes) > 1 else scopes[0], "dbs": sorted({db for db, _, _ in occ}),
            "created": o.get("created"), "modified": max((per_db[db]["objects"][i].get("modified") or "") for db, i, _ in occ),
            "divergent": divergent, "hash": sha("|".join(sorted(set(hashes.values())))) if hashes else None,
            "uses": sorted(uses)[:80], "usedBy": sorted(used_by)[:80],
        }
        if typ == "TR":
            parent = per_db[first_db]["objects"].get(o.get("parent"))
            entry["table"] = parent["name"] if parent else None
            entry["events"] = sorted(set(events.get((first_db, first_id), [])))
        if kind == "tabela":
            entry["file"] = f"tabelas/{o['name']}.md"
            write_table(out, entry, occ, per_db)
        else:
            base = f"{KIND_DIR[kind]}/{o['name']}"
            if divergent:
                entry["files"] = []
                for db, i, _ in occ:
                    f = f"{base}.{db}.sql"
                    write(out, f, defs.get((db, i), ""))
                    entry["files"].append(f)
                entry["file"] = entry["files"][0]
            else:
                entry["file"] = f"{base}.sql"
                write(out, entry["file"], defs.get((first_db, first_id), ""))
        files += 1
        objects.append(entry)

    module_names = {e["name"].upper() for e in objects}
    jobs = []
    try:
        jobs = read_jobs(sql, [db for db in per_db], module_names, prefixes, sigla, out)
    except RuntimeError as e:
        problems.append(f"msdb: {e}")
        print(f"AVISO: jobs do SQL Agent não lidos: {e}", file=sys.stderr)

    ui = read_interface(sql, args["global"], sigla)

    catalog = {
        "version": 1, "environment": args.get("environment") or "DEMO", "host": args["host"], "global": args["global"],
        "locals": args["local"], "prefixes": prefixes, "sigla": sigla, "problems": problems,
        "objects": objects, "jobs": jobs, "menus": ui["menus"], "application": ui["application"],
    }
    with open(os.path.join(out, "catalogo.json"), "w", encoding="utf-8") as fh:
        json.dump(catalog, fh, ensure_ascii=False, indent=1)
    # inventário do banco (mesmo formato do re_tool.py): a cobertura exige cada objeto
    inv = []
    for e in objects:
        cat = "tabela" if e["kind"] == "tabela" else e["kind"]
        inv.append({"cat": cat, "name": e["name"], "file": "banco/" + e["file"], "line": 1,
                    "detail": f"{e['scope']}{' · DIVERGENTE entre locais' if e['divergent'] else ''} · {e['reason']}"
                              + (f" · {e['table']} ({', '.join(e.get('events', []))})" if e.get("table") else "")})
        if e["kind"] == "tabela":
            for chk in e.get("checks", []):
                inv.append({"cat": "constraint", "name": chk["name"], "file": "banco/" + e["file"], "line": 1, "detail": chk["definition"][:200]})
    for j in jobs:
        inv.append({"cat": "job", "name": j["name"], "file": "banco/" + j["file"], "line": 1, "detail": j["schedule"]})
    counts = defaultdict(int)
    for it in inv:
        counts[it["cat"]] += 1
    with open(os.path.join(out, "inventario-banco.json"), "w", encoding="utf-8") as fh:
        json.dump({"version": 1, "sources": [{"role": "database", "path": f"{args['host']}:{db}"} for db in per_db], "files": {},
                   "counts": dict(counts), "items": inv}, fh, ensure_ascii=False, indent=1)
    by_kind = defaultdict(int)
    for e in objects:
        by_kind[e["kind"]] += 1
    print(f"Banco ({catalog['environment']}): " + ", ".join(f"{n} {k}" for k, n in sorted(by_kind.items())) + f", {len(jobs)} jobs"
          + (f" · {sum(1 for e in objects if e['divergent'])} divergentes entre locais" if any(e['divergent'] for e in objects) else "")
          + f" · {len(ui['menus'])} itens de menu -> {out}")
    if problems:
        print("Problemas (viram GAP no documento): " + "; ".join(problems))


def write(out, rel, text):
    with open(os.path.join(out, rel), "w", encoding="utf-8") as fh:
        fh.write(text or "")


def write_table(out, entry, occ, per_db):
    db, tid, _ = occ[0]
    info = per_db[db]
    cols = sorted([c for c in info.get("columns", []) if c["tid"] == tid], key=lambda c: c["pos"])
    idx = defaultdict(list)
    for r in info.get("indexes", []):
        if r["tid"] == tid:
            idx[(r["idx"], r["pk"], r["uq"])].append((r["ord"], r["col"]))
    fks = [f for f in info.get("fks", []) if f["tid"] == tid]
    checks = [c for c in info.get("checks", []) if c["tid"] == tid]
    entry["checks"] = [{"name": c["name"], "definition": c["definition"]} for c in checks]
    entry["columns"] = len(cols)
    lines = [f"# {entry['name']} ({entry['scope']}: {', '.join(entry['dbs'])})", "",
             f"Alterada em {entry['modified']} · motivo: {entry['reason']}", "", "| # | Coluna | Tipo | Nulo | Padrão / fórmula |", "|---|---|---|---|---|"]
    for c in cols:
        tipo = c["tipo"] + (f"({c['len']})" if c["tipo"] in ("varchar", "nvarchar", "char", "nchar", "varbinary") else "")
        extra = c.get("formula") or c.get("padrao") or ""
        lines.append(f"| {c['pos']} | {c['name']}{' (identity)' if c.get('ident') else ''} | {tipo} | {'sim' if c['nulo'] else 'não'} | {extra} |")
    if idx:
        lines += ["", "## Chaves e índices únicos"]
        for (name, pk, uq), cs in idx.items():
            lines.append(f"- {'PK' if pk else 'único'} `{name}`: {', '.join(c for _, c in sorted(cs))}")
    if fks:
        lines += ["", "## Chaves estrangeiras"]
        for f in fks:
            lines.append(f"- `{f['name']}`: {f['col']} → {f['ref']}.{f['refcol']}")
    if checks:
        lines += ["", "## Check constraints (regras no banco)"]
        for c in checks:
            lines.append(f"- `{c['name']}`: `{c['definition']}`")
    if entry.get("usedBy"):
        lines += ["", "## Objetos que usam", ", ".join(f"`{n}`" for n in entry["usedBy"])]
    write(out, entry["file"], "\n".join(lines) + "\n")


def read_jobs(sql, demo_dbs, module_names, prefixes, sigla, out):
    steps = sql.rows("msdb", "SELECT CAST(j.job_id AS varchar(36)) AS jid, j.name AS job, j.enabled, j.description, s.step_id, s.step_name, "
                             "s.subsystem, s.database_name AS db, s.command FROM msdb.dbo.sysjobs j JOIN msdb.dbo.sysjobsteps s ON s.job_id = j.job_id")
    sched = sql.rows("msdb", "SELECT CAST(js.job_id AS varchar(36)) AS jid, sc.name, sc.enabled, sc.freq_type, sc.freq_interval, sc.freq_subday_type, "
                             "sc.freq_subday_interval, sc.active_start_time FROM msdb.dbo.sysjobschedules js "
                             "JOIN msdb.dbo.sysschedules sc ON sc.schedule_id = js.schedule_id")
    names = [n for n in module_names if len(n) >= 5]
    pattern = re.compile("|".join([re.escape(n) for n in names] + [re.escape(p) for p in prefixes] + ([re.escape(sigla)] if sigla else [])), re.I) if (names or prefixes) else None
    demo = {d.upper() for d in demo_dbs}
    by_job = defaultdict(list)
    for s in steps:
        by_job[s["jid"]].append(s)
    jobs, other_clients = [], defaultdict(int)
    for jid, ss in by_job.items():
        cites = [s for s in ss if pattern and pattern.search(s.get("command") or "")]
        if not cites:
            continue
        in_demo = any((s.get("db") or "").upper() in demo or any(d in (s.get("command") or "").upper() for d in demo) for s in ss)
        if not in_demo:
            other_clients[re.sub(r"(?i)DB_[A-Z0-9]+_(PRD|TST|QA|DEV)_", "DB_*_", ss[0]["job"])] += 1
            continue
        sc = [schedule_text(x) for x in sched if x["jid"] == jid]
        name = ss[0]["job"]
        safe = re.sub(r"[^\w.-]+", "_", name)[:80]
        entry = {"name": name, "enabled": bool(ss[0]["enabled"]), "schedule": "; ".join(sc) or "sem agenda", "file": f"jobs/{safe}.md",
                 "steps": [{"step": s["step_id"], "name": s["step_name"], "subsystem": s["subsystem"], "db": s["db"],
                            "cites": sorted({m.group(0).upper() for m in pattern.finditer(s.get("command") or "")})[:20]} for s in sorted(ss, key=lambda x: x["step_id"])],
                 "hash": sha("|".join(mask(s.get("command") or "") for s in ss))}
        lines = [f"# Job {name}", "", f"{'Ativo' if entry['enabled'] else 'DESATIVADO'} · agenda: {entry['schedule']}",
                 "", (ss[0].get("description") or "").strip(), ""]
        for s in sorted(ss, key=lambda x: x["step_id"]):
            lines += [f"## Passo {s['step_id']} — {s['step_name']} ({s['subsystem']}, banco {s['db']})", "```sql", mask(s.get("command") or "").strip(), "```", ""]
        write(out, entry["file"], "\n".join(lines))
        jobs.append(entry)
    for j in jobs:
        key = re.sub(r"(?i)DB_[A-Z0-9]+_(PRD|TST|QA|DEV)_", "DB_*_", j["name"])
        if other_clients.get(key):
            j["otherClients"] = other_clients[key]
    return jobs


def read_interface(sql, global_db, sigla):
    """Textos da interface do módulo: aplicação (sigla) e menus — para o glossário. As traduções saíram daqui (0056):
    vêm do Multilingual do revamp (PostgreSQL), lidas pelo re_traducoes.py; o TB_WCM_LANGUAGE é legado."""
    result = {"application": None, "menus": []}
    try:
        apps = sql.rows(global_db, "SELECT CAST(a.idApplication AS varchar(36)) AS id, a.ApplicationName AS name, a.ApplicationAlias AS alias, "
                                   "a.Description AS description FROM TB_SYS_Application a")
        app = next((a for a in apps if (a.get("alias") or "").upper() == sigla), None) if sigla else None
        result["application"] = app
        if app:
            menus = sql.rows(global_db, "SELECT CAST(m.APPLICATION_ID AS varchar(36)) AS app, g.MENU_GROUP_NAME AS grp, m.MENU_DESC AS item, "
                                        "m.TARGET_PATH AS path, m.TARGET_PATH_NEW AS pathNew FROM TB_WCM_MENU m "
                                        "LEFT JOIN TB_WCM_MENU_GROUP g ON g.MENU_GROUP_ID = m.MENU_GROUP_ID WHERE m.ACTIVE = 1")
            result["menus"] = [m for m in menus if (m.get("app") or "").lower() == app["id"].lower()]
    except RuntimeError as e:
        print(f"AVISO: aplicação/menus não lidos: {e}", file=sys.stderr)
    return result


def snapshot(catalog):
    return {"environment": catalog.get("environment"), "objects": [{"name": o["name"], "kind": o["kind"], "scope": o["scope"], "modified": o.get("modified"),
                                                                     "hash": o.get("hash"), "divergent": o.get("divergent", False)} for o in catalog.get("objects", [])],
            "jobs": [{"name": j["name"], "hash": j.get("hash"), "enabled": j.get("enabled")} for j in catalog.get("jobs", [])]}


def diff(old, new):
    o = {(x["kind"], x["name"].upper()): x for x in old.get("objects", [])}
    n = {(x["kind"], x["name"].upper()): x for x in new.get("objects", [])}
    res = {"novos": [], "alterados": [], "removidos": []}
    for k, x in n.items():
        if k not in o:
            res["novos"].append(x["name"])
        elif (x.get("hash") and x.get("hash") != o[k].get("hash")) or (x.get("modified") or "") > (o[k].get("modified") or ""):
            res["alterados"].append(x["name"])
    res["removidos"] = [x["name"] for k, x in o.items() if k not in n]
    oj = {j["name"]: j for j in old.get("jobs", [])}
    for j in new.get("jobs", []):
        if j["name"] not in oj:
            res["novos"].append("job " + j["name"])
        elif j.get("hash") != oj[j["name"]].get("hash"):
            res["alterados"].append("job " + j["name"])
    res["removidos"] += ["job " + n for n in oj if n not in {j["name"] for j in new.get("jobs", [])}]
    return res


def parse_args(argv):
    args = {"prefix": [], "local": []}
    i = 0
    while i < len(argv):
        a = argv[i]
        if a.startswith("--"):
            key = a[2:]
            val = argv[i + 1] if i + 1 < len(argv) else None
            if key in ("prefix", "local"):
                args[key].append(val)
            else:
                args[key] = val
            i += 2
        else:
            args.setdefault("_", []).append(a)
            i += 1
    return args


def main(argv):
    if len(argv) < 2 or argv[1] in ("-h", "--help"):
        print(__doc__)
        return 0
    cmd = argv[1]
    if cmd == "catalogo":
        args = parse_args(argv[2:])
        for req in ("sql", "host", "global", "out"):
            if not args.get(req):
                die(f"falta --{req}")
        if not args["prefix"] and not args.get("sigla"):
            die("informe --prefix TB_<SIGLA>_ e/ou --sigla")
        os.makedirs(args["out"], exist_ok=True)
        catalogo(args)
        return 0
    if cmd == "snapshot":
        print(json.dumps(snapshot(json.load(open(argv[2], encoding="utf-8"))), ensure_ascii=False))
        return 0
    if cmd == "diff":
        old = json.load(open(argv[2], encoding="utf-8"))
        new = json.load(open(argv[3], encoding="utf-8"))
        print(json.dumps(diff(old, snapshot(new) if "objects" in new and new["objects"] and "uses" in new["objects"][0] else new), ensure_ascii=False, indent=1))
        return 0
    die(f"comando desconhecido: {cmd}")


if __name__ == "__main__":
    sys.exit(main(sys.argv))
