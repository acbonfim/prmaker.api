#!/usr/bin/env python3
"""gestao.py — numeros, grupos e arquivos das tarefas de gestao (feature 0099), sem IA.

Le e grava SO pela API do PRMake (x-api-key): a consulta ao DevOps usa a integracao pessoal de quem roda. Nada de PAT.

  gestao.py fetch  <chave> <item> <nome> [--wiql W | --filter JSON] [--fields a,b | --fields +a,b] [--text] [--top N]
                   [--task N] [--no-save]        consulta (POST DevOps/query) -> ~/.prmake/cards/<chave>/gestao/<nome>.json
                                                 + arquivo <nome>.json (kind data) no item; imprime o resumo
  gestao.py pull   <chave> <item> <arquivo>      baixa um arquivo DO ITEM (ex.: consulta-bugs.json) para a pasta local
  gestao.py save   <chave> <item> <arquivo> [--name N] [--kind analysis|data|text] [--task N]
                                                 grava um arquivo local como arquivo do item (sem passar pelo chat)
  gestao.py pdf    <chave> <item> <arquivo.md> [--name N.pdf] [--title T] [--task N]
                                                 markdown -> PDF no servidor (POST WorkItems/<item>/files/pdf)
  gestao.py report <dados.json> [--title T] [--summary sumario.md] [--weeks 8] [--top-old 10] [--stale-days 14]
                   [--closed-states "Closed,Done,Removed"] [--today AAAA-MM-DD] [--out relatorio.md]
  gestao.py duplicates <dados.json> [--window-days 120] [--min 0.55] [--max-groups 30] [--out grupos.md] [--json grupos.json]
  gestao.py stale  <dados.json> [--days 14] [--closed-states ...] [--today AAAA-MM-DD]
  gestao.py rca-check <rca.md> <card>            confere o RCA no "Modelo do RCA" do prompt de Bug do PRMake (rca_check.py)

<dados.json> = resposta do POST DevOps/query ({count, items:[{id, fields}]}), lista de BoardItem ou a lista de items.
Token: PRMAKE_TOKEN ou ~/.claude/prmake-token.txt. API: PRMAKE_API_BASE (padrao https://api.softhouse.app.br/api/v1).
Saida: 0 ok · 1 erro · 2 uso incorreto · 3 sem permissao na API (use a ferramenta MCP equivalente).
"""
import argparse
import datetime as dt
import difflib
import html
import json
import math
import os
import re
import subprocess
import sys
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
from collections import Counter, defaultdict

BASE = os.environ.get("PRMAKE_API_BASE", "https://api.softhouse.app.br/api/v1").rstrip("/")
CARDS_DIR = os.environ.get("CARDS_DIR", os.path.join(os.path.expanduser("~"), ".prmake", "cards"))
DEFAULT_CLOSED = "Closed,Done,Removed,Resolved,Completed,Cancelled,Canceled"

DEFAULT_FIELDS = [
    "System.Id", "System.WorkItemType", "System.Title", "System.State", "System.Reason", "System.AreaPath",
    "System.IterationPath", "System.AssignedTo", "System.CreatedDate", "System.CreatedBy", "System.ChangedDate",
    "System.Tags", "System.BoardColumn", "System.BoardLane", "Microsoft.VSTS.Common.Severity",
    "Microsoft.VSTS.Common.Priority", "Microsoft.VSTS.Common.StateChangeDate", "Microsoft.VSTS.Common.ClosedDate",
    "Microsoft.VSTS.Common.ResolvedDate", "Custom.BugType", "Custom.Module",
]
TEXT_FIELDS = ["Microsoft.VSTS.TCM.ReproSteps", "System.Description", "Microsoft.VSTS.Common.AcceptanceCriteria"]

# Campos de cliente/ambiente/planta variam por processo do DevOps: reconhecidos pelo nome de referencia.
CLIENT_REF = re.compile(r"(client|customer|cliente|environment|ambiente|site|plant|planta)", re.I)

STOP = set("""
a o os as um uma uns umas de do da dos das no na nos nas em ao aos para pra por pelo pela pelos pelas com sem que
se nao não e é ou mas mais menos muito quando onde como qual quais ja já foi ser esta está estao estão tem ter ha
há isso esse essa este esta aqui ali ele ela eles elas seu sua seus suas meu minha apos após ate até sobre entre
the of to in on at for from with without and or but not is are was were be been when where how which this that
these those it its into after before by as an if then than can cannot cant dont doesnt does do
el la los las del al y en con sin por para que se es un una
erro error errors bug bugs problema problemas issue issues falha falhas fail failed failing quando when
tela screen pagina page sistema system usuario usuário user users funciona working work works
""".split())


# ----------------------------------------------------------------------------------------------- API do PRMake
def token():
    t = os.environ.get("PRMAKE_TOKEN", "").strip()
    if t:
        return t
    path = os.path.join(os.path.expanduser("~"), ".claude", "prmake-token.txt")
    if os.path.isfile(path):
        return open(path, encoding="utf-8").read().strip()
    die("token nao encontrado (PRMAKE_TOKEN ou ~/.claude/prmake-token.txt)")


def api(method, path, body=None, raw=False, timeout=120):
    data = None
    # X-Execution-Client: skill — chamada do Claude (executor/terminal); a Mesa (DevOps/*) aceita a skill de qualquer papel.
    headers = {"x-api-key": token(), "Accept": "application/json", "X-Execution-Client": "skill"}
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(f"{BASE}/{path.lstrip('/')}", data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            payload = resp.read()
            return payload if raw else json.loads(payload.decode("utf-8") or "null")
    except urllib.error.HTTPError as e:
        detail = e.read().decode("utf-8", "replace")[:500]
        if e.code in (401, 403):
            print(f"SEM PERMISSAO ({e.code}) em {method} {path}: {detail}\n"
                  "Use a ferramenta MCP equivalente (prmake_devops_query / prmake_work_item_file / prmake_pdf).", file=sys.stderr)
            sys.exit(3)
        die(f"HTTP {e.code} em {method} {path}: {detail}")
    except urllib.error.URLError as e:
        die(f"sem conexao com o PRMake ({BASE}): {e.reason}")


def die(msg, code=1):
    print(f"ERRO: {msg}", file=sys.stderr)
    sys.exit(code)


def key_dir(key):
    d = os.path.join(CARDS_DIR, key.lstrip("#"), "gestao")
    os.makedirs(d, exist_ok=True)
    return d


def upload(item, name, content, kind, task=None):
    body = {"name": name, "kind": kind, "content": content}
    if task:
        body["taskNumber"] = int(task)
    return api("POST", f"WorkItems/{int(item)}/files", body)


# ----------------------------------------------------------------------------------------------- normalizacao
def strip_html(s):
    if not s:
        return ""
    s = re.sub(r"<(br|/p|/div|/li|/tr|/h\d)[^>]*>", "\n", str(s), flags=re.I)
    s = re.sub(r"<[^>]+>", " ", s)
    return re.sub(r"[ \t\r\f\v]+", " ", html.unescape(s)).strip()


def person(v):
    if not v:
        return ""
    if isinstance(v, dict):
        return v.get("displayName") or v.get("uniqueName") or ""
    return re.sub(r"\s*<[^>]*>\s*$", "", str(v)).strip()


def parse_date(v):
    if not v:
        return None
    s = str(v).strip().replace("Z", "+00:00")
    try:
        d = dt.datetime.fromisoformat(s[:32])
    except ValueError:
        try:
            d = dt.datetime.strptime(s[:10], "%Y-%m-%d")
        except ValueError:
            return None
    return d.date()


def load_items(path):
    data = json.load(open(path, encoding="utf-8"))
    raw = data.get("items", data.get("value", [])) if isinstance(data, dict) else data
    return [normalize(i) for i in raw if isinstance(i, dict)]


def normalize(it):
    f = it.get("fields") or {}

    def g(*keys):
        for k in keys:
            v = f.get(k, it.get(k))
            if v not in (None, "", []):
                return v
        return None

    tags = g("System.Tags", "tags") or []
    if isinstance(tags, str):
        tags = [t.strip() for t in tags.split(";") if t.strip()]
    client = " / ".join(str(v).strip() for k, v in f.items()
                        if v and not k.startswith("System.") and CLIENT_REF.search(k.split(".")[-1]) and not isinstance(v, (dict, list)))
    text = " ".join(strip_html(f.get(k)) for k in TEXT_FIELDS if f.get(k))
    return {
        "id": str(g("System.Id", "id") or ""),
        "type": g("System.WorkItemType", "type") or "",
        "title": str(g("System.Title", "title") or "").strip(),
        "state": g("System.State", "state") or "",
        "area": g("System.AreaPath", "areaPath") or "",
        "assigned": person(g("System.AssignedTo", "assignedTo")),
        "created": parse_date(g("System.CreatedDate", "createdDate")),
        "changed": parse_date(g("System.ChangedDate", "changedDate")),
        "state_changed": parse_date(g("Microsoft.VSTS.Common.StateChangeDate")),
        "closed": parse_date(g("Microsoft.VSTS.Common.ClosedDate", "Microsoft.VSTS.Common.ResolvedDate")),
        "severity": str(g("Microsoft.VSTS.Common.Severity", "severity") or ""),
        "priority": str(g("Microsoft.VSTS.Common.Priority", "priority") or ""),
        "bug_type": str(g("Custom.BugType") or ""),
        "column": g("System.BoardColumn", "column") or "",
        "tags": tags,
        "client": client,
        "text": text,
    }


def short_area(a, levels=2):
    parts = [p for p in re.split(r"[\\/]", a or "") if p]
    return "\\".join(parts[-levels:]) if parts else "(sem area)"


def pct(n, total):
    return f"{(100.0 * n / total):.0f}%" if total else "-"


def bar(n, top, width=20):
    return "█" * max(1 if n else 0, round(width * n / top)) if top else ""


def cell(s, limit=90):
    s = ("" if s is None else str(s)).replace("|", "\\|").replace("\n", " ").strip()
    return s if len(s) <= limit else s[: limit - 1] + "…"


def table(headers, rows):
    out = ["| " + " | ".join(headers) + " |", "|" + "|".join("---" for _ in headers) + "|"]
    out += ["| " + " | ".join(cell(c) for c in r) + " |" for r in rows]
    return "\n".join(out)


def week_start(d):
    return d - dt.timedelta(days=d.weekday())


def is_open(i, closed):
    return i["state"].lower() not in closed


def closed_set(arg):
    return {s.strip().lower() for s in (arg or DEFAULT_CLOSED).split(",") if s.strip()}


def today_of(arg):
    return dt.date.fromisoformat(arg) if arg else dt.date.today()


# ----------------------------------------------------------------------------------------------- report
def cmd_report(a):
    items = load_items(a.data)
    closed = closed_set(a.closed_states)
    today = today_of(a.today)
    total = len(items)
    opened = [i for i in items if is_open(i, closed)]
    out = [f"# {a.title or 'Relatório de cards'}", ""]
    out.append(f"_Gerado em {today:%d/%m/%Y} a partir de {total} cards (`{os.path.basename(a.data)}`). "
               f"Abertos = estado fora de: {', '.join(sorted(s.title() for s in closed))}._")
    out.append("")
    if a.summary:
        out += ["## Sumário executivo", "", open(a.summary, encoding="utf-8").read().strip(), ""]

    ages = sorted((today - i["created"]).days for i in opened if i["created"])
    last4 = today - dt.timedelta(days=28)
    prev4 = today - dt.timedelta(days=56)
    created_last = sum(1 for i in items if i["created"] and i["created"] > last4)
    created_prev = sum(1 for i in items if i["created"] and prev4 < i["created"] <= last4)
    closed_last = sum(1 for i in items if i["closed"] and i["closed"] > last4)
    stale = [i for i in opened if (i["changed"] or i["created"]) and (today - (i["changed"] or i["created"])).days > a.stale_days]
    median = ages[len(ages) // 2] if ages else None

    rows = [("Total", total), ("Abertos", f"{len(opened)} ({pct(len(opened), total)})"),
            ("Fechados", total - len(opened)),
            ("Criados nas últimas 4 semanas", created_last), ("Criados nas 4 semanas anteriores", created_prev)]
    if any(i["closed"] for i in items):
        rows.append(("Fechados nas últimas 4 semanas", closed_last))
    if median is not None:
        rows.append(("Idade mediana dos abertos", f"{median} dias"))
    rows.append((f"Abertos sem mudança há mais de {a.stale_days} dias", len(stale)))
    out += ["## Números", "", table(["Indicador", "Valor"], rows), ""]

    def breakdown(title, keyf, only_open=False, limit=15, label="Valor"):
        base = opened if only_open else items
        cnt = Counter(keyf(i) or "(sem valor)" for i in base)
        if not cnt or (len(cnt) == 1 and "(sem valor)" in cnt):
            return
        cnt_open = Counter(keyf(i) or "(sem valor)" for i in opened)
        cnt_all = Counter(keyf(i) or "(sem valor)" for i in items)
        top = max(cnt.values())
        rows = [(k, cnt_open.get(k, 0), cnt_all.get(k, 0), bar(cnt[k], top)) for k, _ in cnt.most_common(limit)]
        more = len(cnt) - limit
        out.extend([f"## {title}", "", table([label, "Abertos", "Total", "Distribuição"], rows)])
        if more > 0:
            out.append(f"\n_+{more} outros valores com menos cards._")
        out.append("")

    st = Counter(i["state"] or "(sem estado)" for i in items)
    top = max(st.values()) if st else 0
    out += ["## Por estado", "", table(["Estado", "Cards", "%", "Distribuição"],
                                       [(k, v, pct(v, total), bar(v, top)) for k, v in st.most_common()]), ""]
    breakdown("Por severidade", lambda i: i["severity"], label="Severidade")
    breakdown("Por prioridade", lambda i: i["priority"], label="Prioridade")
    breakdown("Por tipo de bug", lambda i: i["bug_type"], label="Bug Type")
    if len({i["type"] for i in items}) > 1:
        breakdown("Por tipo", lambda i: i["type"], label="Tipo")
    breakdown("Por área (abertos)", lambda i: short_area(i["area"]), only_open=True, label="Área")
    breakdown("Por cliente/ambiente (abertos)", lambda i: i["client"], only_open=True, label="Cliente/ambiente")
    breakdown("Por responsável (abertos)", lambda i: i["assigned"] or "(sem responsável)", only_open=True, label="Responsável")

    if a.weeks > 0:
        first = week_start(today) - dt.timedelta(weeks=a.weeks - 1)
        cw = Counter(week_start(i["created"]) for i in items if i["created"] and i["created"] >= first)
        fw = Counter(week_start(i["closed"]) for i in items if i["closed"] and i["closed"] >= first)
        weeks = [first + dt.timedelta(weeks=k) for k in range(a.weeks)]
        top = max([cw.get(w, 0) for w in weeks] + [fw.get(w, 0) for w in weeks] + [0])
        has_closed = any(fw.values())
        rows = []
        for w in weeks:
            c, f = cw.get(w, 0), fw.get(w, 0)
            rows.append((f"{w:%d/%m}", c, f if has_closed else "-", (f"{c - f:+d}" if has_closed else "-"), bar(c, top)))
        out += [f"## Por semana (últimas {a.weeks}, semana começando na segunda)", "",
                table(["Semana", "Criados", "Fechados", "Saldo", "Criados (gráfico)"], rows), ""]
        if not has_closed:
            out += ["_Sem data de fechamento na consulta (inclua `Microsoft.VSTS.Common.ClosedDate`) — só criados._", ""]

    if ages:
        buckets = [("até 7 dias", 0, 7), ("8–30 dias", 8, 30), ("31–90 dias", 31, 90), ("91–180 dias", 91, 180), ("mais de 180 dias", 181, 10**6)]
        rows = [(n, sum(1 for x in ages if lo <= x <= hi)) for n, lo, hi in buckets]
        top = max(r[1] for r in rows)
        out += ["## Idade dos abertos", "", table(["Faixa", "Cards", "Distribuição"], [(n, c, bar(c, top)) for n, c in rows]), ""]

    oldest = sorted((i for i in opened if i["created"]), key=lambda i: i["created"])[: a.top_old]
    if oldest:
        out += [f"## Mais antigos em aberto (top {len(oldest)})", "",
                table(["Card", "Título", "Estado", "Área", "Responsável", "Idade (dias)"],
                      [(i["id"], i["title"], i["state"], short_area(i["area"]), i["assigned"] or "-", (today - i["created"]).days) for i in oldest]), ""]

    if stale:
        by_col = defaultdict(list)
        for i in stale:
            by_col[i["column"] or i["state"] or "(sem coluna)"].append(i)
        rows = []
        for col, lst in sorted(by_col.items(), key=lambda kv: -len(kv[1])):
            worst = max(lst, key=lambda i: (today - (i["changed"] or i["created"])).days)
            rows.append((col, len(lst), f"{worst['id']} ({(today - (worst['changed'] or worst['created'])).days} dias)"))
        out += [f"## Parados há mais de {a.stale_days} dias, por coluna", "", table(["Coluna", "Cards", "Mais parado"], rows), ""]

    facts = []
    if created_prev:
        delta = (created_last - created_prev) / created_prev * 100
        facts.append(f"Criados nas últimas 4 semanas: {created_last} contra {created_prev} nas 4 anteriores ({delta:+.0f}%).")
    elif created_last:
        facts.append(f"Criados nas últimas 4 semanas: {created_last} (nenhum nas 4 anteriores).")
    if any(i["closed"] for i in items):
        facts.append(f"Fechados nas últimas 4 semanas: {closed_last} — saldo {created_last - closed_last:+d}.")
    areas = Counter(short_area(i["area"]) for i in opened)
    if areas:
        k, v = areas.most_common(1)[0]
        facts.append(f"Área com mais abertos: {k} ({v} de {len(opened)}, {pct(v, len(opened))}).")
    unassigned = sum(1 for i in opened if not i["assigned"])
    if unassigned:
        facts.append(f"Abertos sem responsável: {unassigned}.")
    if median is not None:
        facts.append(f"Metade dos abertos tem mais de {median} dias.")
    if facts:
        out += ["## Fatos para as tendências", "", *[f"- {x}" for x in facts], ""]

    text = "\n".join(out).rstrip() + "\n"
    if a.out:
        open(a.out, "w", encoding="utf-8").write(text)
        print(f"relatorio: {a.out} ({total} cards, {len(opened)} abertos)")
    else:
        sys.stdout.write(text)


# ----------------------------------------------------------------------------------------------- duplicados
def fold(s):
    s = unicodedata.normalize("NFKD", s or "").encode("ascii", "ignore").decode("ascii").lower()
    s = re.sub(r"\[[^\]]*\]|ab#\d+|#\d+", " ", s)
    return re.sub(r"[^a-z0-9]+", " ", s).strip()


def terms(s):
    out = []
    for w in fold(s).split():
        if len(w) < 3 or w.isdigit() or w in STOP:
            continue
        if len(w) > 4 and w.endswith("s"):
            w = w[:-1]
        out.append(w)
    return out


def cosine(a, b):
    if not a or not b:
        return None
    dot = sum(a[k] * b.get(k, 0) for k in a)
    na = math.sqrt(sum(v * v for v in a.values()))
    nb = math.sqrt(sum(v * v for v in b.values()))
    return dot / (na * nb) if na and nb else None


def cmd_duplicates(a):
    items = [i for i in load_items(a.data) if i["title"]]
    for i in items:
        i["tset"] = set(terms(i["title"]))
        i["tnorm"] = " ".join(terms(i["title"]))
        i["tvec"] = Counter(dict(Counter(terms(i["text"])).most_common(40))) if i["text"] else None
    index = defaultdict(set)
    for n, i in enumerate(items):
        for t in i["tset"]:
            index[t].add(n)
    pairs = {}
    for n, i in enumerate(items):
        cands = set()
        for t in i["tset"]:
            if len(index[t]) <= 200:
                cands |= index[t]
        for m in cands:
            if m <= n:
                continue
            j = items[m]
            if a.window_days and i["created"] and j["created"] and abs((i["created"] - j["created"]).days) > a.window_days:
                continue
            shared = i["tset"] & j["tset"]
            jac = len(shared) / len(i["tset"] | j["tset"]) if i["tset"] and j["tset"] else 0
            ratio = difflib.SequenceMatcher(None, i["tnorm"], j["tnorm"]).ratio()
            title = 0.6 * jac + 0.4 * ratio
            if len(shared) < 2 and title < 0.5:
                continue
            parts = [(0.55, title)]
            reasons = [f"título {title:.2f} (termos em comum: {', '.join(sorted(shared)[:8]) or '-'})"]
            txt = cosine(i["tvec"], j["tvec"]) if i["tvec"] and j["tvec"] else None
            if txt is not None:
                parts.append((0.25, txt))
                common = sorted(set(i["tvec"]) & set(j["tvec"]), key=lambda k: -(i["tvec"][k] + j["tvec"][k]))[:6]
                reasons.append(f"passos/descrição {txt:.2f} ({', '.join(common) or '-'})")
            if i["area"] and j["area"]:
                same = short_area(i["area"]) == short_area(j["area"])
                parts.append((0.1, 1.0 if same else 0.0))
                reasons.append("mesma área" if same else "áreas diferentes")
            if i["client"] and j["client"]:
                same = fold(i["client"]) == fold(j["client"])
                parts.append((0.1, 1.0 if same else 0.3))
                reasons.append("mesmo cliente/ambiente" if same else "cliente/ambiente diferentes")
            score = sum(w * v for w, v in parts) / sum(w for w, _ in parts)
            if i["created"] and j["created"]:
                reasons.append(f"criados com {abs((i['created'] - j['created']).days)} dias de diferença")
            if i["type"] != j["type"]:
                score *= 0.9
                reasons.append(f"tipos diferentes ({i['type']} × {j['type']})")
            if score >= a.min:
                pairs[(n, m)] = (score, reasons)

    parent = list(range(len(items)))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for n, m in pairs:
        parent[find(n)] = find(m)
    groups = defaultdict(list)
    for n, m in pairs:
        groups[find(n)].append((n, m))
    result = []
    closed = closed_set(None)
    for edges in groups.values():
        members = sorted({x for e in edges for x in e}, key=lambda x: (items[x]["created"] or dt.date.max, items[x]["id"]))
        scores = [pairs[e][0] for e in edges]
        conf = sum(scores) / len(scores)
        label = "alta" if conf >= 0.75 else "média" if conf >= 0.62 else "baixa"
        opened = [x for x in members if is_open(items[x], closed)]
        main = (opened or members)[0]
        best = max(edges, key=lambda e: pairs[e][0])
        result.append({
            "confidence": round(conf, 2), "label": label, "main": items[main]["id"],
            "cards": [{k: (str(items[x][k]) if items[x][k] is not None else "") for k in
                       ("id", "type", "title", "state", "area", "client", "created", "assigned")} for x in members],
            "pairs": [{"a": items[n]["id"], "b": items[m]["id"], "score": round(pairs[(n, m)][0], 2),
                       "reasons": pairs[(n, m)][1]} for n, m in sorted(edges, key=lambda e: -pairs[e][0])],
            "why": pairs[best][1], "best": [items[best[0]]["id"], items[best[1]]["id"]],
        })
    result.sort(key=lambda g: (-g["confidence"], -len(g["cards"])))
    result = result[: a.max_groups]

    out = [f"# Possíveis duplicados ({len(result)} grupos em {len(items)} cards)", "",
           f"_Critérios: título normalizado (sem acento, prefixos e números), termos-chave, passos/descrição quando a consulta "
           f"trouxe o texto, mesma área, mesmo cliente/ambiente e janela de {a.window_days or 'qualquer'} dias entre as datas de "
           f"criação. Corte {a.min:.2f}. Confiança: alta ≥ 0,75 · média ≥ 0,62 · baixa abaixo. Nada foi fechado nem ligado._", ""]
    for k, g in enumerate(result, 1):
        out += [f"## Grupo {k} — confiança {g['label']} ({g['confidence']:.2f})", "",
                table(["Card", "Tipo", "Título", "Estado", "Área", "Cliente/ambiente", "Criado"],
                      [(("**" + c["id"] + "**") if c["id"] == g["main"] else c["id"], c["type"], c["title"], c["state"],
                        short_area(c["area"]), c["client"] or "-", c["created"]) for c in g["cards"]]), "",
                f"**Por quê** (par mais forte {g['best'][0]} × {g['best'][1]}): " + "; ".join(g["why"]) + ".", ""]
        others = [c["id"] for c in g["cards"] if c["id"] != g["main"]]
        out += [f"**Recomendação (a decidir):** manter **{g['main']}** como principal (o mais antigo em aberto) e relacionar "
                f"{', '.join(others)} a ele como duplicado — fechar só com a decisão de quem gere o épico.", ""]
    if not result:
        out += ["Nenhum grupo acima do corte. Baixe o corte (`--min 0.45`) ou amplie a janela (`--window-days 0`) para ver candidatos mais fracos.", ""]
    text = "\n".join(out).rstrip() + "\n"
    if a.json:
        json.dump({"groups": result, "cards": len(items), "min": a.min, "windowDays": a.window_days},
                  open(a.json, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    if a.out:
        open(a.out, "w", encoding="utf-8").write(text)
        print(f"duplicados: {a.out} ({len(result)} grupos em {len(items)} cards)")
    else:
        sys.stdout.write(text)


# ----------------------------------------------------------------------------------------------- parados
def cmd_stale(a):
    items = load_items(a.data)
    closed = closed_set(a.closed_states)
    today = today_of(a.today)
    rows = []
    for i in items:
        ref = i["changed"] or i["state_changed"] or i["created"]
        if is_open(i, closed) and ref and (today - ref).days > a.days:
            rows.append((i["column"] or i["state"] or "(sem coluna)", i["id"], i["title"], i["state"], i["assigned"] or "-", (today - ref).days))
    rows.sort(key=lambda r: (r[0], -r[5]))
    print(f"# Parados há mais de {a.days} dias ({len(rows)} de {len(items)} cards)\n")
    print(table(["Coluna", "Card", "Título", "Estado", "Responsável", "Dias sem mudança"], rows) if rows else "Nenhum card parado.")


# ----------------------------------------------------------------------------------------------- API: consulta e arquivos
def cmd_fetch(a):
    fields = list(DEFAULT_FIELDS)
    if a.fields:
        extra = [f.strip() for f in a.fields.lstrip("+").split(",") if f.strip()]
        fields = fields + [f for f in extra if f not in fields] if a.fields.startswith("+") else extra
    if a.text:
        fields += [f for f in TEXT_FIELDS if f not in fields]
    body = {"fields": fields, "top": a.top}
    if a.wiql:
        body["wiql"] = a.wiql
    if a.filter:
        try:
            body["filter"] = json.loads(a.filter)
        except json.JSONDecodeError as e:
            die(f"--filter nao e JSON valido: {e}", 2)
    if not a.wiql and not a.filter:
        die("informe --wiql ou --filter", 2)
    data = api("POST", "DevOps/query", body)
    name = a.name if a.name.endswith(".json") else a.name + ".json"
    path = os.path.join(key_dir(a.key), name)
    text = json.dumps(data, ensure_ascii=False)
    open(path, "w", encoding="utf-8").write(text)
    items = load_items(path)
    count = data.get("count", len(items)) if isinstance(data, dict) else len(items)
    print(f"{count} cards → {path}")
    if count > len(items):
        print(f"AVISO: a consulta tem {count} cards e vieram {len(items)} (top={a.top}) — os números do relatório são só destes.")
    if not a.no_save:
        f = upload(a.item, name, text, "data", a.task)
        print(f"arquivo do item #{a.item}: {name} (arquivo #{(f or {}).get('number', '?')})")
    st = Counter(i["state"] or "-" for i in items)
    print("estados: " + ", ".join(f"{k} {v}" for k, v in st.most_common()))
    print(table(["Card", "Tipo", "Estado", "Título"], [(i["id"], i["type"], i["state"], i["title"]) for i in items[:15]]))
    if len(items) > 15:
        print(f"... +{len(items) - 15} cards (arquivo completo acima)")


def item_files(item):
    files = api("GET", f"WorkItems/{int(item)}/files") or []
    return [f for f in files if f.get("source", "item") == "item"]


def cmd_pull(a):
    match = [f for f in item_files(a.item) if f.get("name") == a.file or str(f.get("number")) == a.file.lstrip("#")]
    if not match:
        die(f"arquivo '{a.file}' nao encontrado no item #{a.item}")
    f = match[-1]
    data = api("GET", f["contentUrl"], raw=True)
    path = os.path.join(key_dir(a.key), f["name"])
    open(path, "wb").write(data)
    print(path)


def cmd_save(a):
    content = open(a.file, encoding="utf-8").read()
    name = a.name or os.path.basename(a.file)
    f = upload(a.item, name, content, a.kind, a.task)
    print(f"arquivo do item #{a.item}: {name} (arquivo #{(f or {}).get('number', '?')}, {len(content.encode('utf-8'))} bytes)")


def cmd_pdf(a):
    md = open(a.file, encoding="utf-8").read()
    name = a.name or re.sub(r"\.(md|markdown|txt)$", "", os.path.basename(a.file)) + ".pdf"
    if not name.lower().endswith(".pdf"):
        name += ".pdf"
    body = {"name": name, "markdown": md}
    if a.title:
        body["title"] = a.title
    if a.task:
        body["taskNumber"] = int(a.task)
    f = api("POST", f"WorkItems/{int(a.item)}/files/pdf", body, timeout=300)
    print(f"PDF no item #{a.item}: {name} (arquivo #{(f or {}).get('number', '?')}, {(f or {}).get('size', '?')} bytes)")


def cmd_rca_check(a):
    cfg = api("GET", "Skills/config")
    prompt = ((cfg or {}).get("prompts") or {}).get("bug") or ""
    if not prompt:
        print("aviso: prompt de Bug nao configurado no PRMake — formato do RCA nao conferido", file=sys.stderr)
        return
    d = key_dir(a.card)
    ppath = os.path.join(d, "rca-prompt.txt")
    open(ppath, "w", encoding="utf-8").write(prompt)
    here = os.path.dirname(os.path.abspath(__file__))
    checker = next((p for p in (os.path.join(here, "..", "..", "gerar-prmake", "scripts", "rca_check.py"),
                                os.path.join(os.path.expanduser("~"), ".claude", "skills", "gerar-prmake", "scripts", "rca_check.py"))
                    if os.path.isfile(p)), None)
    if not checker:
        die("rca_check.py ausente (skill gerar-prmake): rode prmake-skills.sh update gerar-prmake")
    r = subprocess.run([sys.executable, checker, ppath, a.rca, a.card])
    if r.returncode == 0:
        print(f"RCA no padrao do prompt de Bug ({a.rca}).")
    sys.exit(r.returncode)


def main():
    p = argparse.ArgumentParser(prog="gestao.py", description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)

    f = sub.add_parser("fetch")
    f.add_argument("key"); f.add_argument("item", type=int); f.add_argument("name")
    f.add_argument("--wiql"); f.add_argument("--filter"); f.add_argument("--fields")
    f.add_argument("--text", action="store_true"); f.add_argument("--top", type=int, default=500)
    f.add_argument("--task", type=int); f.add_argument("--no-save", action="store_true")
    f.set_defaults(fn=cmd_fetch)

    f = sub.add_parser("pull")
    f.add_argument("key"); f.add_argument("item", type=int); f.add_argument("file")
    f.set_defaults(fn=cmd_pull)

    f = sub.add_parser("save")
    f.add_argument("key"); f.add_argument("item", type=int); f.add_argument("file")
    f.add_argument("--name"); f.add_argument("--kind", default="analysis"); f.add_argument("--task", type=int)
    f.set_defaults(fn=cmd_save)

    f = sub.add_parser("pdf")
    f.add_argument("key"); f.add_argument("item", type=int); f.add_argument("file")
    f.add_argument("--name"); f.add_argument("--title"); f.add_argument("--task", type=int)
    f.set_defaults(fn=cmd_pdf)

    f = sub.add_parser("report")
    f.add_argument("data"); f.add_argument("--title"); f.add_argument("--summary"); f.add_argument("--out")
    f.add_argument("--weeks", type=int, default=8); f.add_argument("--top-old", type=int, default=10)
    f.add_argument("--stale-days", type=int, default=14); f.add_argument("--closed-states"); f.add_argument("--today")
    f.set_defaults(fn=cmd_report)

    f = sub.add_parser("duplicates")
    f.add_argument("data"); f.add_argument("--window-days", type=int, default=120); f.add_argument("--min", type=float, default=0.55)
    f.add_argument("--max-groups", type=int, default=30); f.add_argument("--out"); f.add_argument("--json")
    f.set_defaults(fn=cmd_duplicates)

    f = sub.add_parser("stale")
    f.add_argument("data"); f.add_argument("--days", type=int, default=14); f.add_argument("--closed-states"); f.add_argument("--today")
    f.set_defaults(fn=cmd_stale)

    f = sub.add_parser("rca-check")
    f.add_argument("rca"); f.add_argument("card")
    f.set_defaults(fn=cmd_rca_check)

    a = p.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
