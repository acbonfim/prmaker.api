#!/usr/bin/env python3
"""Engenharia reversa automatica de um repositorio .NET da Solvace (feature 0034) — so leitura, so biblioteca padrao.

Uso:
  mapear.py fatos <pasta-do-repo> [--key chave] [--out fatos.json]
      extrai os FATOS do repositorio (projetos, rotas HTTP, casos de uso, entidades, tabelas, filas/topicos, Lambdas,
      hubs, pacotes Solvace.*, chaves de configuracao que apontam para outros servicos) — com a evidencia (arquivo:linha).
  mapear.py relacoes <pasta-com-fatos> [--out relacoes.json]
      cruza os fatos de todos os repositorios e monta as interdependencias (evento, fila, banco, http, pacote).
  mapear.py secoes <fatos.json> <relacoes.json> <pasta-saida>
      escreve as secoes em markdown (template da base-solvace) e o projeto.json do repositorio.
  mapear.py tudo <pasta-com-os-repositorios> <pasta-saida> [--curados <pasta-kb-revisada>]
      os tres passos para todos os repositorios da pasta; projetos ja revisados (--curados) mantem nome/resumo/secoes
      revisados e ganham so as secoes que faltam e as relacoes. Depois: arch.sh publicar-pasta <pasta-saida>.

Nunca grava segredos: valores de chaves com cara de segredo (senha, token, key, dsn, connection string) sao ignorados.
"""
import json
import os
import re
import subprocess
import sys
from collections import Counter, defaultdict

SKIP_DIRS = {"bin", "obj", "node_modules", ".git", ".vs", ".idea", "packages", "dist", "TestResults", "wwwroot"}
SECRET_KEY = re.compile(r"(pass(word)?|pwd|secret|token|apikey|api_key|dsn|connectionstring|credential|privatekey|accesskey)", re.I)
MAX_FILE = 600_000


def walk(root, exts):
    for d, dirs, files in os.walk(root):
        dirs[:] = [x for x in dirs if x not in SKIP_DIRS and not x.startswith(".")]
        for f in files:
            if f.endswith(exts):
                p = os.path.join(d, f)
                try:
                    if os.path.getsize(p) <= MAX_FILE:
                        yield p
                except OSError:
                    pass


def read(p):
    try:
        return open(p, encoding="utf-8", errors="replace").read()
    except OSError:
        return ""


def rel(root, p, line=None):
    r = os.path.relpath(p, root)
    return f"{r}:{line}" if line else r


def line_of(text, idx):
    return text.count("\n", 0, idx) + 1


def git(root, *args):
    try:
        return subprocess.run(["git", "-C", root, *args], capture_output=True, text=True, timeout=20).stdout.strip()
    except Exception:
        return ""


# ── Fatos ───────────────────────────────────────────────────────────────────────────────────────────────────────────
ROUTE_CLASS = re.compile(r'\[Route\(\s*"([^"]*)"\s*\)\]')
HTTP_ATTR = re.compile(r'\[Http(Get|Post|Put|Delete|Patch)(?:\(\s*"([^"]*)"[^)]*\))?\]')
CLASS_DECL = re.compile(r'\bclass\s+(\w+)')
METHOD_AFTER = re.compile(r'\b(?:public|private|protected|internal)\s+(?:async\s+)?[\w<>\[\],.? ]+\s+(\w+)\s*\(')
TABLE_TB = re.compile(r'\b(TB_[A-Z0-9]+(?:_[A-Z0-9]+)*)\b')
TO_TABLE = re.compile(r'ToTable\(\s*"([^"]+)"')
QUEUE_LIT = re.compile(r'\$?"([A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+)_?(?:\{[^}]*\})?"')
HUB_CLASS = re.compile(r'class\s+(\w+Hub)\s*:\s*Hub\b')
DBSET = re.compile(r'DbSet<(\w+)>')
HTTPCLIENT_NAMED = re.compile(r'AddHttpClient(?:<[^>]+>)?\(\s*"([^"]+)"')
URL_LIT = re.compile(r'https?://[A-Za-z0-9.\-]+(?:/[A-Za-z0-9_\-./{}]*)?')
QUEUE_HINT = re.compile(r'(WORKER|QUEUE|EVENT|TOPIC|NOTIF|SNS|SQS|CREATED|UPDATED|DELETED)', re.I)


def classify_project(name):
    n = name.lower()
    for k, v in [("test", "test"), ("lambda", "lambda"), ("worker", "worker"), (".api", "api"), ("integration", "integration"),
                 ("application.abstractions", "abstractions"), ("application", "application"), ("domain", "domain"),
                 ("infra", "infra")]:
        if k in n:
            return v
    return "other"


def extract(root, key=None):
    root = os.path.abspath(root)
    repo = os.path.basename(root.rstrip("/"))
    facts = {
        "repo": repo, "key": key or repo.lower(),
        "remote": git(root, "remote", "get-url", "origin").replace(".git", ""),
        "commit": git(root, "rev-parse", "HEAD"), "branch": git(root, "rev-parse", "--abbrev-ref", "HEAD"),
        "lastCommitDate": git(root, "log", "-1", "--format=%ad", "--date=short"),
        "readme": "", "codeUrls": [], "projects": [], "controllers": [], "useCases": [], "entities": [], "hubs": [],
        "tables": {}, "tableEvidence": {}, "queuesSent": {}, "lambdas": [], "httpClients": [], "configUrls": [],
        "packages": {}, "dbTiers": [], "files": {"cs": 0, "ts": 0, "sql": 0}, "workflows": [],
    }
    # README
    for name in ("README.md", "readme.md", "Readme.md"):
        p = os.path.join(root, name)
        if os.path.isfile(p):
            txt = re.sub(r"<[^>]+>|!\[[^\]]*\]\([^)]*\)", "", read(p))
            paras = [x.strip() for x in re.split(r"\n\s*\n", txt) if x.strip() and not x.strip().startswith(("#", "|", "```", "[!"))]
            facts["readme"] = (paras[0] if paras else "")[:600]
            break
    # Projetos
    for p in walk(root, (".csproj",)):
        t = read(p)
        name = os.path.splitext(os.path.basename(p))[0]
        fw = re.findall(r"<TargetFrameworks?>([^<]+)</TargetFrameworks?>", t)
        pkgs = re.findall(r'<PackageReference\s+Include="(Solvace\.[^"]+)"', t)
        for pk in pkgs:
            facts["packages"][pk] = facts["packages"].get(pk, 0) + 1
        facts["projects"].append({"name": name, "path": rel(root, p), "framework": ",".join(fw), "type": classify_project(name)})
        m = re.search(r"Infra\.Data\.(Global|Corporate|Local)\.(\w+)", name)
        if m and f"{m.group(1)} ({m.group(2)})" not in facts["dbTiers"]:
            facts["dbTiers"].append(f"{m.group(1)} ({m.group(2)})")
    # C#
    table_count = Counter()
    for p in walk(root, (".cs", ".sql")):
        t = read(p)
        facts["files"]["sql" if p.endswith(".sql") else "cs"] += 1
        low = p.lower()
        if p.endswith(".cs") and "controller" in os.path.basename(low) and "/test" not in low:
            base = ROUTE_CLASS.search(t)
            cls = CLASS_DECL.search(t)
            actions = []
            for m in HTTP_ATTR.finditer(t):
                after = METHOD_AFTER.search(t, m.end())
                actions.append({"verb": m.group(1).upper(), "route": m.group(2) or "", "method": after.group(1) if after else "",
                                "line": line_of(t, m.start())})
            if cls and actions:
                facts["controllers"].append({"name": cls.group(1), "route": base.group(1) if base else "", "file": rel(root, p), "actions": actions})
        for m in HUB_CLASS.finditer(t):
            facts["hubs"].append({"name": m.group(1), "file": rel(root, p)})
        for m in TABLE_TB.finditer(t):
            table_count[m.group(1)] += 1
            facts["tableEvidence"].setdefault(m.group(1), rel(root, p, line_of(t, m.start())))
        for m in TO_TABLE.finditer(t):
            table_count[m.group(1)] += 1
            facts["tableEvidence"].setdefault(m.group(1), rel(root, p, line_of(t, m.start())))
        if p.endswith(".cs") and "/test" not in low:
            for m in QUEUE_LIT.finditer(t):
                name = m.group(1)
                ctx = t[max(0, m.start() - 200): m.end() + 200]
                if QUEUE_HINT.search(name) or re.search(r"(SendMessage|Queue|Sqs|SQS|Publish|Sns|SNS|Topic)", ctx):
                    if len(name) >= 6 and not name.startswith(("TB_", "SP_", "FN_", "VW_", "PK_", "FK_", "IX_")):
                        facts["queuesSent"].setdefault(name, rel(root, p, line_of(t, m.start())))
            for m in URL_LIT.finditer(t):
                u = m.group(0)
                if not re.search(r"(localhost|127\.0\.0\.1|schemas\.|w3\.org|xmlsoap|microsoft\.com/(ws|fwlink)|sentry\.io|aka\.ms|github\.com|example\.)", u) \
                        and len(facts["codeUrls"]) < 60 and not any(c["value"] == u for c in facts["codeUrls"]):
                    facts["codeUrls"].append({"key": "código", "value": u[:160], "file": rel(root, p, line_of(t, m.start()))})
            for m in HTTPCLIENT_NAMED.finditer(t):
                facts["httpClients"].append({"name": m.group(1), "file": rel(root, p, line_of(t, m.start()))})
            for m in DBSET.finditer(t):
                if m.group(1) not in facts["entities"]:
                    facts["entities"].append(m.group(1))
    facts["tables"] = dict(table_count.most_common())
    # Casos de uso (pastas)
    uc = set()
    for d, dirs, _ in os.walk(root):
        dirs[:] = [x for x in dirs if x not in SKIP_DIRS]
        parts = d.split(os.sep)
        for marker in ("UseCases", "Commands", "Queries"):
            if marker in parts:
                i = parts.index(marker)
                if len(parts) > i + 1 and "Abstractions" not in d and "test" not in d.lower():
                    uc.add(f"{marker}/{parts[i + 1]}")
    facts["useCases"] = sorted(uc)
    # Entidades do dominio
    for p in walk(root, (".cs",)):
        if "/Domain/" in p and "/Entities/" in p:
            for m in re.finditer(r"\bclass\s+(\w+)", read(p)):
                if m.group(1) not in facts["entities"]:
                    facts["entities"].append(m.group(1))
    # Lambdas
    for p in walk(root, (".json",)):
        if os.path.basename(p) == "aws-lambda-tools-defaults.json":
            try:
                j = json.loads(read(p))
            except json.JSONDecodeError:
                continue
            envs = dict(re.findall(r'"?(\w+)"?\s*=\s*"?([^";]*)"?', j.get("environment-variables", "") or ""))
            facts["lambdas"].append({"name": j.get("function-name", ""), "description": (j.get("function-description") or "")[:200],
                                     "runtime": j.get("function-runtime", ""), "queue": envs.get("QUEUE_NAME", ""),
                                     "topic": envs.get("SNS_NAME", ""), "file": rel(root, p)})
        elif os.path.basename(p) == "serverless.template":
            try:
                j = json.loads(read(p))
            except json.JSONDecodeError:
                continue
            for rk, rv in (j.get("Resources") or {}).items():
                for ek, ev in ((rv.get("Properties") or {}).get("Events") or {}).items():
                    props = ev.get("Properties") or {}
                    sched = props.get("Schedule")
                    if sched:
                        facts["lambdas"].append({"name": rk, "description": f"agendada: {sched}", "runtime": "", "queue": "",
                                                 "topic": "", "schedule": str(sched), "file": rel(root, p)})
        elif os.path.basename(p).startswith("appsettings") and "/bin/" not in p:
            try:
                j = json.loads(re.sub(r"^﻿", "", read(p)))
            except json.JSONDecodeError:
                continue

            def visit(node, path):
                if isinstance(node, dict):
                    for k, v in node.items():
                        visit(v, path + [k])
                elif isinstance(node, list):
                    for i, v in enumerate(node):
                        visit(v, path + [str(i)])
                elif isinstance(node, str):
                    keypath = ":".join(path)
                    if SECRET_KEY.search(keypath) or "sentry" in keypath.lower():
                        return
                    # Origens de CORS/redirect sao quem chama o servico, nao dependencias dele.
                    if re.search(r"cors|allowedorigin|origins|redirect", keypath, re.I):
                        return
                    if URL_LIT.match(node.strip()):
                        facts["configUrls"].append({"key": keypath, "value": node.strip()[:160], "file": rel(root, p)})
            visit(j, [])
    # Workflows
    wf = os.path.join(root, ".github", "workflows")
    if os.path.isdir(wf):
        facts["workflows"] = sorted(os.listdir(wf))
    return facts


# ── Relacoes ────────────────────────────────────────────────────────────────────────────────────────────────────────
# Donos das siglas de tabela (convencao do legado/revamp). A sigla tambem e aprendida das filas das proprias Lambdas de
# cada modulo (ex.: ACP_USER_EVENT_CREATED -> actionplan). Sem dono conhecido, nao ha aresta (melhor faltar do que inventar).
PREFIX_OWNER = {
    "ACP": "actionplan", "DFT": "defecttag", "UNC": "unsafecondition", "SCC": "scorecard", "BOS": "bos", "LUP": "lpp",
    "SA3": "rca", "MLH": "kaizen", "MOC": "moc", "TRN": "training", "WRK": "workpermit", "NCF": "nonconformity",
    "ICD": "incident", "CMP": "complaint", "PJT": "project", "AST": "assessment", "CLN": "centerline", "LIL": "cil",
    "CHK": "checklist", "GED": "documentation", "DOB": "digitalobeya", "MST": "masterdata", "MNT": "masterdata",
    "CAF": "users", "ALR": "alert", "QIZ": "quiz", "SVY": "survey", "PRS": "praise", "PST": "post", "CMM": "comment",
}
SHARED_GLOBAL = {"WCM", "SYS", "GLB", "SIT", "USR"}
INFRA_REPOS = ("buildingblocks", "moduleintegration", "datalake", "api-infra", "api-gateway-infra", "audittrail", "wiki")
EXTERNAL_HOSTS = [
    (r"graph\.microsoft\.com", "ext:microsoft-graph", "Microsoft Graph / Teams"),
    (r"login\.microsoftonline\.com", "ext:azure-ad", "Azure AD / Entra ID"),
    (r"openai\.azure\.com|api\.openai\.com", "ext:openai", "OpenAI / Azure OpenAI"),
    (r"api\.anthropic\.com", "ext:anthropic", "Anthropic (Claude)"),
    (r"generativelanguage\.googleapis\.com", "ext:gemini", "Google Gemini"),
    (r"api\.hubapi\.com|hubspot", "ext:hubspot", "HubSpot"),
    (r"dev\.azure\.com|visualstudio\.com", "ext:azure-devops", "Azure DevOps"),
    (r"powerbi\.com", "ext:powerbi", "Power BI"),
    (r"snowflakecomputing\.com", "ext:snowflake", "Snowflake"),
    (r"databricks", "ext:databricks", "Databricks"),
    (r"cognito", "ext:cognito", "AWS Cognito"),
    (r"s3[.-][a-z0-9-]*\.?amazonaws\.com|\.s3\.amazonaws\.com", "ext:s3", "AWS S3"),
    (r"sqs\.[a-z0-9-]+\.amazonaws\.com", "ext:sqs", "AWS SQS"),
    (r"es\.amazonaws\.com|opensearch", "ext:opensearch", "OpenSearch"),
    (r"onlyoffice", "ext:onlyoffice", "OnlyOffice"),
]
ENV_RE = re.compile(r"-(dev|rc|prod|prd|edge|qa|hotfix|release|sandbox|staging|stg)(?=[.\-/])", re.I)


def module_token(key):
    """revamp-actionplan -> actionplan ; edv-solvace-hubspotapi -> hubspot."""
    k = key.lower().replace("revamp-", "").replace("edv-solvace-", "")
    return re.sub(r"api$", "", k) or k


def is_infra(key):
    return any(key.endswith(x) for x in INFRA_REPOS)


def relations(facts_list):
    by_key = {f["key"]: f for f in facts_list}
    tokens = {k: module_token(k) for k in by_key}
    token_to_key = {t: k for k, t in tokens.items()}
    out = defaultdict(list)
    warnings = []

    def add(src, tgt, kind, detail, evidence):
        if src == tgt or not tgt:
            return
        if not any(r["target"] == tgt and r["kind"] == kind and r["detail"] == detail for r in out[src]):
            out[src].append({"target": tgt, "kind": kind, "detail": detail, "evidence": evidence})

    def owner_by_token(name):
        n = name.lower().replace("_", "")
        best = None
        for k, t in tokens.items():
            if is_infra(k) or len(t) < 3:
                continue
            if n == t or n.startswith(t) or (len(n) >= 4 and t.startswith(n)):
                if best is None or len(t) > len(tokens[best]):
                    best = k
        return best

    # Siglas aprendidas das filas das Lambdas (ACP_*, DFT_*, UNC_*...).
    prefix_owner = {p: token_to_key.get(m) for p, m in PREFIX_OWNER.items() if token_to_key.get(m)}
    for f in facts_list:
        for l in f["lambdas"]:
            q = l.get("queue") or ""
            if q and not is_infra(f["key"]):
                prefix_owner.setdefault(q.split("_")[0], f["key"])

    # Eventos: consumidor (Lambda com SNS_NAME) -> dono do topico.
    for f in facts_list:
        for l in f["lambdas"]:
            topic = (l.get("topic") or "").upper()
            if not topic:
                continue
            producer = owner_by_token(topic)
            if producer:
                add(f["key"], producer, "event", f"consome o tópico {topic} (fila {l.get('queue') or '?'})", f"{f['repo']}/{l['file']}")

    # Lambdas com o mesmo nome em repositorios diferentes = um deploy sobrescreve o outro.
    names = defaultdict(list)
    for f in facts_list:
        for l in f["lambdas"]:
            if l.get("name") and not l.get("schedule"):
                names[l["name"]].append(f["key"])
    for n, ks in names.items():
        if len(set(ks)) > 1:
            warnings.append({"kind": "duplicate-lambda", "detail": f"Lambda `{n}` declarada em {', '.join(sorted(set(ks)))} — um deploy sobrescreve o outro", "projects": sorted(set(ks))})

    # Filas enviadas: dono pelo primeiro token (NOTIFICATION_WORKER_* -> notification); _LOCAL e ambiente unificados.
    for f in facts_list:
        for q, ev in f["queuesSent"].items():
            base = re.sub(r"_LOCAL$", "", q)
            owner = owner_by_token(base.split("_")[0])
            if owner:
                add(f["key"], owner, "queue", f"envia para a fila {base}_<amb>", f"{f['repo']}/{ev}")

    # Banco: tabela de outro modulo pela sigla TB_<SIGLA>_ (dono explicito; tabelas globais WCM/SYS nao contam).
    for f in facts_list:
        seen = set()
        for t, n in f["tables"].items():
            m = re.match(r"TB_([A-Z0-9]+)_", t)
            if not m or m.group(1) in SHARED_GLOBAL or m.group(1) in seen:
                continue
            owner = prefix_owner.get(m.group(1))
            if owner and owner != f["key"]:
                seen.add(m.group(1))
                add(f["key"], owner, "database", f"usa tabelas TB_{m.group(1)}_* (ex.: {t})", f"{f['repo']}/{f['tableEvidence'].get(t, '')}")

    # HTTP entre modulos (so pelo host: api-<modulo>-<amb> / apigw.../<modulo>) e servicos externos.
    for f in facts_list:
        for c in f["configUrls"] + f.get("codeUrls", []):
            val = c["value"].lower()
            host = re.sub(r"^https?://", "", val).split("/")[0]
            norm = ENV_RE.sub("-<amb>", val)
            hit = False
            for k, t in tokens.items():
                if k != f["key"] and len(t) >= 3 and (re.search(rf"(^|[.\-]){re.escape(t)}([.\-]|$)", host.replace("api-", "-")) or re.search(rf"apigw[^/]*/{re.escape(t)}\b", val)):
                    add(f["key"], k, "http", f"chama {norm} ({c['key']})", f"{f['repo']}/{c['file']}")
                    hit = True
            if not hit:
                for rx, ext, label in EXTERNAL_HOSTS:
                    if re.search(rx, val):
                        add(f["key"], ext, "external", f"{label}: {norm} ({c['key']})", f"{f['repo']}/{c['file']}")
                        break

    # Pacotes de outro modulo (Solvace.<Modulo>.*, fora BuildingBlocks).
    for f in facts_list:
        for pk in f["packages"]:
            parts = pk.split(".")
            if len(parts) > 1 and parts[1] != "BuildingBlocks":
                owner = owner_by_token(parts[1])
                if owner and owner != f["key"]:
                    add(f["key"], owner, "package", f"usa o pacote {pk}", f"{f['repo']}/*.csproj")
    result = {k: v for k, v in out.items()}
    result["_warnings"] = warnings
    return result


# ── Secoes ──────────────────────────────────────────────────────────────────────────────────────────────────────────
KIND_LABEL = {"event": "Evento (SNS → fila)", "queue": "Fila (SQS)", "database": "Banco compartilhado", "http": "HTTP", "package": "Pacote", "external": "Serviço externo"}


def sections(facts, rels_all, outdir, name=None, kind="revamp", order=100, summary_extra="", wiki_desc=""):
    os.makedirs(outdir, exist_ok=True)
    key = facts["key"]
    mine = rels_all.get(key, [])
    incoming = [(src, r) for src, rs in rels_all.items() if not src.startswith("_") for r in rs if r["target"] == key]
    warns = [w for w in rels_all.get("_warnings", []) if key in w.get("projects", [])]
    api_projects = [p for p in facts["projects"] if p["type"] == "api"]
    fws = sorted({p["framework"] for p in facts["projects"] if p["framework"]})
    top_tables = list(facts["tables"])[:12]
    uc = facts["useCases"]
    routes = sum(len(c["actions"]) for c in facts["controllers"])

    # projeto.json
    deps = sorted({r["target"] for r in mine})
    used_by = sorted({s for s, _ in incoming})
    empty = not facts["projects"] and not facts["files"]["cs"]
    # Resumo curto (entra no INDEX.md, lido em todo card): 1a frase da descricao + numeros. Dependencias ficam em
    # "relations" (o indice ja mostra "Depende de/Usado por"); a descricao completa vai para a secao 010.
    first = re.split(r"(?<=[.!?])\s", wiki_desc.strip(), maxsplit=1)[0][:260] if wiki_desc else ""
    summary = " ".join(filter(None, [
        summary_extra,
        first,
        "Repositório sem código de aplicação (só README/template ou documentação)." if empty else
        f"{len(facts['projects'])} projetos ({', '.join(fws) or 'framework ?'}), {len(facts['controllers'])} controllers/{routes} rotas, "
        f"{len(uc)} casos de uso, {len(facts['lambdas'])} Lambdas" + (f"; tabelas {', '.join(top_tables[:5])}." if top_tables else "."),
    ]))[:600]
    words = []  # ordem = prioridade (modulo, controllers, tabelas, casos de uso)
    token = module_token(key)
    words += [token, facts["repo"]]
    words += [c["name"].replace("Controller", "") for c in facts["controllers"][:8]]
    words += top_tables[:4]
    words += [u.split("/")[-1] for u in uc[:6]]
    meta = {"name": name or facts["repo"], "kind": kind, "order": order, "repository": facts["remote"] or None,
            "repoDir": f"kb-mirror/{facts['repo']}", "summary": summary, "keywords": list(dict.fromkeys(w for w in words if w))[:15],
            "relations": [{"target": r["target"], "kind": r["kind"], "detail": r["detail"], "evidence": r["evidence"]} for r in mine]}
    json.dump(meta, open(os.path.join(outdir, "projeto.json"), "w"), ensure_ascii=False, indent=1)

    # 010 visao geral
    lines = [f"<!-- gerado por mapear.py a partir de {facts['repo']}@{facts['commit'][:8]} ({facts['lastCommitDate']}) -->", ""]
    if facts["readme"]:
        lines += [f"> {facts['readme']}", ""]
    if wiki_desc:
        lines += ["## Descrição (revamp-wiki)", wiki_desc, ""]
    lines += ["## Projetos", "| Projeto | Tipo | Framework |", "|---|---|---|"]
    for p in sorted(facts["projects"], key=lambda x: (x["type"] == "test", x["name"])):
        lines.append(f"| `{p['name']}` | {p['type']} | {p['framework']} |")
    if facts["dbTiers"]:
        lines += ["", f"**Camadas de banco:** {', '.join(facts['dbTiers'])}"]
    bb = sorted(k.replace("Solvace.BuildingBlocks.", "") for k in facts["packages"] if k.startswith("Solvace.BuildingBlocks."))
    if bb:
        lines += ["", f"**Building blocks:** {', '.join(bb)}"]
    others = sorted(k for k in facts["packages"] if not k.startswith("Solvace.BuildingBlocks."))
    if others:
        lines += ["", f"**Outros pacotes Solvace:** {', '.join(others)}"]
    if facts["workflows"]:
        lines += ["", f"**CI (GitHub Actions):** {', '.join(facts['workflows'])}"]
    lines += ["", f"Arquivos: {facts['files']['cs']} C#, {facts['files']['sql']} SQL · commit `{facts['commit'][:8]}` ({facts['branch']}, {facts['lastCommitDate']})"]
    open(os.path.join(outdir, "010-visao-geral.md"), "w").write("\n".join(lines) + "\n")

    # 020 modulos e fluxos
    if facts["controllers"] or uc or facts["hubs"]:
        lines = ["## Endpoints HTTP"]
        for c in sorted(facts["controllers"], key=lambda x: x["name"]):
            acts = " · ".join(f"`{a['verb']} {a['route'] or '/'}`" for a in c["actions"][:14])
            more = f" … (+{len(c['actions']) - 14})" if len(c["actions"]) > 14 else ""
            lines.append(f"- **{c['name']}** (`{c['route'] or '-'}`, {c['file']}): {acts}{more}")
        if uc:
            groups = defaultdict(list)
            for u in uc:
                g, n = u.split("/", 1)
                groups[g].append(n)
            lines += ["", "## Casos de uso"]
            for g, items in groups.items():
                lines.append(f"- **{g}**: {', '.join(items[:60])}{' …' if len(items) > 60 else ''}")
        if facts["hubs"]:
            lines += ["", "## Tempo real (SignalR)"] + [f"- `{h['name']}` ({h['file']})" for h in facts["hubs"]]
        open(os.path.join(outdir, "020-modulos.md"), "w").write("\n".join(lines) + "\n")

    # 030 dados
    if facts["tables"] or facts["entities"]:
        lines = []
        if facts["tables"]:
            lines += ["## Tabelas referenciadas (mais usadas primeiro)", "| Tabela | Ocorrências | Onde aparece |", "|---|---|---|"]
            for t, n in list(facts["tables"].items())[:40]:
                lines.append(f"| `{t}` | {n} | {facts['tableEvidence'].get(t, '')} |")
        if facts["entities"]:
            lines += ["", f"## Entidades / DbSets", ", ".join(f"`{e}`" for e in facts["entities"][:80])]
        open(os.path.join(outdir, "030-dados.md"), "w").write("\n".join(lines) + "\n")

    # 040 integracoes
    lines = ["## Depende de" if mine else "## Depende de", ""]
    if mine:
        by_kind = defaultdict(list)
        for r in mine:
            by_kind[r["kind"]].append(r)
        for k, rs in by_kind.items():
            lines.append(f"**{KIND_LABEL.get(k, k)}**")
            lines += [f"- `{r['target']}` — {r['detail']} ({r['evidence']})" for r in rs[:25]]
            lines.append("")
    else:
        lines.append("_Nenhuma dependência de outro módulo encontrada no código/configuração._")
    lines += ["", "## Usado por", ""]
    if incoming:
        for src, r in incoming[:40]:
            lines.append(f"- `{src}` — {KIND_LABEL.get(r['kind'], r['kind'])}: {r['detail']}")
    else:
        lines.append("_Nenhum outro módulo mapeado depende deste._")
    if facts["queuesSent"]:
        lines += ["", "## Filas/tópicos citados no código"] + [f"- `{q}` ({ev})" for q, ev in list(facts["queuesSent"].items())[:30]]
    ext = [c for c in facts["configUrls"] if not any(x in c["value"] for x in ("localhost", "127.0.0.1"))]
    if ext:
        lines += ["", "## URLs de configuração"] + [f"- `{c['key']}` = {c['value']} ({c['file']})" for c in ext[:25]]
    if warns:
        lines += ["", "## ⚠️ Atenção"] + [f"- {w['detail']}" for w in warns]
    if facts["httpClients"]:
        lines += ["", "## Clientes HTTP nomeados"] + [f"- `{h['name']}` ({h['file']})" for h in facts["httpClients"][:20]]
    open(os.path.join(outdir, "040-integracoes.md"), "w").write("\n".join(lines) + "\n")

    # 070 jobs
    if facts["lambdas"]:
        lines = ["## Lambdas", "| Função | Gatilho | O que faz |", "|---|---|---|"]
        for l in facts["lambdas"]:
            trig = l.get("schedule") or (f"tópico {l['topic']} → fila {l['queue']}" if l.get("topic") else (f"fila {l['queue']}" if l.get("queue") else "—"))
            lines.append(f"| `{l['name']}` | {trig} | {l['description'].replace('|', '/')} |")
        open(os.path.join(outdir, "070-jobs.md"), "w").write("\n".join(lines) + "\n")
    return meta


INFRA_KIND = {"revamp-buildingblocks", "revamp-moduleintegration", "revamp-datalake", "revamp-api-infra",
              "revamp-api-gateway-infra", "revamp-audittrail", "revamp-wiki"}


def display_name(repo):
    base = repo.replace("revamp-", "").replace("edv-solvace-", "")
    spaced = re.sub(r"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ", base).replace("-", " ").strip()
    if repo.startswith("revamp-"):
        return f"Revamp — {spaced}" if repo.lower() not in INFRA_KIND else f"Revamp infra — {spaced}"
    return spaced


def wiki_descriptions(src):
    """Paragrafo '## Descrição' de cada pagina do revamp-wiki (base mantida por LLM no proprio GitHub), por modulo."""
    base = os.path.join(src, "revamp-wiki", "wiki", "modules")
    out = {}
    if not os.path.isdir(base):
        return out
    for f in os.listdir(base):
        if not f.endswith(".md"):
            continue
        m = re.search(r"^##\s*Descri[çc][ãa]o\s*\n(.+?)(?=\n##|\Z)", read(os.path.join(base, f)), re.S | re.M)
        if m:
            text = re.sub(r"\[\[([^\]|]+)(?:\|[^\]]+)?\]\]", r"`\1`", m.group(1)).strip()
            out[f[:-3].lower()] = re.sub(r"\s+", " ", text)
    return out


def run_all(src, out, curated=None):
    import shutil
    wiki = wiki_descriptions(src)
    work = os.path.join(out, "_fatos")
    os.makedirs(work, exist_ok=True)
    repos = sorted(d for d in os.listdir(src) if os.path.isdir(os.path.join(src, d, ".git")))
    facts = []
    for r in repos:
        f = extract(os.path.join(src, r), r.lower())
        json.dump(f, open(os.path.join(work, f"{r}.json"), "w"), ensure_ascii=False, indent=1)
        facts.append(f)
    rels = relations(facts)
    json.dump(rels, open(os.path.join(out, "_relacoes.json"), "w"), ensure_ascii=False, indent=1)
    for i, f in enumerate(facts):
        key = f["key"]
        kind = "infra" if key in INFRA_KIND else ("integration" if "hubspot" in key else "revamp")
        dest = os.path.join(out, key)
        tmp = dest + ".gerado"
        shutil.rmtree(tmp, ignore_errors=True)
        meta = sections(f, rels, tmp, name=display_name(f["repo"]), kind=kind, order=100 + i,
                        wiki_desc=wiki.get(module_token(key), ""),
                        summary_extra=("Wiki do revamp mantida por LLM (padrão LLM Wiki): uma página por módulo em wiki/modules "
                                       "(descrição, building blocks e versões, eventos) e por building block. Fonte da descrição "
                                       "dos módulos desta base; pode estar atrasada em relação ao código.") if key == "revamp-wiki" else "")
        cur = os.path.join(curated, key) if curated else None
        shutil.rmtree(dest, ignore_errors=True)
        if cur and os.path.isdir(cur):
            shutil.copytree(cur, dest)
            cmeta = json.load(open(os.path.join(dest, "projeto.json")))
            cmeta["relations"] = meta["relations"]
            json.dump(cmeta, open(os.path.join(dest, "projeto.json"), "w"), ensure_ascii=False, indent=1)
            have = {re.sub(r"^\d{3}-", "", x) for x in os.listdir(dest) if x.endswith(".md")}
            for x in os.listdir(tmp):
                if x.endswith(".md") and re.sub(r"^\d{3}-", "", x) not in have:
                    shutil.copy(os.path.join(tmp, x), os.path.join(dest, x))
            shutil.rmtree(tmp)
        else:
            os.rename(tmp, dest)
    # Projetos revisados que nao sao repositorios desta pasta (ecossistema, legado, infra...) entram como estao.
    if curated:
        for d in os.listdir(curated):
            if os.path.isdir(os.path.join(curated, d)) and not os.path.exists(os.path.join(out, d)):
                shutil.copytree(os.path.join(curated, d), os.path.join(out, d))
    return len(facts), sum(len(v) for k, v in rels.items() if not k.startswith("_")), rels.get("_warnings", [])


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1
    cmd = sys.argv[1]
    args = sys.argv[2:]

    def opt(name, default=None):
        return args[args.index(name) + 1] if name in args else default

    if cmd == "fatos":
        f = extract(args[0], opt("--key"))
        out = opt("--out")
        (open(out, "w") if out else sys.stdout).write(json.dumps(f, ensure_ascii=False, indent=1))
        return 0
    if cmd == "relacoes":
        folder = args[0]
        fl = [json.load(open(os.path.join(folder, x))) for x in sorted(os.listdir(folder)) if x.endswith(".json")]
        r = relations(fl)
        out = opt("--out")
        (open(out, "w") if out else sys.stdout).write(json.dumps(r, ensure_ascii=False, indent=1))
        return 0
    if cmd == "tudo":
        n, r, w = run_all(args[0], args[1], opt("--curados"))
        print(f"{n} repositorios · {r} relacoes · {len(w)} avisos" + "".join(f"\n  AVISO: {x['detail']}" for x in w))
        return 0
    if cmd == "secoes":
        f = json.load(open(args[0]))
        r = json.load(open(args[1]))
        sections(f, r, args[2], name=opt("--name"), kind=opt("--kind", "revamp"), order=int(opt("--order", "100")),
                 summary_extra=opt("--summary", ""))
        return 0
    print(__doc__)
    return 1


if __name__ == "__main__":
    sys.exit(main())
