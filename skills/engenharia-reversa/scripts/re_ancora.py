#!/usr/bin/env python3
"""Reancoragem da engenharia reversa na master atual (feature 0097) — sem LLM, sem rede (só git local).

Os documentos publicados citam `arquivo:linha` de um commit antigo (às vezes de outra branch). Aqui:
  - cada citação vira o caminho COMPLETO a partir da raiz do repositório (completa quando o nome é único — de preferência
    dentro das pastas do módulo);
  - as linhas são mapeadas pelo `git diff -U0 <commit gravado>..<origin/master>` (rename incluso);
  - o item cujo trecho citado mudou (linhas alteradas/apagadas, arquivo removido) entra na lista de DESATUALIZADOS
    (o índice marca "código mudou desde a geração — confirme no código").
Só as linhas `- **Onde…:**` são reescritas (a revisão `reanchor` não muda o texto das regras); citações no corpo contam
para a lista de desatualizados, mas ficam como estão.

  re_ancora.py fontes <fontes-publicadas.json> <modulo.json> <documento.md>
      de onde o publicado veio (sessão da revisão publicada; senão o cabeçalho `<!-- codigo: -->`): repo, commit e pastas.
  re_ancora.py ancorar <documento.md> --repo <nome>=<pasta-do-clone>@<commit-antigo> [--repo ...]
                       [--prefixo <nome>=<subpasta> ...] [--novo <ref>] [--doc <tipo>] [--out novo.md] [--json relatorio.json]
      <commit-antigo> vazio ou desconhecido = só completa caminhos e confere se o arquivo/linha existe na master.
"""
import json
import os
import re
import subprocess
import sys
from collections import Counter, OrderedDict, defaultdict

EXT = r"(?:cs|cshtml|razor|asp|aspx|ascx|inc|js|ts|html|sql|json|ya?ml|xml|config|vb|py|csproj|scss|css)"
# citação: `caminho.ext:12` ou `caminho.ext:12-30` (com ou sem crase); continuação na mesma linha: `:40-42`
R_CITE = re.compile(r"(?<![\w/.-])((?:[\w.-]+/)*[\w.-]+\." + EXT + r"):(\d+)(?:-(\d+))?")
R_CONT = re.compile(r"`:(\d+)(?:-(\d+))?`")
R_ITEM = re.compile(r"^#{2,4}\s+\**`?([A-Z]{2,4})-(\d{1,4})\b")
R_ONDE = re.compile(r"^\s*[-*]\s+\*\*Onde\b")
R_CODE_HEADER = re.compile(r"^<!-- codigo: .*-->\s*$", re.M)
R_HUNK = re.compile(r"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")


def git(root, *args, check=False):
    p = subprocess.run(["git", "-C", root, *args], capture_output=True, text=True, errors="replace")
    if check and p.returncode != 0:
        raise RuntimeError(p.stderr.strip()[:300])
    return p.stdout if p.returncode == 0 else ""


# ── diff → mapa de linhas ─────────────────────────────────────────────────────────────────────────

def parse_diff(text):
    """`git diff -U0 -M` → {caminho antigo: {"new": caminho novo ou None (apagado), "hunks": [(os, oc, ns, nc)]}}."""
    files, cur = {}, None
    old = new = None
    for line in text.splitlines():
        if line.startswith("diff --git "):
            cur, old, new = None, None, None
            continue
        if line.startswith("rename from "):
            old = line[len("rename from "):]
        elif line.startswith("rename to "):
            new = line[len("rename to "):]
            cur = files.setdefault(old, {"new": new, "hunks": []})
        elif line.startswith("--- "):
            old = None if line[4:] == "/dev/null" else line[6:]
        elif line.startswith("+++ "):
            new = None if line[4:] == "/dev/null" else line[6:]
            if old is not None:
                cur = files.setdefault(old, {"new": new, "hunks": []})
                cur["new"] = new
            else:
                cur = None  # arquivo novo: nada citado nele
        elif line.startswith("@@") and cur is not None:
            m = R_HUNK.match(line)
            if m:
                cur["hunks"].append((int(m.group(1)), int(m.group(2) or 1), int(m.group(3)), int(m.group(4) or 1)))
    return files


def map_range(hunks, a, b):
    """
    Linhas [a, b] do arquivo antigo → ([a', b'] no novo, mudou?). Mudou = alguma linha do trecho foi alterada/apagada ou
    houve inserção no meio dele. Hunk "-os,0" = inserção DEPOIS da linha os.
    """
    changed = False
    delta_a = delta_b = 0
    new_a = new_b = None
    for os_, oc, ns, nc in hunks:
        if oc == 0:
            if a <= os_ < b:
                changed = True
            if os_ < a:
                delta_a += nc
            if os_ < b:
                delta_b += nc
            continue
        end = os_ + oc - 1
        if os_ <= b and a <= end:
            changed = True
        if a > end:
            delta_a += nc - oc
        elif os_ <= a <= end and new_a is None:
            new_a = ns if nc else max(ns, 1)
        if b > end:
            delta_b += nc - oc
        elif os_ <= b <= end and new_b is None:
            new_b = (ns + nc - 1) if nc else max(ns, 1)
    na = new_a if new_a is not None else a + delta_a
    nb = new_b if new_b is not None else b + delta_b
    return max(na, 1), max(nb, na, 1), changed


# ── árvore dos repositórios ───────────────────────────────────────────────────────────────────────

class Repo:
    def __init__(self, name, path, old, new_ref="origin/master", prefixes=()):
        self.name = name
        self.root = git(path, "rev-parse", "--show-toplevel").strip() or path
        self.new = git(self.root, "rev-parse", new_ref).strip()
        self.old_raw = old or ""
        self.old = ""
        if old:
            full = git(self.root, "rev-parse", "--verify", "-q", f"{old}^{{commit}}").strip()
            self.old = full
        self.prefixes = [p.strip("/") for p in prefixes if p is not None]
        self.new_files = self._tree(self.new) if self.new else []
        self.old_files = self._tree(self.old) if self.old else []
        self.diff = {}
        self.renames = {}

    def _tree(self, commit):
        return git(self.root, "ls-tree", "-r", "--name-only", commit).splitlines()

    def load_diff(self, paths):
        """Diff só dos arquivos citados (o diff da master inteira desde julho é enorme), com os renames da árvore toda."""
        if not self.old or not self.new or self.old == self.new:
            return
        for line in git(self.root, "diff", "--name-status", "-M", self.old, self.new).splitlines():
            parts = line.split("\t")
            if parts and parts[0].startswith("R") and len(parts) == 3:
                self.renames[parts[1]] = parts[2]
        spec = sorted(set(paths) | {self.renames[p] for p in paths if p in self.renames})
        for k in range(0, len(spec), 200):
            text = git(self.root, "diff", "-U0", "-M", "--no-color", "--no-ext-diff", self.old, self.new, "--", *spec[k:k + 200])
            self.diff.update(parse_diff(text))


R_TEST_PATH = re.compile(r"(^|/)(tests?|testes|__tests__|e2e|specs?)/|[._-](unit|integration)?tests?/|\.(spec|test)\.[jt]s$|Tests?\.cs$", re.I)


# arquivo local do gerador (o leitor não tem) — não é código: fica para a próxima geração corrigir
R_LOCAL = re.compile(r"^(?:banco|infra)/|(?:^|/)(?:catalogo|traducoes|inventario[\w-]*|payload)\.json$|(?:^|/)(?:pacote|parte)-[\w-]+\.md$")


def clean_cited(cited):
    """Tira `./`, `../`, `.../` e a barra do começo (sem estragar `.github/...`)."""
    return re.sub(r"^(?:\.{2,3}/?|…/?|\./|/)+", "", cited.replace("\\", "/"))


def _pick(group):
    """Um candidato: o único, ou o único fora de pasta/arquivo de teste."""
    if len(group) == 1:
        return group[0]
    real = [g for g in group if not R_TEST_PATH.search(g[1])]
    return real[0] if len(real) == 1 else None


def resolve(cited, repos):
    """
    Caminho citado (completo, com o nome do repo na frente, parcial, abreviado ou só o nome) → (repo, caminho na árvore
    antiga, situação). situação: ok | completado | ambigua | nao-achado. Prefere as pastas do módulo, depois o repositório
    inteiro, e o arquivo de código ao de teste; caminho que não casa (pastas abreviadas: `APP/…`, `Infra.Data/…`) tenta o
    nome do arquivo, desempatando pelas pastas citadas.
    """
    c = clean_cited(cited)
    if R_LOCAL.search(c):
        return None, None, "arquivo-local"
    scope = repos
    first = c.split("/", 1)[0].lower()
    named = [r for r in repos if r.name.lower() == first]
    if named and "/" in c:
        scope, c = named, c.split("/", 1)[1]

    def suffix(f, tail):
        return f == tail or (f.endswith(tail) and f[-len(tail) - 1] in "/.")

    exact, inside, anywhere = [], [], []
    for r in scope:
        for f in (r.old_files or r.new_files):
            if f == c:
                exact.append((r, f))
            elif suffix(f, c):
                anywhere.append((r, f))
                if any(f.startswith(p + "/") for p in r.prefixes if p):
                    inside.append((r, f))
    if len(exact) == 1:
        return exact[0][0], exact[0][1], "ok"
    for group in (inside, anywhere):
        if group:
            hit = _pick(group)
            if hit:
                return hit[0], hit[1], "completado"
            return None, [f for _, f in group][:4], "ambigua"
    if "/" in c:  # pastas abreviadas: pelo nome do arquivo, desempatando pelas pastas citadas
        base = c.rsplit("/", 1)[1]
        dirs = {d.lower() for d in c.split("/")[:-1] if d}
        cands = [(r, f) for r in scope for f in (r.old_files or r.new_files) if f.rsplit("/", 1)[-1] == base]
        if cands:
            hit = _pick(cands)
            if not hit:
                hinted = [x for x in cands if dirs & {d.lower() for d in x[1].split("/")[:-1]}]
                hit = _pick(hinted) if hinted else None
            if hit:
                return hit[0], hit[1], "completado"
            return None, [f for _, f in cands][:4], "ambigua"
    return None, None, "nao-achado"


# ── reancorar ─────────────────────────────────────────────────────────────────────────────────────

def ancorar(doc_text, repos):
    lines = doc_text.split("\n")
    # 1ª passada: o que é citado (para o diff só desses arquivos)
    cites = []  # (linha, ini, fim, caminho citado, a, b, item)
    item = None
    for n, line in enumerate(lines):
        m = R_ITEM.match(line)
        if m:
            item = f"{m.group(1)}-{int(m.group(2)):03d}"
        elif re.match(r"^#{1,2}\s", line):
            item = None
        last = None
        for mm in R_CITE.finditer(line):
            a = int(mm.group(2))
            b = int(mm.group(3) or a)
            cites.append((n, mm.start(), mm.end(), mm.group(1), a, b, item))
            last = mm.group(1)
        if last:
            for mm in R_CONT.finditer(line):
                a = int(mm.group(1))
                cites.append((n, mm.start() + 1, mm.end() - 1, None, a, int(mm.group(2) or a), item, last))
    resolved = {}
    for c in cites:
        path = c[3] if c[3] is not None else c[7]
        if path not in resolved:
            resolved[path] = resolve(path, repos)
    for r in repos:
        r.load_diff([f for (rr, f, st) in resolved.values() if rr is r and st in ("ok", "completado")])

    stats = Counter()
    stale = OrderedDict()   # item -> [motivos]
    unresolved = []
    edits = defaultdict(list)  # linha -> [(ini, fim, texto novo)]
    for c in cites:
        n, s, e, cited, a, b, item = c[:7]
        path = cited if cited is not None else c[7]
        repo, old_path, how = resolved[path]
        stats["citacoes"] += 1
        if how in ("ambigua", "nao-achado", "arquivo-local"):
            stats[how] += 1
            unresolved.append({"item": item, "ref": f"{path}:{a}" + (f"-{b}" if b != a else ""), "situacao": how,
                               "candidatos": old_path if how == "ambigua" else []})
            continue
        if how == "completado":
            stats["caminho-completado"] += 1
        new_path, na, nb, changed, why = old_path, a, b, False, ""
        if repo.old and repo.old != repo.new:
            d = repo.diff.get(old_path)
            if d is not None and d["new"] is None:
                changed, why = True, f"arquivo removido na master ({old_path})"
                new_path = None
            else:
                if d is not None:
                    new_path = d["new"] or old_path
                    na, nb, changed = map_range(d["hunks"], a, b)
                elif old_path in repo.renames:
                    new_path = repo.renames[old_path]
                if changed:
                    why = f"trecho citado mudou: {new_path}:{na}" + (f"-{nb}" if nb != na else "") + f" (antes {old_path}:{a}" + (f"-{b}" if b != a else "") + ")"
        elif not repo.old:
            stats["sem-commit-antigo"] += 1
            if old_path not in set(repo.new_files):
                changed, why, new_path = True, f"arquivo não existe na master ({old_path})", None
        if new_path and new_path != old_path:
            stats["renomeado"] += 1
        if changed:
            stats["mudou"] += 1
            if item:
                stale.setdefault(item, [])
                if why not in stale[item]:
                    stale[item].append(why)
        elif (na, nb) != (a, b):
            stats["linhas-remapeadas"] += 1
        else:
            stats["sem-mudanca-de-linha"] += 1
        if new_path is None or not R_ONDE.match(lines[n]):
            if not R_ONDE.match(lines[n]):
                stats["fora-do-onde"] += 1
            continue
        if cited is None:
            text = f"{na}" + (f"-{nb}" if nb != na else "")
            text = ":" + text
        else:
            text = f"{new_path}:{na}" + (f"-{nb}" if nb != na else "")
        edits[n].append((s, e, text))
    for n, ops in edits.items():
        line = lines[n]
        for s, e, text in sorted(ops, reverse=True):
            line = line[:s] + text + line[e:]
        lines[n] = line
    out = "\n".join(lines)
    header = "<!-- codigo: " + " · ".join(f"{r.name}@{r.new[:10]} (origin/master)" for r in repos if r.new) + " -->"
    if R_CODE_HEADER.search(out):
        out = R_CODE_HEADER.sub(header, out, count=1)
    stats["itens-desatualizados"] = len(stale)
    return out, {"stats": dict(stats), "stale": [{"itemId": k, "reasons": v} for k, v in stale.items()], "unresolved": unresolved,
                 "commits": [{"repository": r.name, "from": r.old or r.old_raw, "to": r.new} for r in repos]}


def fontes(published_sources, module_sources, content):
    """
    De onde o documento publicado veio → [(repo, commit antigo, prefixo)]. Ordem: a sessão da revisão publicada
    (`repository` ou o caminho: `.../.prmake-wt/master/<repo>/<sub>` ou `.../<repo>/<sub>` com o nome de uma fonte do módulo),
    depois o cabeçalho `<!-- codigo: repo@sha -->` do documento; prefixos = pastas do módulo (fontes da tela) + as da sessão.
    """
    names = {(m.get("repository") or "").lower(): m.get("repository") for m in module_sources if m.get("repository")}
    out = OrderedDict()   # repo -> {"commit", "prefixes"}

    def add(repo, commit, prefix):
        e = out.setdefault(repo, {"commit": "", "prefixes": []})
        if commit and not e["commit"]:
            e["commit"] = commit
        if prefix is not None and prefix not in e["prefixes"]:
            e["prefixes"].append(prefix)

    for src in published_sources or []:
        path = (src.get("path") or "").replace("\\", "/")
        commit = (src.get("commit") or "").split("@", 1)[0]
        repo, prefix = src.get("repository"), None
        parts = [p for p in path.split("/") if p]
        if "/.prmake-wt/master/" in path:
            i = parts.index("master", parts.index(".prmake-wt"))
            repo = repo or (parts[i + 1] if i + 1 < len(parts) else None)
            prefix = "/".join(parts[i + 2:])
        else:
            for i, p in enumerate(parts):
                if p.lower() in names:
                    repo = repo or names[p.lower()]
                    prefix = "/".join(parts[i + 1:])
                    break
        if not repo and parts:
            repo = parts[-1]
        if repo:
            add(names.get(repo.lower(), repo), commit, prefix)
    m = R_CODE_HEADER.search(content or "")
    if m:
        for repo, sha in re.findall(r"([\w.-]+)@([0-9a-f]{6,40})", m.group(0)):
            add(names.get(repo.lower(), repo), sha, None)
    for ms in module_sources:
        if ms.get("repository"):
            add(ms["repository"], "", (ms.get("path") or "").strip("/"))
    return [(repo, e["commit"], [p for p in e["prefixes"] if p is not None]) for repo, e in out.items()]


def main(argv):
    if len(argv) >= 2 and argv[1] == "fontes":
        # re_ancora.py fontes <fontes-publicadas.json> <modulo.json> <documento.md> → repo<TAB>commit<TAB>prefixo,prefixo
        pub = json.load(open(argv[2], encoding="utf-8")) if os.path.exists(argv[2]) else []
        mod = json.load(open(argv[3], encoding="utf-8")).get("sources", []) if os.path.exists(argv[3]) else []
        content = open(argv[4], encoding="utf-8").read() if len(argv) > 4 and os.path.exists(argv[4]) else ""
        for repo, commit, prefixes in fontes(pub, mod, content):
            print(f"{repo}\t{commit}\t{','.join(prefixes)}")
        return 0
    if len(argv) < 3 or argv[1] != "ancorar":
        print(__doc__)
        return 0 if len(argv) < 2 or argv[1] in ("-h", "--help") else 2
    doc_path = argv[2]
    opts = defaultdict(list)
    k = 3
    while k < len(argv):
        if argv[k].startswith("--") and k + 1 < len(argv):
            opts[argv[k]].append(argv[k + 1])
            k += 2
        else:
            k += 1
    prefixes = defaultdict(list)
    for p in opts["--prefixo"]:
        name, _, sub = p.partition("=")
        prefixes[name.lower()].append(sub)
    repos = []
    for spec in opts["--repo"]:
        name, _, rest = spec.partition("=")
        path, _, old = rest.rpartition("@") if "@" in rest else (rest, "", "")
        if not os.path.isdir(path):
            print(f"ERRO: pasta do repositório {name} não existe: {path}", file=sys.stderr)
            return 2
        repos.append(Repo(name, path, old, (opts["--novo"] or ["origin/master"])[0], prefixes.get(name.lower(), [])))
    if not repos:
        print("ERRO: informe ao menos um --repo <nome>=<pasta>@<commit>", file=sys.stderr)
        return 2
    text = open(doc_path, encoding="utf-8").read()
    out, rep = ancorar(text, repos)
    rep["doc"] = (opts["--doc"] or [os.path.basename(doc_path)])[0]
    if opts["--out"]:
        with open(opts["--out"][0], "w", encoding="utf-8") as fh:
            fh.write(out)
    if opts["--json"]:
        with open(opts["--json"][0], "w", encoding="utf-8") as fh:
            json.dump(rep, fh, ensure_ascii=False, indent=1)
    st = rep["stats"]
    total = st.get("citacoes", 0)
    pct = (lambda n: f"{n / total:.0%}" if total else "—")
    print(f"Reancoragem {rep['doc']}: {total} citações · {st.get('linhas-remapeadas', 0)} com a linha remapeada ({pct(st.get('linhas-remapeadas', 0))})"
          f" · {st.get('sem-mudanca-de-linha', 0)} iguais · {st.get('caminho-completado', 0)} caminhos completados · {st.get('renomeado', 0)} renomeados"
          f" · {st.get('mudou', 0)} trechos mudaram → {st.get('itens-desatualizados', 0)} itens desatualizados"
          f" · {st.get('ambigua', 0)} ambíguas · {st.get('nao-achado', 0)} não achadas · {st.get('arquivo-local', 0)} arquivos locais do gerador")
    for c in rep["commits"]:
        print(f"  {c['repository']}: {(c['from'] or '?')[:10]} → {c['to'][:10]}" + ("" if c["from"] else " (sem commit gravado: só caminhos)"))
    for s in rep["stale"][:15]:
        print(f"  DESATUALIZADO {s['itemId']}: {s['reasons'][0][:140]}")
    if len(rep["stale"]) > 15:
        print(f"  … e mais {len(rep['stale']) - 15}")
    for u in rep["unresolved"][:8]:
        print(f"  A CONFERIR {u['item'] or '(fora de item)'}: {u['ref']} ({u['situacao']}{': ' + ', '.join(u['candidatos'][:3]) if u['candidatos'] else ''})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
