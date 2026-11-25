#!/usr/bin/env python3
"""Índice técnico do módulo (feature 0097) — SEM LLM, sem rede: o inventário da origin/master vira o documento `tecnico`.

O inventário (re_tool.py inventario) já extrai do código rotas/endpoints, telas do front e páginas ASP, tabelas,
procedures, chaves de configuração e mensagens. Aqui ele vira um markdown com itens de ID estável, publicado direto na
Base (sem aprovação: não há texto de LLM) e regerável a qualquer hora — inclusive para os módulos do revamp sem ER.

  re_indice.py gerar <inventario.json> --modulo <chave> [--publicado tecnico.md] [--banco <pasta-banco>]
                     [--out tecnico.md] [--json resumo.json] [--data AAAA-MM-DD]
      --publicado: o índice publicado (IDs estáveis: a mesma chave tipo + símbolo mantém o ID; novos pegam o próximo).
      --banco: catálogo do banco de referência do módulo (views, functions, triggers e jobs entram como DBO).

Tipos (ReverseItemKinds, 0097): END endpoint/ação HTTP/página ajax · ROT rota do front (→ componente) e página .asp ·
TAB tabela · DBO procedure/view/function/trigger/job · CHV chave de configuração · MSG mensagem literal de validação/erro.
"""
import json
import os
import re
import sys
import unicodedata
from collections import Counter, OrderedDict, defaultdict
from datetime import date

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import re_tool  # noqa: E402  (inventário e caminho a partir da raiz do repositório)

# seções do modelo `tecnico` (ReverseDocTypes, 0097) — a ordem do documento
SECTIONS = OrderedDict([
    ("END", "Endpoints"),
    ("ROT", "Rotas e telas"),
    ("TAB", "Tabelas"),
    ("DBO", "Objetos do banco"),
    ("CHV", "Chaves de configuração"),
    ("MSG", "Mensagens"),
])
R_ITEM = re.compile(r"^###\s+(END|ROT|TAB|DBO|CHV|MSG)-(\d{1,4})\s+[—–-]+\s+(.+?)\s*$")
MAX_TITLE = 200
MAX_TABLES = 10
# mensagem que não é literal (nome de regra/atributo) — não entra como MSG
R_NOT_LITERAL = re.compile(r"^RuleFor\s|\[(Validators\.\w+|Required|MaxLength|StringLength|Range|RegularExpression|EmailAddress|MinLength)\]$")
R_COMPONENT = re.compile(r"\bcomponent\s*:\s*(\w+)|\bredirectTo\s*:\s*['\"]([^'\"]*)['\"]"
                         r"|load(?:Component|Children)\s*:.*?\.then\(\s*\(?\s*\w+\s*\)?\s*=>\s*\w+\.(\w+)", re.S)
# página .asp que é include (trecho compartilhado, não tela): _funcoes.asp, _inc_x.asp, inc_sistemas.asp
R_ASP_INCLUDE = re.compile(r"^(_|inc_)", re.I)
R_PATH = re.compile(r"\bpath\s*:")
DB_KIND = {"V": "view", "P": "procedure", "FN": "function", "IF": "function", "TF": "function", "TR": "trigger"}


def norm(text):
    t = unicodedata.normalize("NFKD", text or "")
    t = "".join(c for c in t if not unicodedata.combining(c)).lower()
    return re.sub(r"\s+", " ", t).strip()


def clip(text, n=MAX_TITLE):
    text = re.sub(r"\s+", " ", text or "").strip()
    return text if len(text) <= n else text[:n - 1] + "…"


class Index:
    def __init__(self, inv, module, generated, banco=None):
        self.inv = inv
        self.module = module
        self.generated = generated
        self.banco = banco
        self.sources = inv.get("sources", [])
        self.cache = {}

    # ── onde / commit ──────────────────────────────────────────────────────────────────────────

    def repo_of(self, it):
        s = self.sources[it["src"]] if isinstance(it.get("src"), int) and it["src"] < len(self.sources) else (self.sources[0] if self.sources else {})
        return s.get("repo") or os.path.basename((s.get("path") or "").rstrip("/")) or "?", (s.get("commit") or "")[:7]

    def where(self, it):
        repo, _ = self.repo_of(it)
        return f"{repo}/{re_tool.full_path(self.inv, it['file'], it.get('src'))}:{it['line']}"

    def commit(self, it):
        repo, sha = self.repo_of(it)
        return f"{repo}@{sha}" if sha else repo

    def lines(self, it):
        key = (it.get("src"), it["file"])
        if key not in self.cache:
            base = None
            srcs = self.sources
            order = [it["src"]] if isinstance(it.get("src"), int) and it["src"] < len(srcs) else range(len(srcs))
            for i in order:
                p = srcs[i].get("path") or ""
                cand = os.path.join(p, it["file"]) if os.path.isdir(p) else None
                if cand and os.path.isfile(cand):
                    base = cand
                    break
            try:
                if base:
                    with open(base, encoding="utf-8", errors="replace") as fh:
                        self.cache[key] = fh.read().splitlines()
                else:
                    self.cache[key] = []
            except OSError:
                self.cache[key] = []
        return self.cache[key]

    # ── itens por tipo: [(chave estável, título, linhas do corpo)] ─────────────────────────────

    def tables_by_file(self):
        out = defaultdict(set)
        for it in self.inv["items"]:
            if it["cat"] == "tabela" and it["name"].upper().startswith(("TB_", "VW_")):
                out[(it.get("src"), it["file"])].add(it["name"].upper())
        return out

    def build(self):
        items = self.inv["items"]
        tables = self.tables_by_file()
        rows = defaultdict(list)

        def body(it, extra=None, with_tables=False):
            b = [f"- **Onde:** `{self.where(it)}`"]
            if with_tables:
                t = sorted(tables.get((it.get("src"), it["file"]), ()))
                if t:
                    b.append("- **Tabelas:** " + ", ".join(t[:MAX_TABLES]) + (" …" if len(t) > MAX_TABLES else ""))
            if extra:
                b.append(f"- **Tags:** {clip(extra, 300)}")
            b.append(f"- **Commit:** {self.commit(it)}")
            return b

        def first(group):
            return sorted(group, key=lambda x: (x.get("src") or 0, x["file"], x["line"]))[0]

        # END — endpoints do back e páginas ajax do legado
        for it in items:
            if it["cat"] == "endpoint":
                rows["END"].append((it["name"], clip(it["name"]), body(it, it.get("detail"), True), it))
            elif it["cat"] == "pagina" and "ajax" in it["name"].lower():
                rows["END"].append((it["file"], clip(it["name"]), body(it, (it.get("detail") and "campos: " + it["detail"]), True), it))
        # ROT — rotas do front (→ componente) e páginas .asp
        for it in items:
            if it["cat"] == "rota-front":
                comp = self.route_component(it)
                title = f"/{it['name'].lstrip('/')}" + (f" → {comp}" if comp else "")
                rows["ROT"].append((f"{it['file']}#{it['name']}", clip(title), body(it), it))
            elif it["cat"] == "pagina" and "ajax" not in it["name"].lower() and not R_ASP_INCLUDE.search(it["name"]):
                rows["ROT"].append((it["file"], clip(it["name"]), body(it, (it.get("detail") and "campos: " + it["detail"]), True), it))
        # TAB — uma por tabela (onde: a primeira citação; onde mais aparece)
        by_table = defaultdict(list)
        for it in items:
            if it["cat"] == "tabela":
                by_table[it["name"].upper()].append(it)
        for name, group in by_table.items():
            it = first(group)
            files = sorted({os.path.basename(g["file"]) for g in group})
            extra = f"citada em {len(files)} arquivo(s): " + ", ".join(files[:6]) + (" …" if len(files) > 6 else "")
            if any(g.get("detail", "").startswith("DbSet") for g in group):
                extra += " · " + next(g["detail"] for g in group if g.get("detail", "").startswith("DbSet"))
            rows["TAB"].append((name, name, body(it, extra), it))
        # DBO — procedures citadas no código + objetos do catálogo do banco (quando houver)
        by_proc = defaultdict(list)
        for it in items:
            if it["cat"] == "procedure":
                by_proc[it["name"].upper()].append(it)
        for name, group in by_proc.items():
            it = first(group)
            files = sorted({os.path.basename(g["file"]) for g in group})
            rows["DBO"].append((name, f"procedure dbo.{group[0]['name']}", body(it, "chamada em: " + ", ".join(files[:6])), it))
        for obj in self.catalog_objects():
            key = obj["name"].upper()
            if any(k == key for k, *_ in rows["DBO"]):
                continue
            kind = obj.get("kindLabel")
            b = [f"- **Onde:** banco {obj.get('environment') or 'DEMO'} {obj.get('scope') or ''} · {obj.get('schema') or 'dbo'}.{obj['name']}".replace("  ", " ")]
            if obj.get("uses"):
                b.append("- **Tabelas:** " + ", ".join(sorted({u.upper() for u in obj["uses"] if u.upper().startswith("TB_")})[:MAX_TABLES]))
            if obj.get("modified"):
                b.append(f"- **Tags:** {kind}, alterado em {obj['modified'][:10]}")
            rows["DBO"].append((key, f"{kind} {obj.get('schema') or 'dbo'}.{obj['name']}", b, None))
        # CHV — chaves de configuração (só o nome; valores nunca)
        by_key = defaultdict(list)
        for it in items:
            if it["cat"] == "config":
                by_key[it["name"]].append(it)
        for name, group in by_key.items():
            it = first(group)
            files = sorted({os.path.basename(g["file"]) for g in group})
            rows["CHV"].append((name, clip(name), body(it, "em: " + ", ".join(files[:6])), it))
        # MSG — mensagens literais (validação/erro/tela)
        by_msg = defaultdict(list)
        for it in items:
            if it["cat"] == "validacao" and not R_NOT_LITERAL.search(it["name"]) and re.search(r"[A-Za-zÀ-ú]{3,}", it["name"]):
                by_msg[norm(it["name"])].append(it)
        for key, group in by_msg.items():
            it = first(group)
            title = '"' + clip(it["name"].replace('"', "'"), MAX_TITLE - 2) + '"'
            tags = sorted({g.get("detail") for g in group if g.get("detail")})
            rows["MSG"].append((key, title, body(it, ", ".join(tags[:4]) if tags else None), it))
        return rows

    def route_component(self, it):
        lines = self.lines(it)
        if not lines:
            return None
        start = it["line"] - 1
        block = [lines[start]]
        for k in range(start + 1, min(len(lines), start + 10)):  # até a próxima rota (o loadChildren quebra em várias linhas)
            if R_PATH.search(lines[k]):
                break
            block.append(lines[k])
        m = R_COMPONENT.search(" ".join(x.strip() for x in block))
        if not m:
            return None
        if m.group(2) is not None:
            return f"redireciona para /{m.group(2).lstrip('/')}"
        return m.group(1) or m.group(3)

    def catalog_objects(self):
        if not self.banco:
            return []
        path = os.path.join(self.banco, "catalogo.json")
        if not os.path.exists(path):
            return []
        cat = json.load(open(path, encoding="utf-8"))
        out = []
        for o in cat.get("objects", []):
            kind = DB_KIND.get((o.get("type") or "").strip())
            if kind:
                out.append(dict(o, kindLabel=kind, environment=cat.get("environment")))
        for j in cat.get("jobs", []):
            name = j.get("name") or j.get("job")
            if name:
                out.append({"name": name, "schema": "msdb", "scope": "SQL Agent", "kindLabel": "job", "environment": cat.get("environment"),
                            "uses": j.get("tables") or [], "modified": j.get("modified")})
        return out


def published_ids(text):
    """IDs do índice publicado por (tipo, símbolo normalizado) — a mesma chave mantém o ID."""
    ids, top = {}, Counter()
    for line in (text or "").splitlines():
        m = R_ITEM.match(line)
        if m:
            kind, num, title = m.group(1), int(m.group(2)), m.group(3)
            ids[(kind, norm(title))] = num
            top[kind] = max(top[kind], num)
    return ids, top


def render(rows, module, published_text, commits, generated):
    ids, top = published_ids(published_text)
    counts = Counter()
    L = [f"# Índice técnico — {module}", "",
         f"> Gerado sem IA pela skill (`re.sh indice {module}`) a partir do inventário da origin/master em {generated}"
         + (f" ({', '.join(commits)})" if commits else "") + ". Publicado direto, sem revisão humana; regerável a qualquer hora.",
         "> Para a regra de negócio, veja os levantamentos (RN/UC); aqui só onde cada coisa está no código.", ""]
    kept = reused = 0
    for kind, heading in SECTIONS.items():
        group = rows.get(kind, [])
        L += [f"## {heading}", ""]
        if not group:
            L += ["(nada no inventário)", ""]
            continue
        seen = Counter()
        prepared = []
        for key, title, body, _ in sorted(group, key=lambda r: (norm(r[1]), r[0])):
            seen[norm(title)] += 1
            prepared.append((key, title, body))
        dup = {t for t, n in seen.items() if n > 1}
        numbered = []
        for key, title, body in prepared:
            if norm(title) in dup:  # mesmo título (ex.: duas páginas com o mesmo nome): desambigua pelo caminho
                where = body[0].split("`")[1] if "`" in body[0] else key
                title = clip(f"{title} ({os.path.dirname(where.split(':')[0])})")
            num = ids.get((kind, norm(title)))
            if num is not None and num not in {n for n, *_ in numbered}:
                reused += 1
            else:
                num = None
            numbered.append((num, title, body))
        for i, (num, title, body) in enumerate(numbered):
            if num is None:
                top[kind] += 1
                numbered[i] = (top[kind], title, body)
        for num, title, body in sorted(numbered, key=lambda x: x[0]):
            if num > 9999:
                continue
            counts[kind] += 1
            kept += 1
            L.append(f"### {kind}-{num:03d} — {title}")
            L += body
            L.append("")
    return "\n".join(L).rstrip() + "\n", dict(counts), reused


def main(argv):
    if len(argv) < 3 or argv[1] != "gerar":
        print(__doc__)
        return 0 if len(argv) < 2 or argv[1] in ("-h", "--help") else 2
    opt = lambda n, d=None: argv[argv.index(n) + 1] if n in argv and argv.index(n) + 1 < len(argv) else d
    with open(argv[2], encoding="utf-8") as fh:
        inv = json.load(fh)
    module = opt("--modulo", "modulo")
    generated = opt("--data", date.today().isoformat())
    published = open(opt("--publicado"), encoding="utf-8").read() if opt("--publicado") and os.path.exists(opt("--publicado")) else ""
    idx = Index(inv, module, generated, opt("--banco"))
    rows = idx.build()
    commits, seen = [], set()
    for s in inv.get("sources", []):
        if s.get("repo") and (s["repo"], s.get("commit")) not in seen:
            seen.add((s["repo"], s.get("commit")))
            commits.append({"repository": s["repo"], "commit": s.get("commit") or "", "branch": s.get("branch") or ""})
    text, counts, reused = render(rows, module, published, [f"{c['repository']}@{c['commit'][:7]}" for c in commits], generated)
    out = opt("--out", "tecnico.md")
    with open(out, "w", encoding="utf-8") as fh:
        fh.write(text)
    summary = {"module": module, "generatedAt": generated, "commits": commits, "counts": counts, "items": sum(counts.values()),
               "reusedIds": reused, "length": len(text),
               "notMaster": [c["repository"] for c in commits if c["branch"] != "origin/master"]}
    if opt("--json"):
        with open(opt("--json"), "w", encoding="utf-8") as fh:
            json.dump(summary, fh, ensure_ascii=False, indent=1)
    print(f"Índice técnico {module}: {summary['items']} itens (" + ", ".join(f"{k} {counts.get(k, 0)}" for k in SECTIONS) + ")"
          f" · {len(text):,} caracteres".replace(",", ".") + f" · {reused} IDs mantidos do publicado -> {out}")
    if summary["notMaster"]:
        print(f"AVISO: fora da origin/master: {', '.join(summary['notMaster'])} (inventário com --local?)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
