#!/usr/bin/env python3
"""Ferramentas locais da engenharia reversa (feature 0052) - sem LLM, sem rede.

  re_tool.py inventario <saida.json> <papel>=<pasta> [<papel>=<pasta> ...]
      Inventario deterministico do codigo do modulo: endpoints, validacoes/mensagens, permissoes, tabelas, eventos/filas,
      jobs, configuracoes, rotas/componentes/chamadas HTTP do front, paginas ASP. E a lista do que o documento PRECISA
      cobrir ("todas as regras, sem excecao"). papel: backend | frontend | database | other.
  re_tool.py cobertura <inventario.json> <documento.md> <tipo-doc> [--json saida.json] [--max 80]
      Quanto do inventario o documento cita (por nome, rota, mensagem, tabela ou arquivo:linha) e o que falta.
  re_tool.py ids <pasta-ou-arquivos...>
      IDs definidos (### RN-012 ...) em varios documentos locais - para nao repetir ID entre documentos da mesma sessao.
  re_tool.py juntar <saida.md> <parte1.md> [parte2.md ...]
      Junta partes escritas por subagentes: mesmas secoes (##) viram uma so, na ordem da primeira parte; IDs repetidos
      sao avisados.
"""
import json
import os
import re
import sys
import unicodedata
from collections import OrderedDict, defaultdict

SKIP_DIRS = {".git", "node_modules", "bin", "obj", "dist", "build", ".angular", ".vs", ".idea", "coverage", "packages", "wwwroot/lib",
             "TestResults", "__pycache__", ".venv", "venv", "migrations_backup"}
TEXT_EXT = {".cs", ".cshtml", ".razor", ".asp", ".aspx", ".ascx", ".inc", ".js", ".ts", ".html", ".sql", ".json", ".yml", ".yaml",
            ".config", ".xml", ".vb", ".py"}
MAX_FILE = 1_500_000

# Categorias do inventario e os documentos que precisam cobri-las.
DOC_CATEGORIES = {
    "funcional": ["validacao", "permissao", "endpoint", "enum", "pagina", "handler", "procedure"],
    "arquitetura": ["endpoint", "tabela", "evento", "job", "config", "http-front", "pagina", "handler", "procedure"],
    "uiux": ["rota-front", "componente-front", "http-front", "pagina"],
    "design": ["componente-front"],
    "visao": [],
    "spec-arquitetura": ["evento", "job"],
}

ID_HEADING = re.compile(r"^(#{2,4})\s+\**`?(TELA|PRF|EST|NTF|CFG|REL|TEC|CMP|API|EVT|JOB|INT|FLX|OBJ|PER|GLO|ADR|NFR|SEQ|GAP|FN|UC|RN|DB|UI)-(\d{1,4})\b",
                        re.IGNORECASE)


def norm(text):
    text = unicodedata.normalize("NFD", text or "")
    text = "".join(c for c in text if unicodedata.category(c) != "Mn").lower()
    return re.sub(r"[^a-z0-9_/.:-]+", " ", text).strip()


def walk(root):
    for base, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.startswith(".")]
        rel_base = os.path.relpath(base, root)
        if any(part in SKIP_DIRS for part in rel_base.split(os.sep)):
            continue
        for name in files:
            ext = os.path.splitext(name)[1].lower()
            if ext not in TEXT_EXT or name.endswith((".min.js", ".spec.ts", ".d.ts", ".map")) or "Designer" in name:
                continue
            path = os.path.join(base, name)
            try:
                if os.path.getsize(path) > MAX_FILE:
                    continue
                with open(path, encoding="utf-8", errors="replace") as fh:
                    yield path, fh.read()
            except OSError:
                continue


def line_of(text, index):
    return text.count("\n", 0, index) + 1


# ── extratores ───────────────────────────────────────────────────────────────────────────────

R_CLASS_ROUTE = re.compile(r'\[Route\("([^"]*)"\)\]\s*(?:\[[^\]]*\]\s*)*public\s+(?:partial\s+)?class\s+(\w+)')
R_HTTP = re.compile(r'\[Http(Get|Post|Put|Delete|Patch)(?:\(\s*(?:"([^"]*)")?[^\]]*\))?\]\s*(?:\[[^\]]*\]\s*)*public\s+[\w<>\[\],\s?.]+?\s+(\w+)\s*\(')
R_MINIMAL = re.compile(r'\.Map(Get|Post|Put|Delete|Patch)\(\s*"([^"]+)"')
R_MVC_ACTION = re.compile(r'public\s+(?:async\s+)?(?:Task<)?(?:IActionResult|ActionResult|JsonResult|ViewResult|PartialViewResult)[^\s(]*\s+(\w+)\s*\(')
R_THROW = re.compile(r'throw\s+new\s+(\w*Exception)\s*\(\s*\$?@?"((?:[^"\\]|\\.){4,300})"')
R_FLUENT = re.compile(r'RuleFor\(\s*\w+\s*=>\s*\w+\.([\w.]+)\)(?:(?!RuleFor).){0,600}?WithMessage\(\s*\$?"((?:[^"\\]|\\.){2,300})"', re.S)
R_RULEFOR = re.compile(r'RuleFor\(\s*\w+\s*=>\s*\w+\.([\w.]+)\)')
R_BADREQ = re.compile(r'(?:BadRequest|UnprocessableEntity|Conflict|Forbid|NotFound)\(\s*(?:new\s*\{[^}]*?)?\$?"((?:[^"\\]|\\.){4,300})"')
R_MODELSTATE = re.compile(r'AddModelError\(\s*[^,]+,\s*\$?"((?:[^"\\]|\\.){4,300})"')
R_ATTR_VALID = re.compile(r'\[(Required|MaxLength|StringLength|Range|RegularExpression|EmailAddress|MinLength)[^\]]*\]\s*public\s+[\w<>?\[\]]+\s+(\w+)')
R_ROLES = re.compile(r'\[Authorize\(\s*(?:Roles|Policy)\s*=\s*"([^"]+)"')
R_PERM_CALL = re.compile(r'(?:HasPermission|hasPermission|CheckPermission|VerificaPermissao|verificaPermissao|PermissionCode|permission)\s*\(\s*["\']([\w.:-]{3,80})["\']')
R_TABLE = re.compile(r'\bTB_[A-Z0-9_]{3,}\b')
R_TOTABLE = re.compile(r'(?:ToTable\(\s*"(\w+)"|\[Table\(\s*"(\w+)"\)\]|CREATE\s+TABLE\s+(?:\[?\w+\]?\.)?\[?(\w+)\]?)', re.I)
R_DBSET = re.compile(r'DbSet<(\w+)>\s+(\w+)')
R_ENUM = re.compile(r'\benum\s+(\w+)\s*\{([^}]{0,1500})\}')
R_EVENT_STR = re.compile(r'"([A-Za-z0-9_.:-]*(?:Topic|Queue|QUEUE|TOPIC|_SQS|_SNS|-queue|-topic)[A-Za-z0-9_.:-]*)"')
R_CONSUMER = re.compile(r'class\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*[^{]*\b(I\w*(?:Consumer|Handler|Subscriber)\w*)<\s*(\w+)')
# Handlers de comando/consulta (MediatR e afins) = casos de uso da aplicação; consumidores de fila/evento = eventos.
EVENT_IFACE = re.compile(r"Consumer|Subscriber|EventHandler|NotificationHandler|MessageHandler", re.I)
R_JOB = re.compile(r'(?:class\s+(\w+)\s*:\s*[^{]*\b(?:BackgroundService|IHostedService|IJob|IInvocable)\b|RecurringJob\.AddOrUpdate(?:<[^>]+>)?\(\s*"?([\w.-]+)|public\s+\w+(?:<[^>]+>)?\s+(FunctionHandler)\s*\()')
R_CRON = re.compile(r'"((?:[\d*/,-]+\s+){4,5}[\d*/,?LW#-]+)"')
R_NG_ROUTE = re.compile(r"path\s*:\s*'([^']*)'")
R_NG_COMPONENT = re.compile(r"@Component\(\s*\{[^}]*?selector\s*:\s*'([^']+)'[^}]*\}\s*\)\s*export\s+class\s+(\w+)", re.S)
R_NG_HTTP = re.compile(r"\b(?:this\.)?(?:http|httpClient|_http|_httpClient|api|apiService)\.(get|post|put|delete|patch)(?:<[^>()]*(?:<[^>]*>)?[^>()]*>)?\(\s*([^,)]{1,240})")
R_STR_PART = re.compile(r"[`'\"]([^`'\"]*)[`'\"]")
R_NG_VALIDATOR = re.compile(r"(\w+)\s*:\s*\[[^\]]{0,200}?Validators\.(required|maxLength|minLength|pattern|email|min|max)\b")
R_JS_ALERT = re.compile(r"""\b(?:alert|confirm|msgbox|MsgBox|toastr\.\w+|this\.toast\w*\.\w+|this\.notification\w*\.\w+|swal)\s*\(\s*(["'`])((?:(?!\1).){4,300})\1""")
R_LANG = re.compile(r'GetLanguageByName\(\s*"([^"]{3,300})"\s*\)')
R_ASP_MSG = re.compile(r'\b(?:strMsg|msg|mensagem|strErro|sErro|erro|strMensagem)\s*=\s*"([^"]{6,250})"', re.I)
R_PROC = re.compile(r'\b((?:dbo\.)?(?:sp|usp|SP|USP|PR|PRC|prc|proc|PROC|STP|stp)_[A-Za-z0-9_]{3,})\b')
R_ASP_FORM = re.compile(r'Request\.(?:Form|QueryString)\(\s*"(\w+)"\s*\)', re.I)


def add(items, seen, cat, name, path, root, text, index, detail=None):
    name = (name or "").strip()
    if not name:
        return
    rel = os.path.relpath(path, root)
    key = (cat, name.lower(), rel if cat in ("validacao", "pagina", "componente-front") else "")
    if key in seen:
        return
    seen.add(key)
    item = {"cat": cat, "name": name[:300], "file": rel, "line": line_of(text, index)}
    if detail:
        item["detail"] = detail[:300]
    items.append(item)


def inventory(sources):
    items, seen = [], set()
    files_by_role = defaultdict(int)
    for role, root in sources:
        for path, text in walk(root):
            files_by_role[role] += 1
            ext = os.path.splitext(path)[1].lower()
            if ext == ".cs":
                class_routes = {m.group(2): (m.group(1), m.start()) for m in R_CLASS_ROUTE.finditer(text)}
                prefix = next(iter(class_routes.values()), ("", 0))[0] if class_routes else ""
                controller = os.path.basename(path)[:-3]
                for m in R_HTTP.finditer(text):
                    route = "/".join(x for x in [prefix.replace("[controller]", controller.replace("Controller", "")), m.group(2) or ""] if x)
                    add(items, seen, "endpoint", f"{m.group(1).upper()} {route or controller + '.' + m.group(3)}", path, root, text, m.start(),
                        f"{controller}.{m.group(3)}")
                for m in R_MINIMAL.finditer(text):
                    add(items, seen, "endpoint", f"{m.group(1).upper()} {m.group(2)}", path, root, text, m.start())
                if controller.endswith("Controller") and not R_HTTP.search(text):
                    for m in R_MVC_ACTION.finditer(text):
                        add(items, seen, "endpoint", f"{controller.replace('Controller', '')}/{m.group(1)}", path, root, text, m.start(), "acao MVC")
                for m in R_THROW.finditer(text):
                    add(items, seen, "validacao", m.group(2), path, root, text, m.start(), m.group(1))
                fluent = set()
                for m in R_FLUENT.finditer(text):
                    fluent.add(m.start())
                    add(items, seen, "validacao", m.group(2), path, root, text, m.start(), f"RuleFor {m.group(1)}")
                for m in R_RULEFOR.finditer(text):
                    if m.start() not in fluent:
                        add(items, seen, "validacao", f"RuleFor {m.group(1)}", path, root, text, m.start(), "validador")
                for r in (R_BADREQ, R_MODELSTATE):
                    for m in r.finditer(text):
                        add(items, seen, "validacao", m.group(1), path, root, text, m.start())
                for m in R_ATTR_VALID.finditer(text):
                    add(items, seen, "validacao", f"{m.group(2)} [{m.group(1)}]", path, root, text, m.start(), "atributo")
                for m in R_ROLES.finditer(text):
                    add(items, seen, "permissao", m.group(1), path, root, text, m.start())
                for m in R_DBSET.finditer(text):
                    add(items, seen, "tabela", m.group(1), path, root, text, m.start(), f"DbSet {m.group(2)}")
                for m in R_ENUM.finditer(text):
                    values = re.findall(r"\b([A-Z]\w*)\b", m.group(2))
                    add(items, seen, "enum", m.group(1), path, root, text, m.start(), ", ".join(values[:20]))
                for m in R_CONSUMER.finditer(text):
                    cat = "evento" if EVENT_IFACE.search(m.group(2)) else "handler"
                    add(items, seen, cat, m.group(3), path, root, text, m.start(), f"{m.group(1)} ({m.group(2)})")
                for m in R_JOB.finditer(text):
                    add(items, seen, "job", m.group(1) or m.group(2) or f"{os.path.basename(path)[:-3]}.FunctionHandler", path, root, text, m.start())
            if ext in (".cs", ".json", ".yml", ".yaml", ".config", ".ts", ".js") and os.path.basename(path) != "launchSettings.json":
                for m in R_EVENT_STR.finditer(text):
                    value = m.group(1)
                    # nome de projeto/namespace (Solvace.X.Queue.Worker) nao e fila
                    if value.startswith(("http", "Solvace.", "solvace.")) or value.count(".") >= 2:
                        continue
                    add(items, seen, "evento", value, path, root, text, m.start())
                for m in R_CRON.finditer(text):
                    add(items, seen, "job", f"cron {m.group(1)}", path, root, text, m.start())
            if ext in (".cs", ".sql", ".asp", ".inc", ".cshtml", ".vb", ".js", ".ts", ".aspx"):
                for m in R_TABLE.finditer(text):
                    add(items, seen, "tabela", m.group(0), path, root, text, m.start())
            if ext in (".cs", ".sql"):
                for m in R_TOTABLE.finditer(text):
                    add(items, seen, "tabela", next(g for g in m.groups() if g), path, root, text, m.start())
            if ext in (".cs", ".ts", ".js", ".asp", ".inc", ".cshtml"):
                for m in R_PERM_CALL.finditer(text):
                    add(items, seen, "permissao", m.group(1), path, root, text, m.start())
            if ext in (".ts", ".js", ".html", ".asp", ".inc", ".cshtml", ".aspx"):
                for m in R_JS_ALERT.finditer(text):
                    msg = m.group(2)
                    lang = R_LANG.search(msg)
                    if lang:  # legado: alert("<%=GetLanguageByName("texto")%>") -> o texto
                        msg = lang.group(1)
                    if re.search(r"[A-Za-zÀ-ú]{3,}", msg) and not msg.startswith(("http", "/", "#", ".")):
                        add(items, seen, "validacao", msg, path, root, text, m.start(), "mensagem na tela")
            if ext == ".ts":
                if path.endswith((".routes.ts", "-routing.module.ts", "routing.ts", "app.routes.ts")):
                    for m in R_NG_ROUTE.finditer(text):
                        if m.group(1) not in ("", "**"):
                            add(items, seen, "rota-front", m.group(1), path, root, text, m.start())
                for m in R_NG_COMPONENT.finditer(text):
                    add(items, seen, "componente-front", m.group(2), path, root, text, m.start(), m.group(1))
                for m in R_NG_HTTP.finditer(text):
                    parts = R_STR_PART.findall(m.group(2))
                    url = re.sub(r"\$\{[^}]*\}", "{}", "".join(parts) if parts else m.group(2).strip())
                    add(items, seen, "http-front", f"{m.group(1).upper()} {url}", path, root, text, m.start())
                for m in R_NG_VALIDATOR.finditer(text):
                    add(items, seen, "validacao", f"{m.group(1)} [Validators.{m.group(2)}]", path, root, text, m.start(), "formulario")
            if ext in (".asp", ".inc", ".aspx", ".vb"):
                for m in R_ASP_MSG.finditer(text):
                    msg = R_LANG.search(m.group(1)).group(1) if R_LANG.search(m.group(1)) else m.group(1)
                    if re.search(r"[A-Za-zÀ-ú]{3,}\s+[A-Za-zÀ-ú]{2,}", msg):
                        add(items, seen, "validacao", msg, path, root, text, m.start(), "mensagem (VBScript)")
            if ext in (".asp", ".inc", ".aspx", ".cs", ".sql", ".vb"):
                for m in R_PROC.finditer(text):
                    add(items, seen, "procedure", m.group(1).replace("dbo.", ""), path, root, text, m.start())
            if ext in (".asp", ".aspx"):
                add(items, seen, "pagina", os.path.basename(path), path, root, text, 0,
                    ", ".join(sorted(set(R_ASP_FORM.findall(text)))[:15]) or None)
            if os.path.basename(path).lower().startswith("appsettings") and ext == ".json":
                try:
                    data = json.loads(re.sub(r"//.*", "", text))
                except ValueError:
                    data = None
                for key in flat_keys(data):  # so o NOME da chave — valores nunca entram no inventario
                    add(items, seen, "config", key, path, root, text, 0)
    counts = defaultdict(int)
    for it in items:
        counts[it["cat"]] += 1
    return {"version": 1, "sources": [{"role": r, "path": p} for r, p in sources], "files": dict(files_by_role), "counts": dict(counts),
            "items": items}


def flat_keys(data, prefix=""):
    if isinstance(data, dict):
        for k, v in data.items():
            key = f"{prefix}:{k}" if prefix else k
            if isinstance(v, dict):
                yield from flat_keys(v, key)
            else:
                yield key


# ── cobertura ────────────────────────────────────────────────────────────────────────────────

R_EVIDENCE = re.compile(r"([\w./\\-]+\.(?:cs|cshtml|razor|asp|aspx|ascx|inc|js|ts|html|sql|json|ya?ml|xml|config|vb|py)):(\d+)(?:-(\d+))?")


GENERIC = {"get", "list", "post", "put", "delete", "patch", "create", "update", "upsert", "index", "search", "find", "save", "remove", "add",
           "edit", "details", "detail", "export", "import", "download", "upload", "getall", "getbyid", "filter", "count", "exists", "all"}
# Nomes que so contam quando aparecem "fortes" no documento: cabecalho, metadado (**...**), linha de tabela ou `codigo`.
STRONG_CATS = {"tabela", "enum", "handler", "componente-front", "config", "job", "evento", "procedure"}


def needles(item):
    """Formas do item que, citadas no documento, contam como cobertura."""
    name = item["name"]
    cat = item["cat"]
    detail = item.get("detail") or ""
    out = []
    if cat == "endpoint":
        verb, _, raw = name.partition(" ") if " " in name else ("", "", name)
        route = re.sub(r"\{[^}]*\}", "", raw).strip("/")
        # rota de um segmento ("Behavior") e ambigua: so conta com o verbo ("POST Behavior")
        if "/" in route and len(route) > 3:
            out.append(norm(route))
        elif route:
            out.append(norm(f"{verb} {route}"))
        action = detail.split(".")[-1] if "." in detail else ""
        if len(action) >= 6 and action.lower() not in GENERIC:
            out.append(norm(action))
    elif cat == "validacao":
        if name.startswith("RuleFor "):
            out.append(norm(name[8:]))
        else:
            n = norm(re.sub(r"\{[^}]*\}", " ", re.sub(r"\s*\[(Validators\.\w+|Required|MaxLength|StringLength|Range|RegularExpression|EmailAddress|MinLength)\]", "", name)))
            out.append(n[:40] if len(n) > 40 else n)
    elif cat == "http-front":
        url = re.sub(r"\{[^}]*\}", "", name.split(" ", 1)[-1]).strip("/")
        if len(url) > 3:
            out.append(norm(url))
    elif cat == "config":
        leaf = name.split(":")[-1]
        out.append(norm(leaf) if len(leaf) > 5 else norm(name))
    elif cat == "pagina":
        out += [norm(name), norm(os.path.splitext(name)[0])]
    elif cat == "handler":
        out.append(norm(re.sub(r"(Query|Command)?(Queries)?(Request|Handler)$", "", name)))
    else:
        out.append(norm(name))
        if cat in ("componente-front",) and detail:
            out.append(norm(detail))
        if cat == "tabela" and detail.startswith("DbSet "):
            out.append(norm(detail[6:]))
    return [n for n in out if len(n) >= 3]


def strong_text(doc):
    lines = []
    for line in doc.splitlines():
        t = line.strip()
        if t.startswith(("#", "|")) or "**" in t:
            lines.append(t)
        lines += re.findall(r"`([^`]+)`", t)
    return " \n ".join(lines)


def contains(haystack, needle):
    return re.search(r"(?<![a-z0-9_])" + re.escape(needle) + r"(?![a-z0-9_])", haystack) is not None


def coverage(inv, doc_text, doc_type, max_missing):
    cats = DOC_CATEGORIES.get(doc_type, [])
    doc_norm = norm(doc_text)
    strong_norm = norm(strong_text(doc_text))
    evidence = defaultdict(list)
    for m in R_EVIDENCE.finditer(doc_text):
        start = int(m.group(2))
        end = int(m.group(3) or start)
        evidence[os.path.basename(m.group(1)).lower()].append((start, end))
    total = covered = 0
    missing = []
    by_cat = OrderedDict()
    for it in inv["items"]:
        if it["cat"] not in cats:
            continue
        total += 1
        c = by_cat.setdefault(it["cat"], {"total": 0, "covered": 0})
        c["total"] += 1
        text = strong_norm if it["cat"] in STRONG_CATS else doc_norm
        hit = any(contains(text, n) for n in needles(it))
        if not hit:
            base = os.path.basename(it["file"]).lower()
            hit = any(s - 5 <= it["line"] <= e + 5 for s, e in evidence.get(base, []))
        if hit:
            covered += 1
            c["covered"] += 1
        else:
            missing.append(it)
    ratio = None if total == 0 else round(covered / total, 4)
    return {"docType": doc_type, "total": total, "covered": covered, "ratio": ratio, "byCategory": by_cat,
            "missing": missing[:max_missing], "missingCount": len(missing)}


# ── ids e juntar ─────────────────────────────────────────────────────────────────────────────

def doc_ids(text):
    out, fence = [], False
    for line in text.splitlines():
        if line.lstrip().startswith("```") and (fence or line.lstrip().find("```", 3) < 0):
            fence = not fence
            continue
        if fence:
            continue
        m = ID_HEADING.match(line)
        if m:
            out.append(f"{m.group(2).upper()}-{int(m.group(3)):03d}")
    return out


def join_parts(output, parts):
    sections = OrderedDict()
    preamble = []
    ids = defaultdict(list)
    for part in parts:
        with open(part, encoding="utf-8") as fh:
            text = fh.read()
        for i in doc_ids(text):
            ids[i].append(os.path.basename(part))
        current = None
        fence = False
        touched = []
        for line in text.splitlines():
            if line.lstrip().startswith("```") and (fence or line.lstrip().find("```", 3) < 0):
                fence = not fence
            if not fence and re.match(r"^##\s+\S", line) and not re.match(r"^###", line):
                title = line.strip()
                key = norm(re.sub(r"^##\s+", "", title))
                current = sections.setdefault(key, {"title": title, "lines": []})
                touched.append(current)
                continue
            if current is None:
                if not preamble or line.strip() or preamble[-1].strip():
                    if part == parts[0]:
                        preamble.append(line)
            else:
                current["lines"].append(line)
        for section in touched:
            section["lines"].append("")
    with open(output, "w", encoding="utf-8") as fh:
        fh.write("\n".join(preamble).rstrip() + "\n\n")
        for s in sections.values():
            body = "\n".join(s["lines"]).strip()
            fh.write(f"{s['title']}\n\n{body}\n\n" if body else f"{s['title']}\n\n")
    dups = {k: v for k, v in ids.items() if len(v) > 1}
    return dups


def main(argv):
    if len(argv) < 2 or argv[1] in ("-h", "--help"):
        print(__doc__)
        return 0
    cmd = argv[1]
    if cmd == "inventario":
        if len(argv) < 4:
            print("uso: re_tool.py inventario <saida.json> <papel>=<pasta> ...", file=sys.stderr)
            return 2
        sources = []
        for spec in argv[3:]:
            role, _, path = spec.partition("=")
            if not path or not os.path.isdir(path):
                print(f"ERRO: pasta inexistente: {spec}", file=sys.stderr)
                return 2
            sources.append((role or "backend", os.path.abspath(path)))
        inv = inventory(sources)
        with open(argv[2], "w", encoding="utf-8") as fh:
            json.dump(inv, fh, ensure_ascii=False, indent=1)
        print(f"Inventario: {sum(inv['counts'].values())} itens em {sum(inv['files'].values())} arquivos -> {argv[2]}")
        for cat, n in sorted(inv["counts"].items(), key=lambda x: -x[1]):
            print(f"  {cat}: {n}")
        return 0
    if cmd == "cobertura":
        if len(argv) < 5:
            print("uso: re_tool.py cobertura <inventario.json> <documento.md> <tipo-doc> [--json saida] [--max N]", file=sys.stderr)
            return 2
        with open(argv[2], encoding="utf-8") as fh:
            inv = json.load(fh)
        with open(argv[3], encoding="utf-8") as fh:
            doc = fh.read()
        max_missing = int(argv[argv.index("--max") + 1]) if "--max" in argv else 80
        result = coverage(inv, doc, argv[4], 2000)
        if "--json" in argv:
            with open(argv[argv.index("--json") + 1], "w", encoding="utf-8") as fh:
                json.dump(result, fh, ensure_ascii=False, indent=1)
        if result["ratio"] is None:
            print(f"Cobertura: o documento '{argv[4]}' nao tem itens de inventario a cobrir.")
            return 0
        print(f"Cobertura do inventario: {result['covered']}/{result['total']} = {result['ratio']:.0%}")
        for cat, c in result["byCategory"].items():
            print(f"  {cat}: {c['covered']}/{c['total']}")
        if result["missingCount"]:
            print(f"Faltando ({result['missingCount']}; mostrando {min(max_missing, result['missingCount'])}) - cite no documento (regra, endpoint, tela...) com o arquivo:linha:")
            for it in result["missing"][:max_missing]:
                print(f"  [{it['cat']}] {it['name'][:110]} — {it['file']}:{it['line']}" + (f" ({it['detail'][:60]})" if it.get("detail") else ""))
        return 0
    if cmd == "ids":
        files = []
        for a in argv[2:]:
            if os.path.isdir(a):
                for base, _, names in os.walk(a):
                    files += [os.path.join(base, n) for n in names if n.endswith(".md")]
            else:
                files.append(a)
        seen = defaultdict(list)
        for f in files:
            with open(f, encoding="utf-8") as fh:
                for i in doc_ids(fh.read()):
                    seen[i].append(f)
        for i in sorted(seen, key=lambda x: (x.split("-")[0], int(x.split("-")[1]))):
            flag = "  <- REPETIDO" if len(set(seen[i])) > 1 or len(seen[i]) > 1 else ""
            print(f"{i}\t{', '.join(sorted(set(os.path.basename(p) for p in seen[i])))}{flag}")
        return 0
    if cmd == "juntar":
        if len(argv) < 4:
            print("uso: re_tool.py juntar <saida.md> <parte1.md> [parte2.md ...]", file=sys.stderr)
            return 2
        dups = join_parts(argv[2], argv[3:])
        print(f"Juntado em {argv[2]}")
        for i, where in dups.items():
            print(f"  AVISO: {i} definido em {', '.join(where)} — renumere numa das partes")
        return 1 if dups else 0
    print(f"comando desconhecido: {cmd}", file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
