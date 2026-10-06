#!/usr/bin/env python3
"""Geração barata da engenharia reversa (feature 0066) — sem LLM, sem rede.

O custo de um subagente cresce com (chamadas x contexto): área grande + uma leitura por chamada + instruções relidas
custavam 10-30 M de leitura de cache por área. Aqui o script faz o trabalho mecânico:

  re_pacote.py areas <inventario.json> <tipo-doc> --out areas.json [--extra inv.json ...] [--ids ids.txt]
                     [--orcamento-kb 90] [--arquivo-pequeno 400] [--bloco-max 220]
      divide o código do módulo em ÁREAS que cabem no orçamento (KB do pacote de leitura), com arquivos relacionados
      juntos, e reserva uma faixa de números de ID por área (sem colidir com os IDs já usados no módulo).
  re_pacote.py pacote <areas.json> <pasta-saida> [--area NOME] [--banco <pasta-banco>] [--parte-dir <pasta-doc>]
      um pacote por área (pacote-<area>.md): os itens do inventário que a área precisa cobrir e os TRECHOS do código
      (método inteiro em volta de cada item; arquivo pequeno inteiro), com número de linha, + as tabelas da área.
  re_pacote.py especiais <areas.json> <pasta-saida> --modulo-dir <pasta-do-modulo> [--parte-dir D]
      0066-ajustes: pacotes dos SUBAGENTES ESPECIAIS (o que é do módulo inteiro): funcional → glossário; arquitetura →
      banco (tabelas/objetos/triggers/jobs do catálogo) e módulo (tecnologias, configuração, segurança, observabilidade, infra).
  re_pacote.py sintese <areas.json> <pasta-saida> --fonte funcional=<doc.md> --fonte arquitetura=<doc.md> [--parte-dir D]
      documentos de SÍNTESE (visão, spec de arquitetura): pacote com os itens dos levantamentos — sem ler o código.
  re_pacote.py cartao <subagente.md> <modelo.md> <modulos.tsv> --out cartao.md
      o cartão do subagente: instruções curtas + o modelo do documento + as chaves dos módulos (para **Módulos:**).
  re_pacote.py faltando <areas.json> <area> <parte.md> [--tipo-doc funcional]
      o que a parte da área ainda não cobre (checkpoint/retomada: o subagente continua daí).
  re_pacote.py evidencia <documento.md> <inventario.json> [--json saida.json] [--max 40]
      confere a evidência: cada `arquivo:linha` existe nas fontes e os literais citados aparecem no arquivo citado.
  re_pacote.py juntar <saida.md> [--base documento.md] <parte1.md> [parte2.md ...]
      junta as partes das áreas sobre a base (o item reescrito numa parte substitui o da base — modo melhorar).
  re_pacote.py repetidos <documento.md> [--out saida.md]
      funde itens do mesmo tipo e mesmo título vindos de áreas diferentes (API, INT, DB, TEC, CFG, GLO…), referências juntas.
  re_pacote.py tamanho <documento.md>         tamanho × limite do PRMake (2 milhões de caracteres)
  re_pacote.py compactar <documento.md> --areas areas.json [--ids ids.txt] [--out saida.md]
      depois de juntar as partes: os IDs das faixas das áreas viram a sequência de cada tipo (referências trocadas junto).
"""
import json
import os
import re
import sys
from collections import Counter, OrderedDict, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import re_tool  # noqa: E402  (mesma normalização, cobertura e IDs)

CODE_EXT = {".cs", ".cshtml", ".razor", ".asp", ".aspx", ".ascx", ".inc", ".js", ".ts", ".html", ".sql", ".vb", ".py", ".json", ".yml",
            ".yaml", ".config", ".xml"}
FRONT_EXT = {".cshtml", ".razor", ".asp", ".aspx", ".ascx", ".inc", ".js", ".ts", ".html"}
LANG = {".cs": "csharp", ".cshtml": "cshtml", ".razor": "razor", ".js": "js", ".ts": "ts", ".html": "html", ".sql": "sql", ".vb": "vb",
        ".asp": "asp", ".aspx": "aspx", ".inc": "asp", ".json": "json", ".py": "python", ".yml": "yaml", ".yaml": "yaml", ".xml": "xml"}

# Arquivos que cada documento lê (o pacote leva os arquivos INTEIROS, em blocos — no legado muita regra vive só na view, e o
# inventário não pega tudo; ele serve de lista do que precisa estar coberto, não de recorte do que ler).
BACK_EXT = {".cs", ".vb", ".sql", ".py"}
CONFIG_EXT = {".json", ".yml", ".yaml", ".config", ".xml"}
STYLE_EXT = {".css", ".scss", ".less"}
DOC_FILES = {
    "funcional": BACK_EXT | FRONT_EXT,
    "arquitetura": BACK_EXT | FRONT_EXT | CONFIG_EXT,
    "spec-arquitetura": BACK_EXT | FRONT_EXT | CONFIG_EXT,
    "uiux": FRONT_EXT | STYLE_EXT,
    "design": FRONT_EXT | STYLE_EXT,
    "visao": BACK_EXT | FRONT_EXT,
}
FRONT_DOCS = {"uiux", "design"}
SKIP_PARTS = {"bin", "obj", "node_modules", "dist", "build", ".angular", "packages", "migrations", "testresults", "__pycache__", "lib", "libs", "vendor",
              "plugins", "templates", "fonts", "images", "img", "assets", "coverage", ".vscode", ".vs", ".idea", "properties", "e2e"}
SKIP_NAME = re.compile(r"(\.min\.(js|css)$|\.bundle\.|\.map$|\.d\.ts$|\.spec\.ts$|\.test\.|designer\.cs$|modelsnapshot\.cs$|assemblyinfo\.cs$|"
                       r"launchsettings\.json$|package(-lock)?\.json$|tsconfig.*\.json$|angular\.json$|\.g\.cs$)", re.I)
MAX_SOURCE = 1_500_000

# Começo de bloco (método/função) por linguagem — o corte dos arquivos grandes cai num começo de bloco.
R_BLOCK_START = {
    "cs": re.compile(r"^\s*(?:\[[^\]]*\]\s*)*(?:public|private|protected|internal|static|async|override|virtual|sealed|partial|\s)+[\w<>\[\],.?\s]+\s+\w+\s*(?:<[^>]*>)?\s*\("),
    "js": re.compile(r"^\s*(?:export\s+)?(?:async\s+)?(?:function\s+\w*\s*\(|(?:const|let|var)\s+\w+\s*=\s*(?:async\s*)?(?:function|\([^)]*\)\s*=>)|\w+\s*:\s*(?:async\s*)?function\s*\(|\$\(\s*(?:document|function))|^\s*<script\b|^\s*@section\b"
                     r"|^\s+(?:async\s+)?(?!if\b|for\b|while\b|switch\b|catch\b|return\b)[A-Za-z_]\w*\s*\([^)]*\)\s*\{\s*$"),
    "vb": re.compile(r"^\s*(?:Public\s+|Private\s+)?(?:Function|Sub)\s+\w+", re.I),
}
R_BLOCK_END_VB = re.compile(r"^\s*End\s+(?:Function|Sub)\b", re.I)


def lang_of(path):
    ext = os.path.splitext(path)[1].lower()
    if ext == ".cs":
        return "cs"
    if ext in (".asp", ".inc", ".aspx", ".vb"):
        return "vb"
    if ext in (".js", ".ts", ".cshtml", ".razor", ".html", ".ascx"):
        return "js"
    return None


def read_lines(path):
    try:
        with open(path, encoding="utf-8", errors="replace") as fh:
            return fh.read().splitlines()
    except OSError:
        return []


# ── fontes: caminho do inventário → arquivo no disco ──────────────────────────────────────────────

def source_roots(inv):
    roots = []
    for s in inv.get("sources", []):
        p = s.get("path") or ""
        if p and os.path.exists(p):
            roots.append(re_tool.repo_root(p) if os.path.isfile(p) else p)
    return roots


def resolve_file(rel, roots, cache):
    """Caminho do inventário (relativo à fonte, ou à raiz do repo se a fonte é um arquivo) → arquivo no disco."""
    if rel in cache:
        return cache[rel]
    found = None
    for r in roots:
        cand = os.path.join(r, rel)
        if os.path.isfile(cand):
            found = cand
            break
    cache[rel] = found
    return found


# ── arquivos do documento e cortes ────────────────────────────────────────────────────────────────

def doc_files(inv, doc_type):
    """Arquivos de código das fontes que o documento lê -> {relativo: absoluto}. Relativo = como o inventário cita."""
    exts = DOC_FILES.get(doc_type, BACK_EXT | FRONT_EXT)
    out = {}
    for s in inv.get("sources", []):
        p = s.get("path") or ""
        if not p or not os.path.exists(p):
            continue
        if os.path.isfile(p):
            if re_tool.is_test_file(os.path.basename(p)):
                continue
            root = re_tool.repo_root(p)
            out[os.path.relpath(p, root)] = p
            continue
        for base, dirs, files in os.walk(p):
            dirs[:] = [d for d in dirs if d.lower() not in SKIP_PARTS and not d.startswith(".") and not re_tool.is_test_dir(d)]
            for f in files:
                ext = os.path.splitext(f)[1].lower()
                if ext not in exts or SKIP_NAME.search(f) or re_tool.is_test_file(f):
                    continue
                if ext in CONFIG_EXT and not re.match(r"(appsettings|serverless|buildspec|appspec|docker|aws-lambda|template|web\.config)", f, re.I):
                    continue
                full = os.path.join(base, f)
                try:
                    if os.path.getsize(full) > MAX_SOURCE or os.path.getsize(full) == 0:
                        continue
                except OSError:
                    continue
                out[os.path.relpath(full, p)] = full
    if doc_type in FRONT_DOCS:  # flags e ViewBag que a tela usa: controllers entram como apoio
        for s in inv.get("sources", []):
            p = s.get("path") or ""
            if os.path.isdir(p):
                for base, dirs, files in os.walk(p):
                    dirs[:] = [d for d in dirs if d.lower() not in SKIP_PARTS and not d.startswith(".") and not re_tool.is_test_dir(d)]
                    for f in files:
                        if f.endswith("Controller.cs") and not re_tool.is_test_file(f):
                            out.setdefault(os.path.relpath(os.path.join(base, f), p), os.path.join(base, f))
    return out


def cut_points(lines, lang):
    """Linhas onde dá para cortar um arquivo grande sem partir um método: começos de bloco (ou linhas em branco)."""
    rx = R_BLOCK_START.get(lang)
    pts = [k for k, ln in enumerate(lines) if rx and rx.search(ln)]
    return pts or [k for k, ln in enumerate(lines) if not ln.strip()]


def chunks(lines, lang, limit):
    """[(ini, fim)] 0-based cobrindo o arquivo inteiro, cada pedaço até ~limit bytes, cortando em começo de bloco."""
    n = len(lines)
    sizes = [len(x) + 7 for x in lines]
    total = sum(sizes)
    if total <= limit:
        return [(0, n - 1)]
    pts = set(cut_points(lines, lang))
    out, start, acc, last_cut = [], 0, 0, None
    for k in range(n):
        if k > start and k in pts:
            last_cut = k
        acc += sizes[k]
        if acc > limit and k > start:
            cut = last_cut if last_cut and last_cut > start + 20 else k + 1
            out.append((start, cut - 1))
            start, last_cut = cut, None
            acc = sum(sizes[start:k + 1])
    if start <= n - 1:
        out.append((start, n - 1))
    return out


def method_block(lines, line, lang, max_lines=160):
    """(ini, fim) 0-based do método/função que contém a linha (1-based): sobe até a assinatura, desce até fechar as chaves."""
    n = len(lines)
    i = max(0, min(n - 1, line - 1))
    rx = R_BLOCK_START.get(lang)
    # o inventário pode apontar o atributo ([HttpPost]) logo acima da assinatura: olha primeiro algumas linhas abaixo
    start = next((j for j in range(i, min(n, i + 6)) if rx and rx.search(lines[j])), None)
    if start is None:
        start = next((j for j in range(i, max(-1, i - 60), -1) if rx and rx.search(lines[j])), None)
    if start is None:
        return max(0, i - 15), min(n - 1, i + 40)
    if lang == "vb":
        end = next((k for k in range(start + 1, min(n, start + max_lines)) if R_BLOCK_END_VB.search(lines[k])), min(n - 1, start + 60))
        return start, end
    depth, opened = 0, False
    for k in range(start, min(n, start + max_lines)):
        code = re.sub(r'"(?:[^"\\]|\\.)*"|\'(?:[^\'\\]|\\.)*\'|//.*$', "", lines[k])
        for ch in code:
            if ch == "{":
                depth += 1
                opened = True
            elif ch == "}":
                depth -= 1
        if opened and depth <= 0:
            return start, k
    return start, min(n - 1, start + max_lines - 1)


def route_key(name):
    """'~/Report/GetX', 'POST Report/GetX', 'GET api/report/x/{id}' → 'report/getx' (os dois últimos segmentos)."""
    raw = name.split(" ", 1)[-1] if re.match(r"^[A-Z]+ ", name) else name
    m = re.match(r"^(\w+?)Controller\.(\w+)$", raw)  # sem rota no atributo: "ReportController.GetX"
    if m:
        raw = f"{m.group(1)}/{m.group(2)}"
    segs = [x for x in re.sub(r"\{[^}]*\}|\?.*$", "", raw.lstrip("~")).strip("/").lower().split("/") if x and x != ".."]
    return "/".join(segs[-2:]) if len(segs) >= 2 else ""


def support_blocks(inv, area, max_bytes=40 * 1024):
    """0066: métodos chamados pela área que moram em arquivos de outra área (a view chama ~/Report/GetX → ReportController.GetX)."""
    spans = defaultdict(list)
    for u in area["units"]:
        spans[u["file"]] += [tuple(r) for r in u["ranges"]]
    calls = {route_key(it["name"]) for it in inv["items"] if it["cat"] == "http-front" and it.get("file") in spans
             and any(a <= it["line"] <= b for a, b in spans[it["file"]])}
    calls.discard("")
    roots, cache, out, used = source_roots(inv), {}, [], 0
    for it in inv["items"]:
        if it["cat"] != "endpoint" or route_key(it["name"]) not in calls:
            continue
        f = it.get("file") or ""
        if f in spans and any(a <= it["line"] <= b for a, b in spans[f]):
            continue  # já está no pacote
        path = resolve_file(f, roots, cache)
        if not path:
            continue
        lines = read_lines(path)
        a, b = method_block(lines, it["line"], lang_of(path))
        size = excerpt_bytes(lines, [(a, b)])
        if used + size > max_bytes:
            break
        used += size
        out.append((f, a, b, lines))
    # um salto a mais: o controller costuma só repassar para o service/repositório — traz o método chamado
    seen = {(f, a) for f, a, _, _ in out}
    for f, a, b, lines in list(out):
        for owner, method in set(R_SERVICE_CALL.findall("\n".join(lines[a:b + 1]))):
            hit = find_method(inv, method, owner, roots, cache)
            if not hit:
                continue
            hf, ha, hb, hlines = hit
            if (hf, ha) in seen or (hf in spans and any(x <= ha + 1 <= y for x, y in spans[hf])):
                continue
            size = excerpt_bytes(hlines, [(ha, hb)])
            if used + size > max_bytes:
                return out
            used += size
            seen.add((hf, ha))
            out.append((hf, ha, hb, hlines))
    return out


R_SERVICE_CALL = re.compile(r"\b_?(\w*(?:Service|Repository|Helper|Business|Bll|Dal))\.(\w+)\s*\(", re.I)
_methods_cache = {}


def find_method(inv, method, owner, roots, cache):
    """Declaração do método (C#) num arquivo cujo nome lembra o dono (ReportService → ReportService.cs); senão em qualquer .cs."""
    key = (id(inv), method, owner.lower())
    if key in _methods_cache:
        return _methods_cache[key]
    files = [p for r in roots for p in walk_cs(r)]
    owner_name = owner.lstrip("_").lower()
    files.sort(key=lambda p: (owner_name.replace("_", "") not in os.path.basename(p).lower(), p))
    decl = re.compile(r"^\s*(?:public|private|protected|internal)[^=;(]*\b" + re.escape(method) + r"\s*(?:<[^>]*>)?\s*\(")
    result = None
    for p in files[:400]:
        lines = read_lines(p)
        for k, ln in enumerate(lines):
            if decl.search(ln):
                a, b = method_block(lines, k + 1, "cs", 220)
                rel = next((os.path.relpath(p, r) for r in roots if p.startswith(r)), p)
                result = (rel, a, b, lines)
                break
        if result:
            break
    _methods_cache[key] = result
    return result


def walk_cs(root):
    if os.path.isfile(root):
        return [root] if root.endswith(".cs") else []
    out = []
    for base, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d.lower() not in SKIP_PARTS and not d.startswith(".") and not re_tool.is_test_dir(d)]
        out += [os.path.join(base, f) for f in files if f.endswith(".cs") and not re_tool.is_test_file(f)]
    return out


SHARED_DIR_NAMES = {"includes", "view_shared", "shared"}
VENDOR = re.compile(r"jquery|bootstrap|datatable|dhtmlx|codemirror|moment|select2|chart|echarts|tinymce|ckeditor|fullcalendar|bootbox|anime|"
                    r"chroma|pdf|xlsx|lodash|swiper|sweetalert|toastr|summernote|fontawesome|leaflet|plugin|vendor|polyfill|templates?|limitless|"
                    r"calendar|codemirror|gantt|grid|highcharts|d3|three|signalr|jstree|qrcode|crypto|base64|dropzone|cropper|tooltip|flexmonster|toolbar|"
                    r"kendo|devextreme|syncfusion|handsontable|pivot|gridstack|sortable|masonry|isotope", re.I)
R_VB_DEF = re.compile(r"^\s*(?:Public\s+|Private\s+)?(?:Function|Sub)\s+(\w+)", re.I | re.M)
R_JS_DEF = re.compile(r"(?:^|[\s;])function\s+(\w+)\s*\(|^\s*(\w+)\s*[:=]\s*(?:async\s+)?function\s*\(|^\s{2,}(?:async\s+)?(\w+)\s*\([^)]*\)\s*\{\s*$", re.M)
R_CALL = re.compile(r"(?<![\w])(?:\.)?([A-Za-z_]\w{5,})\s*\(")
CALL_STOP = {"function", "return", "parseInt", "parseFloat", "toString", "replace", "indexOf", "length", "substring", "substr", "toLowerCase",
             "toUpperCase", "isNaN", "setTimeout", "setInterval", "encodeURIComponent", "decodeURIComponent", "getElementById", "querySelector",
             "addEventListener", "preventDefault", "stopPropagation", "JSON", "Request", "Response", "Server", "CreateObject", "Execute", "Convert",
             "String", "Number", "Object", "Array", "console", "append", "prepend", "remove", "trigger", "change", "submit", "attr", "removeClass",
             "addClass", "toggleClass", "hasClass", "children", "parent", "closest", "each", "filter", "forEach", "push", "splice", "slice", "split",
             "trim", "join", "concat", "ToString", "IsNull", "IsNullOrEmpty", "DateAdd", "DateDiff", "CStr", "CInt", "CLng", "FormatNumber",
             "Response_Write", "ajaxSetup", "serialize", "DataTable", "datepicker", "daterangepicker", "select2", "inputmask", "tooltip", "modal"}
_shared_cache = {}


def shared_roots(inv):
    """Pastas de código compartilhado do repositório das fontes (includes do ASP, view_shared/shared do core)."""
    roots, out = set(), []
    for s in inv.get("sources", []):
        p = s.get("path") or ""
        if p and os.path.exists(p):
            roots.add(re_tool.repo_root(p if os.path.isdir(p) else os.path.dirname(p)))
    for r in sorted(roots):
        for base, dirs, _ in os.walk(r):
            depth = os.path.relpath(base, r).count(os.sep)
            dirs[:] = [d for d in dirs if d.lower() not in SKIP_PARTS and not d.startswith(".") and not re_tool.is_test_dir(d) and depth < 3]
            for d in dirs:
                if d.lower() in SHARED_DIR_NAMES:
                    out.append(os.path.join(base, d))
    return [x for x in out if not any(x != y and x.startswith(y + os.sep) for y in out)]


def shared_index(roots):
    """nome da função → [(arquivo, linha 1-based, linguagem)] — só código da Solvace (bibliotecas de terceiros fora)."""
    key = tuple(roots)
    if key in _shared_cache:
        return _shared_cache[key]
    idx = defaultdict(list)
    for r in roots:
        for base, dirs, files in os.walk(r):
            dirs[:] = [d for d in dirs if d.lower() not in SKIP_PARTS and not d.startswith(".") and not re_tool.is_test_dir(d) and not VENDOR.search(d)]
            for f in files:
                ext = os.path.splitext(f)[1].lower()
                if ext not in (".asp", ".inc", ".js") or SKIP_NAME.search(f) or VENDOR.search(f):
                    continue
                path = os.path.join(base, f)
                try:
                    if os.path.getsize(path) > 300 * 1024:
                        continue
                    text = open(path, encoding="utf-8", errors="replace").read()
                except OSError:
                    continue
                rx, lang = (R_VB_DEF, "vb") if ext in (".asp", ".inc") else (R_JS_DEF, "js")
                for m in rx.finditer(text):
                    name = next(g for g in m.groups() if g)
                    if len(name) >= 6 and name not in CALL_STOP:
                        idx[name].append((path, text.count("\n", 0, m.start()) + 1 + (1 if text[m.start()] in "\n;" else 0), lang))
    _shared_cache[key] = idx
    return idx


def shared_blocks(inv, area, max_bytes=30 * 1024, max_funcs=30):
    """0066-ajustes: funções compartilhadas (includes do ASP/JS) que a área chama e não define — o corpo vai no pacote."""
    roots = shared_roots(inv)
    if not roots:
        return []
    src_roots, cache = source_roots(inv), {}
    text = []
    for u in area["units"]:
        path = resolve_file(u["file"], src_roots, cache)
        lines = read_lines(path) if path else []
        for a, b in u["ranges"]:
            text.append("\n".join(lines[a - 1:b]))
    code = "\n".join(text)
    defined = {next(g for g in m.groups() if g) for rx in (R_VB_DEF, R_JS_DEF) for m in rx.finditer(code)}
    calls = Counter(m.group(1) for m in R_CALL.finditer(code))
    idx = shared_index(roots)
    out, used = [], 0
    for name, _ in calls.most_common():
        if name in defined or name in CALL_STOP or name not in idx or len(idx[name]) > 2:
            continue
        path, line, lang = idx[name][0]
        lines = read_lines(path)
        a, b = method_block(lines, line, lang, 120)
        size = excerpt_bytes(lines, [(a, b)])
        if used + size > max_bytes:
            continue
        used += size
        rel = os.path.relpath(path, re_tool.repo_root(path))
        out.append((rel, a, b, lines))
        if len(out) >= max_funcs:
            break
    return out


def excerpt_bytes(lines, ranges):
    return sum(sum(len(lines[k]) + 7 for k in range(a, b + 1)) for a, b in ranges)


# ── áreas ─────────────────────────────────────────────────────────────────────────────────────────

GENERIC_STEM = {"controller", "controllers", "service", "services", "repository", "repositories", "helper", "helpers", "views", "view", "models",
                "model", "component", "components", "page", "pages", "index", "api", "app", "src", "lib", "shared", "common", "core", "base",
                "reg", "lst", "rpt", "cad", "con", "frm", "edit", "list", "detail", "details", "module", "routes", "routing", "dto", "dtos",
                "query", "queries", "command", "commands", "handler", "handlers", "validator", "validators", "wwwroot", "js", "css", "scripts"}


def stems(rel):
    """Palavras do assunto do arquivo: ChecklistController.cs → checklist; chk_rpt_checklist_duration.cshtml → checklist, duration."""
    base = os.path.splitext(os.path.basename(rel))[0]
    base = re.sub(r"\.(component|service|module|page|routes|model|spec)$", "", base)
    words = re.findall(r"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+", base.replace("-", "_"))
    words = [w.lower() for w in words]
    out = [w for w in words if len(w) >= 3 and w not in GENERIC_STEM]
    if not out:
        parent = os.path.basename(os.path.dirname(rel)).lower()
        out = [parent] if parent and parent not in GENERIC_STEM else ["geral"]
    return out


def plan_areas(inv, doc_type, budget_kb, small, max_lines, used_ids):
    """
    Áreas do documento: os arquivos que ele lê, inteiros, cortados em pedaços de até ~metade do orçamento e agrupados em
    áreas de até `budget_kb` (arquivos do mesmo assunto juntos). `small`/`max_lines` ficam para compatibilidade.
    """
    files = doc_files(inv, doc_type)
    item_count = Counter(it.get("file") for it in inv["items"])
    budget = budget_kb * 1024
    units = []
    for rel, path in files.items():
        lines = read_lines(path)
        if not lines:
            continue
        # 0066-ajustes: arquivo que cabe no orçamento vai inteiro (partido entre áreas, o subagente ia atrás da outra metade)
        whole = excerpt_bytes(lines, [(0, len(lines) - 1)])
        parts = [(0, len(lines) - 1)] if whole <= budget else chunks(lines, lang_of(path), max(8 * 1024, int(budget * 0.9)))
        for k, (a, b) in enumerate(parts):
            units.append({"file": rel, "ranges": [(a, b)], "bytes": excerpt_bytes(lines, [(a, b)]), "items": item_count.get(rel, 0),
                          "stems": stems(rel), "part": k, "parts": len(parts)})
    stem_count = Counter(s for u in units for s in set(u["stems"]))
    for u in units:
        u["key"] = max(u["stems"], key=lambda s: (stem_count[s], -u["stems"].index(s)))
    units.sort(key=lambda u: (u["key"], os.path.dirname(u["file"]), u["file"], u["ranges"][0][0]))
    areas, cur, cur_bytes = [], [], 0
    for u in units:
        # o primeiro pedaço de um arquivo grande começa uma área nova (os pedaços do mesmo arquivo ficam em áreas seguidas)
        if cur and (cur_bytes + u["bytes"] > budget or (u["parts"] > 1 and u["part"] == 0)):
            areas.append(cur)
            cur, cur_bytes = [], 0
        cur.append(u)
        cur_bytes += u["bytes"]
    if cur:
        areas.append(cur)
    out, names = [], Counter()
    base_num = next_base(used_ids)
    step = 100
    common = common_prefix_tokens([u["file"] for u in units])
    parts_of = Counter(u["file"] for u in units)
    main_seen = Counter()
    for idx, area in enumerate(areas):
        # nome = o arquivo que mais pesa na área, sem o prefixo comum do módulo (chk_reg_checklist → reg-checklist)
        weight = Counter()
        for u in area:
            weight[u["file"]] += u["bytes"]
        main = weight.most_common(1)[0][0]
        name = slug(file_label(main, common)) or f"area{idx + 1}"
        if parts_of[main] > 1:
            main_seen[main] += 1
            name = f"{name}-p{main_seen[main]}"
        names[name] += 1
        if names[name] > 1:
            name = f"{name}-{names[name]}"
        lo = base_num + idx * step + 1
        merged = OrderedDict()
        for u in area:
            merged.setdefault(u["file"], []).extend([a + 1, b + 1] for a, b in u["ranges"])
        out.append({"name": name, "units": [{"file": f, "ranges": r} for f, r in merged.items()],
                    "bytes": sum(u["bytes"] for u in area), "files": list(merged.keys()), "idRange": [lo, lo + step - 1]})
    return out


def common_prefix_tokens(paths):
    """Palavras que começam o nome de mais da metade dos arquivos (a sigla do módulo: chk_, sa3_…) — saem do nome da área."""
    firsts = Counter(re.split(r"[_.-]", os.path.basename(p).lower())[0] for p in paths)
    return {t for t, n in firsts.items() if n * 2 > len(paths) and len(t) <= 5}


def file_label(rel, common):
    base = os.path.splitext(os.path.basename(rel))[0]
    base = re.sub(r"\.(component|service|module|page|routes|model)$", "", base)
    tokens = [t for t in re.split(r"[_.-]", base) if t]
    while tokens and tokens[0].lower() in common:
        tokens = tokens[1:]
    label = "_".join(tokens) or base
    label = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "-", label)  # ChecklistController → Checklist-Controller
    return re.sub(r"-?(Controller|Service|Repository|Helper)$", r"-\1", label).lower().replace("_", "-")


def next_base(used_ids):
    """Primeira centena livre depois do maior número de ID já usado no módulo (qualquer tipo)."""
    nums = [int(m.group(1)) for i in used_ids for m in [re.match(r"^[A-Z]{2,4}-(\d+)", i.strip())] if m]
    top = max(nums, default=0)
    return ((top // 100) + 1) * 100 if top else 0


def slug(text):
    t = re_tool.norm(text).replace(" ", "-").replace("/", "-").replace(".", "-")
    return re.sub(r"-+", "-", t).strip("-")[:40] or "area"


def area_items(inv, area, doc_type):
    """Itens do inventário que caem nos trechos da área (o que a parte precisa cobrir)."""
    cats = set(re_tool.DOC_CATEGORIES.get(doc_type, []))
    spans = defaultdict(list)
    for u in area["units"]:
        spans[u["file"]] += [tuple(r) for r in u["ranges"]]
    out = []
    for it in inv["items"]:
        f = it.get("file") or ""
        if f in spans and (not cats or it["cat"] in cats) and any(a <= it["line"] <= b for a, b in spans[f]):
            out.append(it)
    return out


# ── pacote ────────────────────────────────────────────────────────────────────────────────────────

def tables_of_area(inv, area):
    spans = defaultdict(list)
    for u in area["units"]:
        spans[u["file"]] += [tuple(r) for r in u["ranges"]]
    names = set()
    for it in inv["items"]:
        if it["cat"] == "tabela" and it.get("file") in spans and any(a <= it["line"] <= b for a, b in spans[it["file"]]):
            names.add(it["name"].upper())
    return sorted(names)


def build_pack(inv, plan, area, out_dir, banco_dir, parte_dir):
    roots = source_roots(inv)
    cache = {}
    doc_type = plan["docType"]
    items = area_items(inv, area, doc_type)
    lo, hi = area["idRange"]
    parte = os.path.join(parte_dir or out_dir, f"parte-{area['name']}.md")
    L = [f"# Pacote de leitura — {plan['module']} / {doc_type} / área `{area['name']}`", "",
         f"- **Faixa de números de ID desta área:** {lo:03d} a {hi:03d} (todos os tipos: RN-{lo:03d}, UC-{lo:03d}, TELA-{lo:03d}…). Item que já",
         "  existe no publicado mantém o ID dele.",
         f"- **Grave em:** `{parte}` (mesmas seções `##` do modelo; a cada ~{plan.get('checkpointEvery', 10)} itens — se o arquivo já existe, continue dele).",
         f"- **Arquivos:** {len(area['files'])} · trechos com o número da linha real (use-o no `**Onde:** arquivo:linha`)."]
    allowed, taken = area_kinds(plan)
    if plan.get("kinds"):
        if "API" in allowed:
            L.append("- **API:** só os endpoints cuja ROTA/controller está neste pacote. **INT:** só com outro módulo Solvace, quando a "
                     "chamada está neste pacote — integração com serviço externo/tecnologia (`ext:`) é do especial `modulo`.")
        L.append(f"- **Tipos que esta área cria:** {', '.join(allowed)}. **Não crie** {', '.join(taken) or '—'} (têm subagente próprio ou são da "
                 "sessão principal: cite pelo nome) nem tipos de outros documentos (tela, endpoint, tabela… no documento certo — aqui só referencie).")
    L.append("")
    if items:
        L += ["## Itens do inventário que esta área precisa cobrir", ""]
        by_cat = defaultdict(list)
        for it in items:
            by_cat[it["cat"]].append(it)
        for cat in sorted(by_cat):
            L.append(f"- **{cat}** ({len(by_cat[cat])}): " + " · ".join(f"{it['name'][:90]} ({os.path.basename(it['file'])}:{it['line']})" for it in by_cat[cat][:60])
                     + (" …" if len(by_cat[cat]) > 60 else ""))
        L.append("")
    L += ["## Código (trechos)", ""]
    for u in area["units"]:
        path = resolve_file(u["file"], roots, cache)
        lines = read_lines(path) if path else []
        total = len(lines)
        for a, b in u["ranges"]:
            ext = os.path.splitext(u["file"])[1].lower()
            L.append(f"### `{u['file']}` — linhas {a}-{b} de {total}")
            L.append("```" + LANG.get(ext, ""))
            for k in range(a - 1, min(b, total)):
                L.append(f"{k + 1:>5}| {lines[k]}")
            L.append("```")
            L.append("")
    support = support_blocks(inv, area) + shared_blocks(inv, area)
    if support:
        L += ["## Apoio — ações chamadas por esta área que estão em outros arquivos (só o método)", ""]
        for f, a, b, lines in support:
            ext = os.path.splitext(f)[1].lower()
            L.append(f"### `{f}` — linhas {a + 1}-{b + 1} (apoio)")
            L.append("```" + LANG.get(ext, ""))
            for k in range(a, b + 1):
                L.append(f"{k + 1:>5}| {lines[k]}")
            L += ["```", ""]
    tables = tables_of_area(inv, area)
    if banco_dir and tables:
        L += ["## Tabelas da área (banco de referência)", ""]
        for t in tables:
            f = os.path.join(banco_dir, "tabelas", f"{t}.md")
            if not os.path.exists(f):
                alt = [x for x in os.listdir(os.path.join(banco_dir, "tabelas")) if x.upper() == f"{t}.MD"] if os.path.isdir(os.path.join(banco_dir, "tabelas")) else []
                f = os.path.join(banco_dir, "tabelas", alt[0]) if alt else None
            if f and os.path.exists(f):
                L += [open(f, encoding="utf-8", errors="replace").read().strip()[:5000], ""]
            else:
                L.append(f"- `{t}` (fora do catálogo do banco do módulo)")
        L.append("")
    text = "\n".join(L) + "\n"
    out = os.path.join(out_dir, f"pacote-{area['name']}.md")
    with open(out, "w", encoding="utf-8") as fh:
        fh.write(text)
    return out, len(text.encode("utf-8")), len(items)


# ── subagentes especiais: o que é do módulo inteiro (0066-ajustes) ─────────────────────────────────
# Áreas descreviam a mesma tabela/tecnologia/termo várias vezes e a sessão principal (Opus) passava 10–15 min juntando
# duplicados. O que é do módulo inteiro ganha um subagente próprio, despachado junto com as áreas; as áreas só criam os
# tipos delas (o cabeçalho do pacote lista quais).
SPECIALS = {
    "funcional": [("glossario", ["GLO"])],
    "arquitetura": [("banco", ["DB", "SQL", "TRG"]), ("modulo", ["TEC", "CMP", "CFG", "NFR", "ADR", "INF"])],
}
# tipos que a sessão principal consolida (das listas que os subagentes devolvem no resumo)
MAIN_KINDS = {"funcional": ["PRF"]}


def area_kinds(plan):
    """Tipos que uma área pode criar = os do documento − os dos especiais − os da sessão principal (+ GAP)."""
    doc = plan.get("docType", "")
    kinds = plan.get("kinds") or []
    taken = {k for _, ks in SPECIALS.get(doc, []) for k in ks} | set(MAIN_KINDS.get(doc, []))
    return [k for k in kinds if k not in taken] + ["GAP"], sorted(taken)


def next_range(plan):
    """Próxima faixa livre de IDs (depois das áreas e dos especiais já alocados)."""
    used = [a["idRange"][1] for a in plan["areas"]] + [x["idRange"][1] for x in plan.get("specials", [])]
    lo = (max(used) + 1) if used else plan.get("idBase", 0) + 1
    return [lo, lo + 99]


def add_special(plan, name, kinds, extra=None):
    plan.setdefault("specials", [])
    plan["specials"] = [x for x in plan["specials"] if x["name"] != name]
    entry = {"name": name, "kinds": kinds, "idRange": next_range(plan)}
    entry.update(extra or {})
    plan["specials"].append(entry)
    return entry


def special_header(plan, entry, parte_dir, out_dir, what, sections):
    lo, hi = entry["idRange"]
    parte = os.path.join(parte_dir or out_dir, f"parte-{entry['name']}.md")
    return [f"# Pacote especial `{entry['name']}` — {plan['module']} / {plan['docType']} ({what})", "",
            f"- **Faixa de números de ID:** {lo:03d} a {hi:03d}. **Tipos que você cria:** {', '.join(entry['kinds'])} (+ GAP). Item já "
            "publicado mantém o ID.",
            f"- **Grave em:** `{parte}` com as seções `##` do modelo: {sections}. Checkpoint a cada ~10 itens.",
            f"- Ao terminar: `re.sh faltando {plan['module']} {plan['docType']} {entry['name']}`.", ""]


def banco_packs(plan, banco_dir, out_dir, parte_dir, budget_kb):
    """Arquitetura: tabelas, views/procedures/functions, triggers e jobs do SQL Agent do catálogo (retrato) — um ou mais pacotes."""
    cat_path = os.path.join(banco_dir or "", "catalogo.json")
    if not banco_dir or not os.path.exists(cat_path):
        return []
    cat = json.load(open(cat_path, encoding="utf-8"))
    blocks = []
    for o in sorted(cat.get("objects", []), key=lambda x: (x["kind"] != "tabela", x["name"])):
        files = o.get("files") or [o.get("file")]
        body = []
        for f in files:
            fp = os.path.join(banco_dir, f) if f else None
            if fp and os.path.exists(fp):
                body.append(open(fp, encoding="utf-8", errors="replace").read().strip()[:12000])
        head = (f"### {o['kind']} {o['name']} — escopo {o['scope']}{' · DIVERGENTE entre locais' if o.get('divergent') else ''} · {o['reason']}"
                + (f" · usa: {', '.join(o.get('uses', [])[:12])}" if o.get("uses") else "")
                + (f" · usado por: {', '.join(o.get('usedBy', [])[:12])}" if o.get("usedBy") else ""))
        text = head + "\n" + ("```sql\n" + "\n".join(body) + "\n```" if o["kind"] != "tabela" else "\n".join(body)) + "\n"
        blocks.append((o["name"], o["kind"], text))
    for j in cat.get("jobs", []):
        fp = os.path.join(banco_dir, j.get("file", ""))
        body = open(fp, encoding="utf-8", errors="replace").read()[:8000] if os.path.exists(fp) else ""
        blocks.append((j["name"], "job", f"### job {j['name']} — {j.get('schedule', '')}\n{body}\n"))
    budget = budget_kb * 1024
    packs, cur, size = [], [], 0
    for b in blocks:
        if cur and size + len(b[2].encode()) > budget:
            packs.append(cur)
            cur, size = [], 0
        cur.append(b)
        size += len(b[2].encode())
    if cur:
        packs.append(cur)
    out = []
    for k, objs in enumerate(packs, 1):
        name = "banco" if len(packs) == 1 else f"banco-p{k}"
        entry = add_special(plan, name, ["DB", "SQL", "TRG", "JOB"], {"objects": [n for n, _, _ in objs]})
        L = special_header(plan, entry, parte_dir, out_dir, "objetos do banco de referência",
                           "`## Dados` (DB, por tabela), `## Objetos de banco` (SQL), `## Triggers` (TRG), `## Jobs e rotinas` (JOB do SQL Agent) "
                           "e `## Lacunas e pontos a confirmar`")
        L += ["Um item por objeto: para que serve, colunas-chave/PK/FK/checks (regras no banco), escopo (global/local, divergente), quem "
              "usa (outros módulos → `**Módulos:**` com a chave), e o que trigger/job faz \"escondido\". **Onde:** `banco DEMO <escopo> · "
              "dbo.<objeto>`. As áreas citam as tabelas pelo nome — a descrição é sua.", ""]
        if cat.get("problems") and k == 1:
            L += ["## Lacunas da coleta (viram GAP — nunca escreva que não existe o que não foi lido)", ""] + [f"- {p}" for p in cat["problems"]] + [""]
        L += [t for _, _, t in objs]
        path = os.path.join(out_dir, f"pacote-{name}.md")
        with open(path, "w", encoding="utf-8") as fh:
            fh.write("\n".join(L) + "\n")
        out.append((path, len(objs)))
    return out


def modulo_pack(plan, inv, out_dir, parte_dir, moddir):
    """Arquitetura: o que é do módulo inteiro — tecnologias, configuração (só nomes), segurança, observabilidade e infra."""
    entry = add_special(plan, "modulo", ["TEC", "CMP", "CFG", "NFR", "ADR", "INF", "INT (só ext:)"])
    L = special_header(plan, entry, parte_dir, out_dir, "módulo inteiro",
                       "`## Tecnologias e componentes` (TEC/CMP), `## Configuração e segredos (só nomes)` (CFG), `## Segurança e autenticação`, "
                       "`## Observabilidade e diagnóstico`, `## Infraestrutura e AWS (opcional)` (INF), `## Decisões e requisitos` (ADR/NFR) e lacunas")
    L += ["Nunca valores de segredo (senha, connection string, token): só o NOME da chave e para que serve.",
          "As integrações com serviços externos/tecnologias (`INT` com `**Módulos:** ext:s3`, `ext:redis`, Cognito, DynamoDB, SES…) "
          "são suas — uma por serviço, com os usos (as áreas não criam).", ""]
    files = set()
    for s in inv.get("sources", []):
        p = s.get("path") or ""
        if os.path.isdir(p):
            for base, dirs, fs in os.walk(p):
                dirs[:] = [d for d in dirs if d.lower() not in SKIP_PARTS and not d.startswith(".") and not re_tool.is_test_dir(d)]
                for f in fs:
                    if re.match(r"(appsettings.*\.json|web\.config|global\.asa|.*\.csproj|startup\.cs|program\.cs|serverless.*|buildspec.*|_inc_.*\.asp|default\.asp)$", f, re.I):
                        files.add(os.path.join(base, f))
    L += tech_signals(inv)
    keys = sorted({it["name"] for it in inv["items"] if it["cat"] == "config"})
    if keys:
        L += ["## Chaves de configuração citadas no código (nomes)", "", ", ".join(keys[:300]), ""]
    for fp in sorted(files)[:40]:
        lines = read_lines(fp)
        text = "\n".join(lines[:400])
        text = re.sub(r'(?i)((?:password|pwd|senha|secret|token|key|connectionstring)[^=:]{0,30}[=:]\s*["\']?)[^"\'\s;<]+', r"\1***", text)
        L += [f"### `{os.path.relpath(fp, re_tool.repo_root(fp))}`", "```", text[:12000], "```", ""]
    infra = os.path.join(moddir or "", "infra", "modulo.md")
    if os.path.exists(infra):
        L += ["## Infra na AWS (re.sh infra — retrato)", "", open(infra, encoding="utf-8", errors="replace").read()[:20000], ""]
    roots_sh = shared_roots(inv)
    if roots_sh:
        L += ["## Bibliotecas compartilhadas do repositório usadas pelo módulo", "", ", ".join(os.path.relpath(r, re_tool.repo_root(r)) for r in roots_sh), ""]
    path = os.path.join(out_dir, "pacote-modulo.md")
    with open(path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(L) + "\n")
    return path


R_TECH = [
    ("includes do ASP", re.compile(r'#include\s+(?:file|virtual)\s*=\s*"([^"]+)"', re.I)),
    ("componentes COM (Server.CreateObject)", re.compile(r'CreateObject\(\s*"([^"]+)"', re.I)),
    ("scripts carregados pelas telas", re.compile(r'<script[^>]+src\s*=\s*["\']([^"\'?#]+)', re.I)),
    ("estilos carregados", re.compile(r'<link[^>]+href\s*=\s*["\']([^"\'?#]+\.css)', re.I)),
    ("namespaces .NET (using)", re.compile(r'^\s*using\s+([A-Z][\w.]+)\s*;', re.M)),
    ("pacotes NuGet", re.compile(r'PackageReference\s+Include="([^"]+)"(?:\s+Version="([^"]+)")?', re.I)),
    ("serviços AWS no código", re.compile(r'\b(Amazon\.\w+|AWSSDK\.\w+|S3Client|SQSClient|SNSClient|SimpleEmail\w*)', re.I)),
]


def tech_signals(inv):
    """Sinais de tecnologia do módulo, contados no código (para o subagente `modulo` escrever TEC/CMP sem ler tudo)."""
    found = defaultdict(Counter)
    example = {}
    for s in inv.get("sources", []):
        p = s.get("path") or ""
        files = [p] if os.path.isfile(p) else [os.path.join(b, f) for b, ds, fs in os.walk(p) for f in fs
                                                if not any(x.lower() in SKIP_PARTS for x in os.path.relpath(b, p).split(os.sep))] if os.path.isdir(p) else []
        for fp in files:
            if os.path.splitext(fp)[1].lower() not in CODE_EXT | {".csproj"} or re_tool.is_test_file(os.path.basename(fp)) \
                    or any(re_tool.is_test_dir(x) for x in os.path.relpath(fp, p if os.path.isdir(p) else os.path.dirname(p)).split(os.sep)[:-1]):
                continue
            text = "\n".join(read_lines(fp))
            for label, rx in R_TECH:
                for m in rx.finditer(text):
                    v = " ".join(g for g in m.groups() if g)
                    if label.startswith("namespaces") and v.startswith(("System", "Microsoft.AspNetCore.Mvc")):
                        v = ".".join(v.split(".")[:2])
                    found[label][v] += 1
                    example.setdefault((label, v), f"{os.path.basename(fp)}:{text.count(chr(10), 0, m.start()) + 1}")
    if not found:
        return []
    L = ["## Sinais de tecnologia no código (contagem · exemplo)", ""]
    for label, _ in R_TECH:
        if found[label]:
            L.append(f"- **{label}:** " + "; ".join(f"{v} ×{n} ({example[(label, v)]})" for v, n in found[label].most_common(40)))
    return L + [""]


def especiais(plan, out_dir, parte_dir, moddir, budget_kb):
    """Gera os pacotes especiais do documento (funcional: glossário; arquitetura: banco e módulo)."""
    doc = plan["docType"]
    plan["specials"] = []
    made = []
    if doc == "funcional":
        termos = os.path.join(moddir, "inventario-termos.json")
        if os.path.exists(termos):
            out, n = glossary_pack(plan, termos, out_dir, parte_dir, os.path.join(moddir, "banco"))
            made.append(("glossario", out, n))
    if doc == "arquitetura":
        for out, n in banco_packs(plan, os.path.join(moddir, "banco"), out_dir, parte_dir, budget_kb):
            made.append((os.path.basename(out)[len("pacote-"):-3], out, n))
        made.append(("modulo", modulo_pack(plan, load_inv(plan["inventory"]), out_dir, parte_dir, moddir), 0))
    return made


# ── documentos de síntese (visão, spec de arquitetura): sem áreas, a partir dos levantamentos ─────────
# A visão e a spec de arquitetura são escritas A PARTIR do levantamento funcional/arquitetura (o modelo diz). Dividir o
# código inteiro em áreas para elas custava o mesmo que o funcional para um documento de ~30 KB.
SYNTHESIS = {
    "visao": {"full": ["PRF", "REL", "OBJ", "PER"], "titles": ["FN", "UC", "GLO", "INT", "EST", "NTF", "TELA", "FLX", "CFG"],
              "sections": ["resumo do modulo", "integracoes com outros modulos", "legado"]},
    "spec-arquitetura": {"full": ["INT", "EVT", "JOB", "TRG", "NFR", "ADR", "INF"],
                         "titles": ["TEC", "CMP", "API", "DB", "SQL", "UC", "RN", "EST", "TELA", "CFG"],
                         "sections": ["resumo", "visao geral", "integracoes", "seguranca", "observabilidade"]},
}


def blocks_by_id(text):
    """[(ID, tipo, título, bloco)] dos itens de um documento (cabeçalho com ID até o próximo cabeçalho)."""
    out, cur, buf = [], None, []
    for line in text.splitlines():
        m = re_tool.ID_HEADING.match(line)
        if m or re.match(r"^#{1,3}\s", line):
            if cur:
                out.append((*cur, "\n".join(buf).strip()))
            cur = (f"{m.group(2).upper()}-{int(m.group(3)):03d}", m.group(2).upper(), line.split("—", 1)[-1].strip()) if m else None
            buf = [line]
        elif cur:
            buf.append(line)
    if cur:
        out.append((*cur, "\n".join(buf).strip()))
    return out


def named_sections(text, wanted):
    """Seções ## cujo título contém uma das palavras (sem acento), com o texto até a próxima ## (sem os itens ###)."""
    out, cur, buf = [], None, []
    for line in text.splitlines() + ["## __fim__"]:
        if re.match(r"^##\s", line) and not line.startswith("###"):
            if cur and buf:
                body = "\n".join(x for x in buf if not re_tool.ID_HEADING.match(x))[:6000]
                out.append(f"{cur}\n{body.strip()}")
            title = re_tool.norm(line[3:])
            cur = line if any(w in title for w in wanted) else None
            buf = []
        elif cur:
            if re_tool.ID_HEADING.match(line):
                cur_items = True
            buf.append(line)
    return out


def sintese_pack(doc_type, sources, out_dir, parte_dir, module, plan):
    """Pacote de síntese: itens dos levantamentos (inteiros os que a síntese usa; os demais só ID + título)."""
    spec = SYNTHESIS[doc_type]
    entry = add_special(plan, "sintese", plan.get("kinds") or [])
    lo, hi = entry["idRange"]
    parte = os.path.join(parte_dir or out_dir, "parte-sintese.md")
    L = [f"# Pacote de síntese — {module} / {doc_type}", "",
         "- **Documento de síntese:** escreva A PARTIR dos levantamentos abaixo (o modelo pede isso). **Não leia o código** —",
         "  só para confirmar um ponto que os itens não explicam (e aí leia o trecho citado no `**Onde:**`).",
         f"- **Faixa de números de ID** para itens novos: {lo:03d} a {hi:03d}. Referencie os itens dos levantamentos pelo ID (não redefina).",
         f"- **Grave em:** `{parte}` com TODAS as seções `##` do modelo. Checkpoint a cada seção.",
         "- Para ler um item inteiro que aqui só tem o título: `grep -n -A25 '^### RN-012 ' <fonte>` (a fonte de cada documento está abaixo).", ""]
    for doc, path in sources.items():
        text = open(path, encoding="utf-8", errors="replace").read()
        items = blocks_by_id(text)
        L += [f"## Fonte: {doc} (`{path}`) — {len(items)} itens", ""]
        for sec in named_sections(text, spec["sections"]):
            L += [sec, ""]
        full = [b for b in items if b[1] in spec["full"]]
        if full:
            L += [f"### Itens inteiros ({', '.join(sorted({b[1] for b in full}))})", ""] + [b[3] for b in full] + [""]
        by_kind = defaultdict(list)
        for b in items:
            if b[1] in spec["titles"]:
                by_kind[b[1]].append(f"{b[0]} {b[2][:110]}")
        for k in spec["titles"]:
            if by_kind[k]:
                L += [f"**{k}** ({len(by_kind[k])}): " + " · ".join(by_kind[k]), ""]
    out = os.path.join(out_dir, "pacote-sintese.md")
    with open(out, "w", encoding="utf-8") as fh:
        fh.write("\n".join(L) + "\n")
    return out


# ── glossário (subagente próprio, em paralelo com as áreas) ─────────────────────────────────────────

def glossary_pack(plan, termos_path, out_dir, parte_dir, banco_dir=None):
    """
    0066-ajustes: o glossário é do módulo inteiro e era escrito por último, em sequência, pela sessão principal. Aqui ele
    vira um pacote próprio — os termos (com traduções e onde aparecem, com 3 linhas de contexto) — para um subagente
    escrever junto com as áreas.
    """
    inv = load_inv(plan["inventory"])
    terms = [it for it in json.load(open(termos_path, encoding="utf-8")).get("items", []) if it["cat"] == "termo"]
    main_terms = [t for t in terms if "(traducao)" not in (t.get("detail") or "")]
    translations = defaultdict(list)
    for t in terms:
        m = re.match(r'^(PT|EN|ES) de "(.+)" \(traducao\)', t.get("detail") or "")
        if m:
            translations[m.group(2)].append(f"{m.group(1)}: {t['name']}")
    roots, cache = source_roots(inv), {}
    entry = add_special(plan, "glossario", ["GLO"], {"terms": len(main_terms)})
    lo = entry["idRange"][0]
    parte = os.path.join(parte_dir or out_dir, "parte-glossario.md")
    L = [f"# Pacote do glossário — {plan['module']} / {plan['docType']}", "",
         f"- **Faixa de números de ID:** {lo:03d} a {lo + 99:03d} (`GLO-{lo:03d}`…; `GAP` também). Termo já publicado mantém o ID.",
         f"- **Grave em:** `{parte}` com as seções `## Glossário` (um `### GLO-… — <termo como o usuário vê>` por conceito) e",
         "  `## Lacunas e pontos a confirmar` (um `GAP` \"Termos fora do glossário\" com `**Termos:** a, b, c` para o que é genérico de",
         "  interface ou de outro módulo — assim conta como coberto).",
         "- Cada GLO: **Sinônimos:** (sigla, nome antigo, as traduções PT/EN/ES abaixo, nome da tabela), o significado NO DOMÍNIO",
         "  (o que é para quem usa) e onde aparece (`arquivo:linha`, tela). Junte no mesmo GLO os termos que são o mesmo conceito.",
         f"- Ao terminar: `re.sh faltando {plan['module']} {plan['docType']} glossario` (cobertura dos termos).", "",
         f"## Termos ({len(main_terms)})", ""]
    for t in main_terms:
        extra = "; ".join(translations.get(t["name"], []))
        L.append(f"### {t['name']}")
        L.append(f"- fonte: {t.get('detail', '')}{(' · traduções: ' + extra) if extra else ''} · `{t['file']}:{t['line']}`")
        path = resolve_file(t["file"], roots, cache) if not t["file"].startswith("banco/") else None
        if path:
            lines = read_lines(path)
            a, b = max(0, t["line"] - 2), min(len(lines), t["line"] + 1)
            snippet = " ⏎ ".join(x.strip()[:160] for x in lines[a:b] if x.strip())
            if snippet:
                L.append(f"- contexto: `{snippet[:400]}`")
        L.append("")
    if banco_dir and os.path.exists(os.path.join(banco_dir, "catalogo.json")):
        cat = json.load(open(os.path.join(banco_dir, "catalogo.json"), encoding="utf-8"))
        menus = [f"{m.get('grp')} › {m.get('item')}" for m in cat.get("menus", [])]
        if menus:
            L += ["## Menus do módulo (banco)", "", "; ".join(menus[:80]), ""]
    out = os.path.join(out_dir, "pacote-glossario.md")
    with open(out, "w", encoding="utf-8") as fh:
        fh.write("\n".join(L) + "\n")
    return out, len(main_terms)


# ── cartão ────────────────────────────────────────────────────────────────────────────────────────

def cartao(sub_md, modelo_md, modulos_tsv, out):
    parts = [open(sub_md, encoding="utf-8").read().strip(), "", "---", "", "# Modelo do documento (seções ## obrigatórias e formato de cada item)", ""]
    parts.append(open(modelo_md, encoding="utf-8").read().strip() if os.path.exists(modelo_md) else "(modelo.md ausente — rode re.sh start)")
    if modulos_tsv and os.path.exists(modulos_tsv):
        rows = [r.split("\t") for r in open(modulos_tsv, encoding="utf-8").read().splitlines() if r.strip()]
        parts += ["", "# Chaves dos módulos (use em **Módulos:** — ou ext:<serviço>)", ""]
        parts += [f"- `{r[0]}` {r[1] if len(r) > 1 else ''}{(' · ' + r[2]) if len(r) > 2 and r[2] else ''}" for r in rows]
    with open(out, "w", encoding="utf-8") as fh:
        fh.write("\n".join(parts) + "\n")
    return out


# ── faltando ──────────────────────────────────────────────────────────────────────────────────────

def faltando(inv, plan, area_name, parte, max_show=80):
    if area_name == "glossario":  # 0066-ajustes: cobertura dos termos (só os títulos/sinônimos dos GLO contam)
        termos = os.path.join(os.path.dirname(plan["inventory"]), "inventario-termos.json")
        items = json.load(open(termos, encoding="utf-8")).get("items", []) if os.path.exists(termos) else []
        doc = open(parte, encoding="utf-8").read() if os.path.exists(parte) else ""
        if not doc:
            print(f"Parte do glossário ainda não existe ({parte}): {len(items)} termos a cobrir.")
            return 0
        res = re_tool.coverage({"items": items}, doc, "funcional", 5000)
        print(f"Glossário: {res['covered']}/{res['total']} termos cobertos" + (f" = {res['ratio']:.0%}" if res["ratio"] is not None else ""))
        for it in res["missing"][:max_show]:
            print(f"  FALTA {it['name'][:80]} ({it.get('detail', '')[:60]})")
        return 0
    special = next((x for x in plan.get("specials", []) if x["name"] == area_name), None)
    if special and area_name != "glossario":
        moddir = os.path.dirname(plan["inventory"])
        if area_name.startswith("banco"):
            bi = os.path.join(moddir, "banco", "inventario-banco.json")
            names = {n.upper() for n in special.get("objects", [])}
            items = [it for it in (json.load(open(bi, encoding="utf-8")).get("items", []) if os.path.exists(bi) else [])
                     if it["name"].upper() in names or it["cat"] == "constraint"]
        else:
            items = [it for it in inv["items"] if it["cat"] == "config" or it["cat"].startswith("aws-") or it["cat"] == "esteira"]
        doc = open(parte, encoding="utf-8").read() if os.path.exists(parte) else ""
        if not doc:
            print(f"Parte ainda não existe ({parte}): {len(items)} itens a cobrir.")
            return 0
        res = re_tool.coverage({"items": items}, doc, "arquitetura", 5000)
        print(f"{area_name}: {res['covered']}/{res['total']} cobertos" + (f" = {res['ratio']:.0%}" if res["ratio"] is not None else ""))
        for it in res["missing"][:max_show]:
            print(f"  FALTA [{it['cat']}] {it['name'][:90]}")
        return 0
    area = next((a for a in plan["areas"] if a["name"] == area_name), None)
    if not area:
        print(f"ERRO: área '{area_name}' não está em areas.json ({', '.join(a['name'] for a in plan['areas'])})", file=sys.stderr)
        return 2
    items = area_items(inv, area, plan["docType"])
    doc = open(parte, encoding="utf-8").read() if os.path.exists(parte) else ""
    sub = {"items": items}
    res = re_tool.coverage(sub, doc, plan["docType"], 5000) if doc else None
    if res is None:
        print(f"Parte ainda não existe ({parte}): {len(items)} itens a cobrir.")
        return 0
    # coverage só olha as categorias do documento; os itens da área fora delas contam pela evidência
    ids = re_tool.doc_ids(doc)
    print(f"Área {area_name}: {len(ids)} itens escritos · cobertura do inventário da área {res['covered']}/{res['total']}"
          + (f" = {res['ratio']:.0%}" if res["ratio"] is not None else ""))
    for it in res["missing"][:max_show]:
        print(f"  FALTA [{it['cat']}] {it['name'][:100]} — {it['file']}:{it['line']}")
    if res["missingCount"] > max_show:
        print(f"  … e mais {res['missingCount'] - max_show}")
    return 0


# ── evidência ─────────────────────────────────────────────────────────────────────────────────────

R_EVIDENCE = re.compile(r"`?([\w./\\-]+\.(?:cs|cshtml|razor|asp|aspx|ascx|inc|js|ts|html|sql|json|ya?ml|xml|config|vb|py)):(\d+)(?:-(\d+))?")
R_LITERAL = re.compile(r'"([^"\n]{12,200})"|“([^”\n]{12,200})”')


def literals(body):
    """Textos entre aspas, linha a linha e em pares (aspas sem par na linha não contam; nada de markdown dentro)."""
    out = []
    for line in body.splitlines():
        line = line.replace("“", '"').replace("”", '"')
        parts = line.split('"')
        if len(parts) < 3 or len(parts) % 2 == 0:
            continue
        for k in range(1, len(parts), 2):
            lit = parts[k].strip()
            if 12 <= len(lit) <= 200 and "`" not in lit and "**" not in lit:
                out.append(lit)
    return out


def file_index(roots):
    idx = defaultdict(list)
    for r in roots:
        for base, dirs, files in os.walk(r):
            dirs[:] = [d for d in dirs if d not in re_tool.SKIP_DIRS and not d.startswith(".") and not re_tool.is_test_dir(d)]
            for f in files:
                if os.path.splitext(f)[1].lower() in CODE_EXT:
                    idx[f.lower()].append(os.path.join(base, f))
    return idx


def locate(cited, idx):
    """Arquivo citado (às vezes só o fim do caminho) → candidatos no disco que terminam com ele."""
    cited = cited.replace("\\", "/").lstrip("./")
    cands = idx.get(os.path.basename(cited).lower(), [])
    if "/" in cited:
        tail = cited.lower()
        best = [c for c in cands if c.replace("\\", "/").lower().endswith(tail)]
        if best:
            return best
    return cands


def evidencia(doc_path, inv, out=None, max_show=40):
    doc = open(doc_path, encoding="utf-8").read()
    idx = file_index(source_roots(inv))
    blocks, current, buf = [], None, []
    for line in doc.splitlines():
        m = re_tool.ID_HEADING.match(line)
        if m or re.match(r"^#{1,2}\s", line):
            if current:
                blocks.append((current, "\n".join(buf)))
            current = f"{m.group(2).upper()}-{int(m.group(3)):03d}" if m else None
            buf = [line]
        elif current:
            buf.append(line)
    if current:
        blocks.append((current, "\n".join(buf)))
    lines_cache = {}
    checked = refs = 0
    problems = []
    for item, body in blocks:
        cited_files = []
        for m in R_EVIDENCE.finditer(body):
            refs += 1
            cands = locate(m.group(1), idx)
            if not cands:
                if not m.group(1).lower().startswith(("banco/", "infra/")):
                    problems.append({"item": item, "kind": "arquivo", "ref": f"{m.group(1)}:{m.group(2)}", "detail": "arquivo não encontrado nas fontes"})
                continue
            start = int(m.group(2))
            ok = False
            for c in cands:
                if c not in lines_cache:
                    lines_cache[c] = read_lines(c)
                if start <= len(lines_cache[c]):
                    ok = True
                    cited_files.append(c)
            checked += 1
            if not ok:
                problems.append({"item": item, "kind": "linha", "ref": f"{m.group(1)}:{start}",
                                 "detail": f"linha além do fim do arquivo ({max(len(lines_cache[c]) for c in cands)} linhas)"})
        if not cited_files:
            continue
        text = "\n".join("\n".join(lines_cache[c]) for c in set(cited_files))
        flat = re_tool.norm(text)
        for lit in literals(body):
            if not re.search(r"[A-Za-zÀ-ú]{3,}\s+[A-Za-zÀ-ú]{2,}", lit) or "{" in lit or "/" in lit[:2]:
                continue
            if re_tool.norm(lit)[:60] not in flat:
                problems.append({"item": item, "kind": "literal", "ref": lit[:80], "detail": "texto não encontrado no arquivo citado (traduzido? confira)"})
    result = {"refs": refs, "checked": checked, "problems": problems[:500], "problemCount": len(problems)}
    if out:
        with open(out, "w", encoding="utf-8") as fh:
            json.dump(result, fh, ensure_ascii=False, indent=1)
    by_kind = Counter(p["kind"] for p in problems)
    print(f"Evidência: {checked}/{refs} referências arquivo:linha conferidas nas fontes · {len(problems)} a conferir"
          + (" (" + ", ".join(f"{n} {k}" for k, n in by_kind.items()) + ")" if problems else ""))
    for p in problems[:max_show]:
        print(f"  CONFERIR {p['item']} [{p['kind']}] {p['ref']} — {p['detail']}")
    if len(problems) > max_show:
        print(f"  … e mais {len(problems) - max_show}")
    return result


# ── juntar (com o documento de base) ──────────────────────────────────────────────────────────────

def drop_items(text, ids):
    """Tira do texto os blocos dos itens com esses IDs (do cabeçalho até o próximo cabeçalho de nível igual ou maior)."""
    out, skip_level, fence = [], None, False
    for line in text.splitlines():
        stripped = line.lstrip()
        if stripped.startswith("```") and (fence or stripped.find("```", 3) < 0):
            fence = not fence
        if not fence:
            h = re.match(r"^(#{1,6})\s", line)
            if h:
                level = len(h.group(1))
                if skip_level is not None and level <= skip_level:
                    skip_level = None
                m = re_tool.ID_HEADING.match(line)
                if m and f"{m.group(2).upper()}-{int(m.group(3)):03d}" in ids:
                    skip_level = level
                    continue
        if skip_level is None:
            out.append(line)
    return "\n".join(out) + "\n"


def juntar(out, base, parts):
    """Base (publicado/rascunho) + partes das áreas: o item reescrito numa parte substitui o da base (modo melhorar)."""
    part_ids = set()
    for p in parts:
        part_ids.update(re_tool.doc_ids(open(p, encoding="utf-8").read()))
    inputs = []
    if base and os.path.exists(base):
        cleaned = out + ".base.tmp"
        with open(cleaned, "w", encoding="utf-8") as fh:
            fh.write(drop_items(open(base, encoding="utf-8").read(), part_ids))
        inputs.append(cleaned)
    dups = re_tool.join_parts(out, inputs + list(parts))
    if inputs:
        os.remove(inputs[0])
    print(f"Juntado em {out}: {len(parts)} parte(s){' sobre a base' if inputs else ''} · {len(part_ids)} itens das áreas")
    for i, where in dups.items():
        print(f"  AVISO: {i} definido em {', '.join(where)} — renumere numa das partes")
    return 1 if dups else 0


# ── itens repetidos entre partes (0066-ajustes4) ─────────────────────────────────────────────────────

DEDUPE_KINDS = {"API", "INT", "DB", "SQL", "TRG", "TEC", "CMP", "CFG", "GLO", "PRF", "EVT", "JOB", "INF"}
MAX_DOC_CHARS = 2_000_000   # o PRMake recusa acima disso (ArchitectureSection/ReverseRevision.MaxContentLength)


def dedupe(doc_path, out=None):
    """
    Itens do mesmo tipo com o MESMO título (sem acento/caixa) vindos de áreas diferentes: fica o mais completo, os outros
    saem e as referências locais passam a apontar para ele. Só tipos "do módulo" (endpoint, integração, tabela, tecnologia,
    configuração, termo, perfil…), onde título igual é o mesmo assunto.
    """
    text = open(doc_path, encoding="utf-8").read()
    groups = defaultdict(list)
    for iid, kind, title, body in blocks_by_id(text):
        if kind in DEDUPE_KINDS:
            key = re.sub(r"\s+", " ", re_tool.norm(re.sub(r"\(.*?\)$", "", title))).strip()
            if key:
                groups[(kind, key)].append((iid, len(body)))
    mapping = {}
    for (_, _), ids in groups.items():
        if len({i for i, _ in ids}) < 2:
            continue
        keep = max(ids, key=lambda x: x[1])[0]
        for i, _ in ids:
            if i != keep:
                mapping[i] = keep
    if not mapping:
        print("Repetidos: nenhum item repetido entre as partes.")
        return {}
    text = drop_items(text, set(mapping))

    def repl(m):
        key = f"{m.group(1)}-{int(m.group(2)):03d}"
        return mapping.get(key, m.group(0))
    with open(out or doc_path, "w", encoding="utf-8") as fh:
        fh.write(R_LOCAL_REF.sub(repl, text))
    by_kind = Counter(k.split("-")[0] for k in mapping)
    print(f"Repetidos: {len(mapping)} itens fundidos (" + ", ".join(f"{n} {k}" for k, n in sorted(by_kind.items())) + ") — referências apontam para o que ficou")
    return mapping


def size_report(doc_path):
    n = len(open(doc_path, encoding="utf-8").read())
    pct = n / MAX_DOC_CHARS
    print(f"Tamanho: {n:,} caracteres ({pct:.0%} do limite do PRMake de {MAX_DOC_CHARS:,})".replace(",", "."))
    if pct > 0.8:
        print("AVISO: perto do limite — confira itens repetidos e seções coladas por área antes de enviar (não compacte o texto das regras)")
    return n


# ── compactar IDs ─────────────────────────────────────────────────────────────────────────────────

R_LOCAL_REF = re.compile(r"(?<![#\w-])(TELA|PRF|EST|NTF|CFG|REL|TEC|CMP|API|EVT|JOB|INT|FLX|OBJ|PER|GLO|ADR|NFR|SEQ|GAP|SQL|TRG|INF|TUT|FAQ|FN|UC|RN|DB|UI)-(\d{1,4})\b")


def compactar(doc_path, base, used_ids, out=None):
    """
    IDs novos das áreas (número > base, faixas de 100 por área) viram a sequência seguinte de cada tipo (RN-2701 → RN-058),
    com todas as referências locais do documento trocadas junto. IDs já usados no módulo (≤ base) não mudam.
    """
    text = open(doc_path, encoding="utf-8").read()
    top = defaultdict(int)
    for i in list(used_ids) + re_tool.doc_ids(text):
        m = re.match(r"^([A-Z]{2,4})-(\d+)", i.strip())
        if m and int(m.group(2)) <= base:
            top[m.group(1)] = max(top[m.group(1)], int(m.group(2)))
    mapping = {}
    for i in re_tool.doc_ids(text):
        kind, num = i.split("-")
        if int(num) > base and i not in mapping:
            top[kind] += 1
            mapping[i] = f"{kind}-{top[kind]:03d}"
    if not mapping:
        print("Compactar: nenhum ID de área para renumerar.")
        return {}

    def repl(m):
        key = f"{m.group(1)}-{int(m.group(2)):03d}"
        return mapping.get(key, m.group(0))
    with open(out or doc_path, "w", encoding="utf-8") as fh:
        fh.write(R_LOCAL_REF.sub(repl, text))
    by_kind = Counter(k.split("-")[0] for k in mapping)
    print(f"Compactar: {len(mapping)} IDs renumerados (" + ", ".join(f"{n} {k}" for k, n in sorted(by_kind.items())) + ")")
    return mapping


# ── main ──────────────────────────────────────────────────────────────────────────────────────────

def opt(argv, name, default=None):
    return argv[argv.index(name) + 1] if name in argv and argv.index(name) + 1 < len(argv) else default


def opts(argv, name):
    return [argv[i + 1] for i, a in enumerate(argv) if a == name and i + 1 < len(argv)]


def load_inv(path, extras=()):
    inv = json.load(open(path, encoding="utf-8"))
    for x in extras:
        if os.path.exists(x):
            extra = json.load(open(x, encoding="utf-8"))
            seen = {(it["cat"], it["name"].upper()) for it in inv["items"]}
            inv["items"] += [it for it in extra.get("items", []) if (it["cat"], it["name"].upper()) not in seen]
    return inv


def main(argv):
    if len(argv) < 2 or argv[1] in ("-h", "--help"):
        print(__doc__)
        return 0
    cmd = argv[1]
    if cmd == "areas":
        inv_path, doc_type = argv[2], argv[3]
        inv = load_inv(inv_path, opts(argv, "--extra"))
        used = open(opt(argv, "--ids"), encoding="utf-8").read().split() if opt(argv, "--ids") and os.path.exists(opt(argv, "--ids")) else []
        budget = int(opt(argv, "--orcamento-kb", "90"))
        areas = [] if doc_type in SYNTHESIS else plan_areas(inv, doc_type, budget, int(opt(argv, "--arquivo-pequeno", "400")), int(opt(argv, "--bloco-max", "220")), used)
        plan = {"version": 1, "module": opt(argv, "--modulo", ""), "docType": doc_type, "inventory": os.path.abspath(inv_path), "idBase": next_base(used),
                "extras": [os.path.abspath(x) for x in opts(argv, "--extra") if os.path.exists(x)], "budgetKb": budget,
                "checkpointEvery": int(opt(argv, "--checkpoint", "10")), "areas": areas}
        out = opt(argv, "--out", "areas.json")
        with open(out, "w", encoding="utf-8") as fh:
            json.dump(plan, fh, ensure_ascii=False, indent=1)
        if doc_type in SYNTHESIS:
            print(f"Documento de SÍNTESE ({doc_type}): sem áreas — escrito a partir dos levantamentos (funcional/arquitetura). "
                  f"re.sh pacote monta o pacote de síntese -> {out}")
            return 0
        total = sum(a["bytes"] for a in areas)
        print(f"Áreas: {len(areas)} (orçamento {budget} KB; código a ler {total // 1024} KB) -> {out}")
        for a in areas:
            print(f"  {a['name']:<34} {a['bytes'] // 1024:>4} KB · {len(a['files']):>3} arquivos · IDs {a['idRange'][0]:03d}-{a['idRange'][1]:03d}")
        return 0
    if cmd == "pacote":
        plan = json.load(open(argv[2], encoding="utf-8"))
        out_dir = argv[3]
        os.makedirs(out_dir, exist_ok=True)
        inv = load_inv(plan["inventory"], plan.get("extras", []))
        if opt(argv, "--tipos"):  # tipos do documento (sessao.json) — o cabeçalho diz quais a área pode criar
            plan["kinds"] = [k.strip().upper() for k in opt(argv, "--tipos").split(",") if k.strip()]
            with open(argv[2], "w", encoding="utf-8") as fh:
                json.dump(plan, fh, ensure_ascii=False, indent=1)
        only = opt(argv, "--area")
        for area in plan["areas"]:
            if only and area["name"] != only:
                continue
            path, size, n = build_pack(inv, plan, area, out_dir, opt(argv, "--banco"), opt(argv, "--parte-dir"))
            print(f"  pacote {area['name']:<34} {size // 1024:>4} KB · {n:>4} itens a cobrir -> {path}")
        return 0
    if cmd == "sintese":
        plan_path = argv[2]
        plan = json.load(open(plan_path, encoding="utf-8"))
        sources = OrderedDict()
        for spec_ in opts(argv, "--fonte"):
            doc, _, path = spec_.partition("=")
            if path and os.path.exists(path) and os.path.getsize(path) > 0:
                sources[doc] = path
        if not sources:
            print("ERRO: nenhum levantamento (funcional/arquitetura) disponível — gere e publique (ou rascunhe) os levantamentos antes", file=sys.stderr)
            return 2
        out = sintese_pack(plan["docType"], sources, argv[3], opt(argv, "--parte-dir"), plan.get("module", ""), plan)
        with open(plan_path, "w", encoding="utf-8") as fh:
            json.dump(plan, fh, ensure_ascii=False, indent=1)
        print(f"  pacote sintese {os.path.getsize(out) // 1024:>4} KB · fontes: {', '.join(sources)} -> {out}")
        return 0
    if cmd == "especiais":
        plan_path = argv[2]
        plan = json.load(open(plan_path, encoding="utf-8"))
        made = especiais(plan, argv[3], opt(argv, "--parte-dir"), opt(argv, "--modulo-dir"), int(opt(argv, "--orcamento-kb", str(plan.get("budgetKb", 90)))))
        with open(plan_path, "w", encoding="utf-8") as fh:
            json.dump(plan, fh, ensure_ascii=False, indent=1)
        for name, out, n in made:
            print(f"  especial {name:<26} {os.path.getsize(out) // 1024:>4} KB{(' · ' + str(n) + ' objetos/termos') if n else ''} -> {out}")
        return 0
    if cmd == "glossario":
        plan_path = argv[2]
        plan = json.load(open(plan_path, encoding="utf-8"))
        out, n = glossary_pack(plan, argv[3], argv[4], opt(argv, "--parte-dir"), opt(argv, "--banco"))
        with open(plan_path, "w", encoding="utf-8") as fh:
            json.dump(plan, fh, ensure_ascii=False, indent=1)
        print(f"  pacote glossario {os.path.getsize(out) // 1024:>4} KB · {n:>4} termos a cobrir -> {out}")
        return 0
    if cmd == "cartao":
        print(cartao(argv[2], argv[3], argv[4] if len(argv) > 4 and not argv[4].startswith("--") else None, opt(argv, "--out", "cartao.md")))
        return 0
    if cmd == "faltando":
        plan = json.load(open(argv[2], encoding="utf-8"))
        inv = load_inv(plan["inventory"], plan.get("extras", []))
        return faltando(inv, plan, argv[3], argv[4])
    if cmd == "evidencia":
        inv = load_inv(argv[3])
        evidencia(argv[2], inv, opt(argv, "--json"), int(opt(argv, "--max", "40")))
        return 0
    if cmd == "juntar":
        parts = [a for a in argv[3:] if not a.startswith("--") and a != opt(argv, "--base")]
        return juntar(argv[2], opt(argv, "--base"), parts)
    if cmd == "repetidos":
        dedupe(argv[2], opt(argv, "--out"))
        return 0
    if cmd == "tamanho":
        size_report(argv[2])
        return 0
    if cmd == "compactar":
        used = open(opt(argv, "--ids"), encoding="utf-8").read().split() if opt(argv, "--ids") and os.path.exists(opt(argv, "--ids")) else []
        base = int(opt(argv, "--base")) if opt(argv, "--base") else json.load(open(opt(argv, "--areas"), encoding="utf-8")).get("idBase", 0)
        compactar(argv[2], base, used, opt(argv, "--out"))
        return 0
    print(f"comando desconhecido: {cmd}", file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
