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
  re_tool.py termos <inventario.json> [--banco <pasta-banco>] [--exclusoes '<json>'] --out <inventario-termos.json>
      Termos do modulo para o GLOSSARIO (0053): rotulos da tela (GetLanguageByName do legado, i18n do front), menus e
      aplicacao do banco da DEMO, siglas; com as traducoes (EN/ES) do Multilingual do revamp (re_traducoes.py, 0056). A cobertura do funcional exige
      cada termo no glossario (titulo ou Sinonimos de um GLO). Palavras genericas de interface ficam fora (exclusoes).
  re_tool.py perguntas <documento.md> <perguntas.json> [--json saida.json]
      VISAO PRATICA (0054): quais perguntas reais do Pergunte o documento responde (titulo/corpo de um FAQ/TUT com as
      palavras da pergunta) — cobertura que o revisor ve.
  re_tool.py afetados <publicado.md> [--arquivos lista.txt] [--objetos lista.txt]
      Itens publicados que citam arquivos/objetos alterados (lista de trabalho do modo melhorar — 0053).
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

# 0058: etapa opcional de infra (re_infra.py) — recursos da AWS ligados ao modulo e esteiras dos repositorios.
AWS_CATS = ["esteira"] + ["aws-" + s for s in (
    "lambda", "s3", "codepipeline", "codebuild", "codedeploy", "secret", "ssm", "log-group", "sqs", "sns", "events-rule", "scheduler", "rds",
    "rds-cluster", "cognito", "cloudfront", "route53", "acm", "apigateway", "dynamodb", "elasticache", "alarm", "stepfunctions", "ecs", "ecr", "ec2",
    "beanstalk", "load-balancer", "kms-alias", "codeartifact", "glue-database", "glue-job", "glue-crawler", "ses", "kinesis")]

# Categorias do inventario e os documentos que precisam cobri-las.
DOC_CATEGORIES = {
    "funcional": ["validacao", "permissao", "endpoint", "enum", "pagina", "handler", "procedure", "termo"],
    "arquitetura": ["endpoint", "tabela", "evento", "job", "config", "http-front", "pagina", "handler", "procedure",
                    "view", "function", "trigger", "constraint", *AWS_CATS],
    "uiux": ["rota-front", "componente-front", "http-front", "pagina"],
    "design": ["componente-front"],
    "visao": [],
    "spec-arquitetura": ["evento", "job", "trigger"],
}

ID_HEADING = re.compile(r"^(#{2,4})\s+\**`?(TELA|PRF|EST|NTF|CFG|REL|TEC|CMP|API|EVT|JOB|INT|FLX|OBJ|PER|GLO|ADR|NFR|SEQ|GAP|SQL|TRG|INF|TUT|FAQ|FN|UC|RN|DB|UI)-(\d{1,4})\b",
                        re.IGNORECASE)


def norm(text):
    text = unicodedata.normalize("NFD", text or "")
    text = "".join(c for c in text if unicodedata.category(c) != "Mn").lower()
    return re.sub(r"[^a-z0-9_/.:-]+", " ", text).strip()


def repo_root(path):
    """Raiz do repositorio (pasta com .git) acima do arquivo — o caminho do inventario fica relativo a ela (0056)."""
    d = os.path.dirname(os.path.abspath(path))
    while True:
        if os.path.exists(os.path.join(d, ".git")):
            return d
        parent = os.path.dirname(d)
        if parent == d:
            return os.path.dirname(os.path.abspath(path))
        d = parent


# 0066-ajustes3: código de teste fica fora do inventário e das áreas (no revamp-users era 38% do código — 11 de 28 áreas
# só de teste, e as mensagens repetidas nos testes viravam itens cobrados pela cobertura)
TEST_DIR = re.compile(r"^(tests?|testes|__tests__|unittests?|integrationtests?|e2e|specs)$|[._-](unit|integration|functional)?tests?$", re.I)
TEST_FILE = re.compile(r"Tests?\.(cs|vb)$|TestHelpers?\.cs$|TestFixtures?\.cs$|\.(spec|test)\.(ts|js)$|^test_\w*\.py$|_test\.py$")


def is_test_dir(name):
    return bool(TEST_DIR.search(name))


def is_test_file(name):
    return bool(TEST_FILE.search(name))


def walk(root):
    if os.path.isfile(root):  # 0056: fonte arquivo (--path repo=helpers/Sa3Service.cs ou glob expandido)
        try:
            if os.path.getsize(root) <= MAX_FILE:
                with open(root, encoding="utf-8", errors="replace") as fh:
                    yield root, fh.read()
        except OSError:
            pass
        return
    for base, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.startswith(".") and not is_test_dir(d)]
        rel_base = os.path.relpath(base, root)
        if any(part in SKIP_DIRS for part in rel_base.split(os.sep)):
            continue
        for name in files:
            ext = os.path.splitext(name)[1].lower()
            if ext not in TEXT_EXT or name.endswith((".min.js", ".spec.ts", ".d.ts", ".map")) or "Designer" in name or is_test_file(name):
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
# 0066: legado .NET Core (solvace-core): mensagens das views vêm de ewcmAlert/ewcmConfirm com o texto traduzido por
# LanguageHelper.GetLanguageByTerm(..., "texto"), e as chamadas ao back por Url.Content("~/Controller/Acao").
R_VIEW_MSG = re.compile(r'ewcm\w*(?:Alert|Confirm)\s*\(\s*[\'"]?[^;\n]{0,60}?GetLanguageByTerm\([^,\n]+,\s*"([^"\n]{4,300})"')
R_URL_CONTENT = re.compile(r'Url\.Content\(\s*"~/([^"\n]{3,200})"\s*\)')


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
    for role, target in sources:
        root = repo_root(target) if os.path.isfile(target) else target
        for path, text in walk(target):
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
            if ext in (".cshtml", ".razor", ".js", ".html"):
                for m in R_VIEW_MSG.finditer(text):
                    add(items, seen, "validacao", m.group(1), path, root, text, m.start(), "mensagem na tela")
                for m in R_URL_CONTENT.finditer(text):
                    add(items, seen, "http-front", "~/" + m.group(1), path, root, text, m.start(), "chamada da view")
            if ext in (".asp", ".inc", ".aspx", ".vb", ".cshtml", ".js", ".cs"):
                for m in R_LANG.finditer(text):  # rótulos traduzidos da tela (legado) — matéria-prima do glossário
                    add(items, seen, "rotulo", m.group(1), path, root, text, m.start(), "GetLanguageByName")
            if ext == ".json" and ("/i18n/" in path.replace("\\", "/") or "/locale" in path) and re.search(r"(^|[/_.-])pt([-_]br)?\.json$", path.lower()):
                try:
                    data = json.loads(text)
                except ValueError:
                    data = None
                for key, value in flat_items(data):
                    if isinstance(value, str):
                        add(items, seen, "rotulo", value, path, root, text, 0, f"i18n {key}")
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


def flat_items(data, prefix=""):
    if isinstance(data, dict):
        for k, v in data.items():
            key = f"{prefix}.{k}" if prefix else k
            if isinstance(v, dict):
                yield from flat_items(v, key)
            else:
                yield key, v


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
STRONG_CATS = {"tabela", "enum", "handler", "componente-front", "config", "job", "evento", "procedure", *AWS_CATS}


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
    elif cat == "termo":
        out.append(norm(name))
    elif cat == "handler":
        out.append(norm(re.sub(r"(Query|Command)?(Queries)?(Request|Handler)$", "", name)))
    else:
        out.append(norm(name))
        if cat in ("componente-front",) and detail:
            out.append(norm(detail))
        if cat == "tabela" and detail.startswith("DbSet "):
            out.append(norm(detail[6:]))
    # 0056: sigla de 2 caracteres ("A3", "5S") e termo do glossario — antes caia no corte de 3 letras e nunca casava
    return [n for n in out if len(n) >= 3 or (cat == "termo" and len(n) == 2 and short_term(name))]


def short_term(raw):
    """Sigla curta que vale como termo (0056): 2 caracteres alfanumericos com digito, ou em maiusculas no original."""
    s = (raw or "").strip()
    return len(s) == 2 and s.isalnum() and (any(c.isdigit() for c in s) or s.isupper())


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


def glossary_text(doc):
    """Títulos e linhas de sinônimos dos itens GLO — onde um termo precisa aparecer para contar (0053)."""
    out, inside = [], False
    for line in doc.splitlines():
        m = ID_HEADING.match(line)
        if m:
            inside = m.group(2).upper() == "GLO"
            if inside:
                out.append(line)
            continue
        if re.match(r"^#{1,4}\s", line):
            inside = False
        if inside and re.search(r"(?i)\*\*sin[ôo]nimos", line):
            out.append(line)
    return " \n ".join(out)


def glossary_gaps(doc):
    """
    Lacunas que listam termos fora do glossario (0056) — o guia (references/banco.md) manda registrar o termo que nao e
    do dominio, ou e de outro modulo, num GAP com o motivo, e ele conta como coberto. Vale o GAP cujo titulo fala de
    termos/glossario (todo o bloco) e, em qualquer GAP, a linha "**Termos:** a, b, c". -> [(GAP-ID, texto normalizado)]
    """
    out, current, buf, whole = [], None, [], False
    def flush():
        if current and buf:
            out.append((current, norm(" \n ".join(buf))))
    for line in doc.splitlines():
        m = ID_HEADING.match(line)
        if m or re.match(r"^#{1,4}\s", line):
            flush()
            current, buf, whole = None, [], False
            if m and m.group(2).upper() == "GAP":
                current = f"GAP-{int(m.group(3)):03d}"
                whole = bool(re.search(r"(?i)termos?|gloss[aá]rio", line))
                if whole:
                    buf.append(line)
            continue
        if current and (whole or re.search(r"(?i)\*\*termos?:?\*\*", line)):
            buf.append(line)
    flush()
    return out


def coverage(inv, doc_text, doc_type, max_missing):
    cats = DOC_CATEGORIES.get(doc_type, [])
    doc_norm = norm(doc_text)
    strong_norm = norm(strong_text(doc_text))
    glossary_norm = norm(glossary_text(doc_text))
    gaps = glossary_gaps(doc_text)
    outside = []  # 0056: termos cobertos por estarem numa lacuna "fora do glossário"
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
        text = glossary_norm if it["cat"] == "termo" else strong_norm if it["cat"] in STRONG_CATS else doc_norm
        hit = any(contains(text, n) for n in needles(it))
        if not hit:
            base = os.path.basename(it["file"]).lower()
            hit = any(s - 5 <= it["line"] <= e + 5 for s, e in evidence.get(base, []))
        if it["cat"] == "termo":
            hit = any(contains(text, n) for n in needles(it))  # termo só conta no glossário (sem evidência de arquivo)
            if not hit:
                gap = next((g for g, t in gaps if any(contains(t, n) for n in needles(it))), None)
                if gap:
                    hit = True
                    outside.append({"name": it["name"], "gap": gap})
        if hit:
            covered += 1
            c["covered"] += 1
        else:
            missing.append(it)
    ratio = None if total == 0 else round(covered / total, 4)
    return {"docType": doc_type, "total": total, "covered": covered, "ratio": ratio, "byCategory": by_cat,
            "missing": missing[:max_missing], "missingCount": len(missing), "outsideGlossary": outside[:300]}


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


GENERIC_UI = {norm(w) for w in """
salvar cancelar filtrar filtro filtros data buscar pesquisar pesquisa editar excluir remover adicionar novo nova voltar fechar sim nao ok
confirmar limpar exportar importar imprimir detalhes acoes opcoes selecione selecionar todos todas nenhum nenhuma carregando erro sucesso
atencao aviso enviar anexar visualizar copiar alterar aplicar anterior proximo proxima primeiro ultimo inicio fim carregar mais mostrar
ocultar expandir recolher abrir baixar download upload arquivo arquivos anexo anexos link links buscando aguarde total ver mais menos
agora hoje ontem amanha semana mes ano dia dias horas hora minutos janeiro fevereiro marco abril maio junho julho agosto setembro outubro
novembro dezembro segunda terca quarta quinta sexta sabado domingo jan fev mar abr mai jun jul ago set out nov dez
caixa de selecao caixa suspensa calendario texto numero lista campo campos obrigatorio opcional descricao titulo nome codigo valor
save cancel filter filters date search edit delete remove add new back close yes no confirm clear export import print details actions
options select all none loading error success warning send apply previous next first last more less today yesterday tomorrow
procurar exibir incluir processando salvando primeira ultima minuto days results resultados show dom seg ter qua qui sex sab registro
registros page pages pagina paginas item itens
""".split()} | {norm(w) for w in ["caixa de seleção", "caixa suspensa", "busca global", "carregar mais", "ver mais", "adicionar link"]}

MESSAGE_HINTS = ("sucesso", "êxito", "exito", "deseja", "certeza", "por favor", "aguarde", "não foi possível", "nao foi possivel",
                 "obrigatório", "preencha", "selecione um", "selecione uma", "informe", "inválid", "invalid", "successfully", "please",
                 "digite", "insira", "nenhum registro", "nenhum resultado", "exibindo", "showing", "encontramos", "registros que",
                 "registro(s)", "expandir todos", "recolher todos", "limpar filtros", "pesquisado a partir", "novo texto")


def is_term(text):
    t = (text or "").strip().rstrip(":").strip()
    if not (2 <= len(t) <= 60) or not re.search(r"[A-Za-zÀ-ú]", t):
        return None
    if any(c in t for c in "{}<>[]|%=") or "http" in t.lower() or t.endswith((".", "?", "!")):
        return None
    if len(t.split()) > 5 or any(h in t.lower() for h in MESSAGE_HINTS):
        return None
    return t


def termos(inv_path, banco_dir, exclusions, out, other_modules=()):
    inv = json.load(open(inv_path, encoding="utf-8"))
    # genéricos de interface + nomes de outros módulos (citados por link/integração, não são termos deste)
    excl = {norm(x) for x in exclusions} | GENERIC_UI | {norm(x) for x in other_modules}
    terms = {}

    def put(term, source, file="", line=0):
        t = is_term(term)
        if not t or norm(t) in excl:
            return
        key = norm(t)
        e = terms.setdefault(key, {"term": t, "sources": set(), "file": file, "line": line})
        e["sources"].add(source)

    for it in inv.get("items", []):
        if it["cat"] == "rotulo":
            put(it["name"], "tela", it["file"], it["line"])
    translations = {}
    if banco_dir and os.path.isdir(banco_dir):
        cat_path = os.path.join(banco_dir, "catalogo.json")
        if os.path.exists(cat_path):
            cat = json.load(open(cat_path, encoding="utf-8"))
            app = cat.get("application") or {}
            for v in (app.get("name"), app.get("alias")):
                if v:
                    put(v, "aplicação", "banco/catalogo.json", 1)
            for m in cat.get("menus", []):
                put(m.get("item"), "menu", "banco/catalogo.json", 1)
                put(m.get("grp"), "menu", "banco/catalogo.json", 1)
            if cat.get("sigla"):
                put(cat["sigla"], "sigla", "banco/catalogo.json", 1)
        tr_path = os.path.join(banco_dir, "traducoes.json")
        if os.path.exists(tr_path):
            for r in json.load(open(tr_path, encoding="utf-8")):
                for k in ("term", "pt"):
                    if r.get(k):
                        translations.setdefault(norm(r[k]), r)
    items = []
    seen = set(terms)
    for key, e in sorted(terms.items()):
        tr = translations.get(key)
        detail = ", ".join(sorted(e["sources"]))
        if tr:
            extra = [f"{lang.upper()}: {tr[lang]}" for lang in ("en", "es") if tr.get(lang) and norm(tr[lang]) != key]
            if extra:
                detail += " · " + "; ".join(extra)
        items.append({"cat": "termo", "name": e["term"], "file": e["file"], "line": e["line"], "detail": detail})
        # 0060: o nome EN/ES vira termo exigido (Sinonimos do GLO) — os cards dos clientes vem em ingles ("Compliance per
        # Checklist") e a busca so acha "Cumprimento por Checklist" pelo sinonimo (caso do card 75294).
        # 0064: portugues (o texto pt-BR do Multilingual, quando difere da chave), ingles e espanhol — os mais usados
        for lang in ("pt", "en", "es"):
            v = is_term(tr.get(lang)) if tr else None
            if v and norm(v) not in seen:
                seen.add(norm(v))
                items.append({"cat": "termo", "name": v, "file": e["file"], "line": e["line"], "detail": f"{lang.upper()} de \"{e['term']}\" (traducao)"})
    with open(out, "w", encoding="utf-8") as fh:
        json.dump({"version": 1, "sources": [], "files": {}, "counts": {"termo": len(items)}, "items": items}, fh, ensure_ascii=False, indent=1)
    print(f"Termos do módulo para o glossário: {len(items)} ({sum(1 for i in items if '(traducao)' in i['detail'])} nomes PT/EN/ES traduzidos) -> {out}")
    if not translations:
        print("AVISO: sem traducoes (banco/traducoes.json) — o glossario fica sem os nomes em ingles/espanhol e os cards em ingles "
              "nao acham as telas/relatorios. Configure ~/.claude/multilingual-credentials.json e rode: re.sh traducoes <modulo> && re.sh termos <modulo>")


def afetados(doc_path, files, objects):
    """
    Itens do documento publicado afetados pelo que mudou: objeto do banco citado, ou arquivo alterado cujas LINHAS
    alteradas (lado antigo do diff) caem perto do arquivo:linha que o item cita (±10). Item que cita o arquivo sem linha
    conta como afetado. files: "arquivo<TAB>ini-fim,ini-fim" (ou só o arquivo = mudou inteiro).
    """
    doc = open(doc_path, encoding="utf-8").read()
    ranges = {}
    for f in files:
        if not f.strip():
            continue
        name, _, spans = f.partition("\t")
        rs = []
        for span in filter(None, spans.split(",")):
            a, _, b = span.partition("-")
            rs.append((int(a), int(b or a)))
        ranges.setdefault(os.path.basename(name).lower(), []).extend(rs or [(0, 10 ** 9)])
    objs = {o.strip().upper() for o in objects if o.strip()}
    blocks, current, buf = [], None, []
    for line in doc.splitlines():
        m = ID_HEADING.match(line)
        if m or re.match(r"^#{1,2}\s", line):
            if current:
                blocks.append((current, "\n".join(buf)))
            current = f"{m.group(2).upper()}-{int(m.group(3)):03d} {line.split('—', 1)[-1].strip()}" if m else None
            buf = [line]
        elif current:
            buf.append(line)
    if current:
        blocks.append((current, "\n".join(buf)))
    hits = []
    for item, body in blocks:
        why = set(o for o in objs if o in body.upper())
        low = body.lower()
        for base, rs in ranges.items():
            if base not in low:
                continue
            cited = [(int(mm.group(1)), int(mm.group(2) or mm.group(1)))
                     for mm in re.finditer(re.escape(base) + r":(\d+)(?:-(\d+))?", low)]
            if not cited or any(ca - 10 <= b and a <= cb + 10 for ca, cb in cited for a, b in rs):
                why.add(base)
        if why:
            hits.append((item, sorted(why)))
    for item, why in hits:
        print(f"{item}\t{', '.join(why[:6])}")
    return hits


STOP = set("a o as os um uma de do da dos das em no na nos nas por para pra com sem que se e ou como qual quais quando onde quem "
           "porque por que e é eu voce você meu minha isso esse essa este esta tem ter ha há ser foi esta está nao não sim pode posso"
           " consigo devo the of to in and is how what why".split())


def words(text):
    # 0056: "a3", "5s" (2 caracteres com digito) tambem contam — "Como criar um A3?" precisa casar com o FAQ do A3
    return {w[:-2] if len(w) > 5 else w for w in re.findall(r"[a-z0-9]+", norm(text))
            if w not in STOP and (len(w) >= 3 or (len(w) == 2 and any(c.isdigit() for c in w)))}


def perguntas(doc_path, questions_path, out=None):
    """Cobertura das perguntas reais pela visão prática: a pergunta casa com um FAQ/TUT que tenha ≥ 60% das palavras dela."""
    doc = open(doc_path, encoding="utf-8").read()
    qs = json.load(open(questions_path, encoding="utf-8"))
    blocks, current, buf = [], None, []
    for line in doc.splitlines():
        m = ID_HEADING.match(line)
        if m or re.match(r"^#{1,2}\s", line):
            if current:
                blocks.append((current, "\n".join(buf)))
            current = f"{m.group(2).upper()}-{int(m.group(3)):03d}" if m and m.group(2).upper() in ("FAQ", "TUT") else None
            buf = [line]
        elif current:
            buf.append(line)
    if current:
        blocks.append((current, "\n".join(buf)))
    covered, missing = [], []
    for q in qs:
        qw = words(q.get("text", ""))
        best = max(((len(qw & words(body)) / max(1, len(qw)), item) for item, body in blocks), default=(0, None))
        (covered if qw and best[0] >= 0.6 else missing).append({"cat": "pergunta", "name": q.get("text", ""), "file": "", "line": 0,
                                                              "detail": f"{q.get('times', 1)}× · {q.get('coverage', '')}" + (f" · melhor: {best[1]} ({best[0]:.0%})" if best[1] else "")})
    total = len(qs)
    result = {"docType": "pratica", "total": total, "covered": len(covered), "ratio": None if total == 0 else round(len(covered) / total, 4),
              "byCategory": {"pergunta": {"total": total, "covered": len(covered)}}, "missing": missing, "missingCount": len(missing)}
    if out:
        with open(out, "w", encoding="utf-8") as fh:
            json.dump(result, fh, ensure_ascii=False, indent=1)
    if total == 0:
        print("Nenhuma pergunta do Pergunte sobre o módulo ainda — escreva as perguntas práticas mais prováveis (ex.: legado ou revamp?, como fazer X?).")
    else:
        print(f"Perguntas reais respondidas: {len(covered)}/{total} = {result['ratio']:.0%}")
        for m in missing[:60]:
            print(f"  SEM RESPOSTA: {m['name'][:120]} ({m['detail']})")
    return result


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
            if not path or not os.path.exists(path):  # 0056: pasta ou arquivo
                print(f"ERRO: caminho inexistente: {spec}", file=sys.stderr)
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
        # 0053/0058: inventários a mais (banco da DEMO, termos do glossário, infra) — --extra <arquivo> (repetível)
        for i, a in enumerate(argv):
            if a == "--extra" and i + 1 < len(argv) and os.path.exists(argv[i + 1]):
                with open(argv[i + 1], encoding="utf-8") as fh:
                    extra = json.load(fh)
                seen_names = {(it["cat"], it["name"].upper()) for it in inv["items"]}
                inv["items"] += [it for it in extra.get("items", []) if (it["cat"], it["name"].upper()) not in seen_names]
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
    if cmd == "termos":
        if len(argv) < 3 or "--out" not in argv:
            print("uso: re_tool.py termos <inventario.json> [--banco <pasta>] [--exclusoes '<json>'] --out <saida.json>", file=sys.stderr)
            return 2
        opt = lambda n: argv[argv.index(n) + 1] if n in argv else None
        termos(argv[2], opt("--banco"), json.loads(opt("--exclusoes") or "[]"), opt("--out"), json.loads(opt("--outros-modulos") or "[]"))
        return 0
    if cmd == "perguntas":
        opt = lambda n: argv[argv.index(n) + 1] if n in argv else None
        perguntas(argv[2], argv[3], opt("--json"))
        return 0
    if cmd == "afetados":
        opt = lambda n: argv[argv.index(n) + 1] if n in argv else None
        read = lambda f: open(f, encoding="utf-8").read().splitlines() if f and os.path.exists(f) else []
        afetados(argv[2], read(opt("--arquivos")), read(opt("--objetos")))
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
