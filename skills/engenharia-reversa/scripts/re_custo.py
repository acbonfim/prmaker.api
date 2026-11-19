#!/usr/bin/env python3
"""Custo e tempo da geração da engenharia reversa por documento (feature 0097) — sem LLM, sem rede.

Lê os transcripts do Claude Code (~/.claude/projects/*/<sessão>.jsonl e os subagentes em <sessão>/subagents/*.jsonl) das
sessões que geraram o módulo e soma, por documento: leitura de cache, gravação de cache, entrada, saída, chamadas e o
tempo de parede ATIVO (pausas maiores que --pausa minutos — padrão 5 — não contam). É a régua da meta da 0097 (−50% de tokens e −60%
de tempo por módulo com a mesma qualidade).

  re_custo.py <modulo> [--desde AAAA-MM-DD] [--ate AAAA-MM-DD] [--projetos <pasta>] [--pausa 5] [--json saida.json] [--detalhe]

Atribuição: a sessão principal vai para o documento do último `re.sh start <modulo> <doc>` (ou `re.sh lote <modulo>` →
"lote" = funcional+arquitetura numa passada); cada subagente vai pelo prompt ("Subagente da engenharia reversa — modulo
<m>, documento <doc>, area <a>"), ou, sem esse prompt, para o documento ativo na principal quando ele começou.
"""
import glob
import json
import os
import re
import sys
from collections import Counter, OrderedDict, defaultdict
from datetime import datetime

R_START = r'(?:\$RE|re\.sh)"?\s+(start|lote|fechar|spec)\s+{mod}(?:\s+([a-z-]+))?'
R_SUB = re.compile(r"Subagente da engenharia reversa\s*[—–-]+\s*modulo ([\w-]+),\s*documentos? ([\w+-]+)(?:,\s*area ([\w-]+))?")
FIELDS = ("cache_read", "cache_write", "input", "output")


def ts(value):
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00")).timestamp()
    except (AttributeError, ValueError):
        return None


def read_jsonl(path):
    out = []
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            try:
                out.append(json.loads(line))
            except ValueError:
                continue
    return out


def first_prompt(rows):
    for d in rows:
        if d.get("type") == "user" and not d.get("isMeta"):
            c = (d.get("message") or {}).get("content")
            if isinstance(c, str):
                return c
            if isinstance(c, list):
                return " ".join(b.get("text", "") for b in c if isinstance(b, dict) and b.get("type") == "text")
    return ""


def usage_rows(rows):
    """[(timestamp, modelo, {campos})] por mensagem do assistente (a mesma mensagem vem repetida por bloco: conta uma vez)."""
    seen, out = set(), []
    for d in rows:
        if d.get("type") != "assistant":
            continue
        msg = d.get("message") or {}
        mid = msg.get("id") or d.get("requestId") or d.get("uuid")
        u = msg.get("usage")
        if not u or mid in seen:
            continue
        seen.add(mid)
        out.append((ts(d.get("timestamp")), msg.get("model") or "?", {
            "cache_read": u.get("cache_read_input_tokens") or 0, "cache_write": u.get("cache_creation_input_tokens") or 0,
            "input": u.get("input_tokens") or 0, "output": u.get("output_tokens") or 0}))
    return out


def event_times(rows):
    return [t for t in (ts(d.get("timestamp")) for d in rows if d.get("type") in ("user", "assistant")) if t]


def active_minutes(times, pause_min):
    """Tempo de parede ativo: soma dos intervalos entre eventos consecutivos (de todas as fontes juntas) ≤ pausa."""
    times = sorted(times)
    total = 0.0
    for a, b in zip(times, times[1:]):
        if b - a <= pause_min * 60:
            total += b - a
    return total / 60


def doc_timeline(rows, module):
    """[(timestamp, documento)] dos `start/lote <modulo> <doc>` da sessão principal (na ordem); outro módulo = None."""
    rx_any = re.compile(R_START.format(mod=r"([a-z0-9-]+)"))
    marks = []
    for d in rows:
        if d.get("type") != "assistant":
            continue
        for b in (d.get("message") or {}).get("content") or []:
            if isinstance(b, dict) and b.get("type") == "tool_use":
                inp = b.get("input") or {}
                cmd = " ".join(v for v in inp.values() if isinstance(v, str)) if isinstance(inp, dict) else str(inp)
                for m in rx_any.finditer(cmd):
                    verb, mod, doc = m.group(1), m.group(2), m.group(3)
                    if verb == "fechar":
                        continue
                    doc = {"lote": "lote", "spec": "spec-arquitetura"}.get(verb) or doc or "?"
                    marks.append((ts(d.get("timestamp")) or 0, doc if mod == module else None))
    return marks


def active_doc(marks, t):
    cur = marks[0][1] if marks else None
    for when, doc in marks:
        if t is not None and when <= t:
            cur = doc
    return cur


def collect(module, projects, since=None, until=None, pause=5):
    per_doc = defaultdict(lambda: {"sessions": set(), "subagents": 0, "calls": 0, "main": Counter(), "sub": Counter(), "times": [],
                                   "models": Counter(), "areas": set()})
    needle = module.encode()
    for path in glob.glob(os.path.join(projects, "*", "*.jsonl")):
        try:
            with open(path, "rb") as fh:
                if needle not in fh.read():
                    continue
        except OSError:
            continue
        rows = read_jsonl(path)
        marks = doc_timeline(rows, module)
        if not any(doc for _, doc in marks):
            continue
        start = min((t for t in event_times(rows)), default=None)
        if start and since and start < since:
            continue
        if start and until and start >= until:
            continue
        sid = os.path.basename(path)[:-6]
        for t, model, u in usage_rows(rows):
            doc = active_doc(marks, t)
            if not doc:
                continue
            e = per_doc[doc]
            e["sessions"].add(sid)
            e["calls"] += 1
            e["main"].update(u)
            e["models"][model] += 1
        for t in event_times(rows):
            doc = active_doc(marks, t)
            if doc:
                per_doc[doc]["times"].append(t)
        for sub in glob.glob(os.path.join(path[:-6], "subagents", "*.jsonl")):
            srows = read_jsonl(sub)
            m = R_SUB.search(first_prompt(srows))
            times = event_times(srows)
            if m:
                if m.group(1) != module:
                    continue
                doc = "lote" if "+" in m.group(2) else m.group(2)
            else:
                doc = active_doc(marks, min(times) if times else None)
                if not doc:
                    continue
            e = per_doc[doc]
            e["subagents"] += 1
            if m and m.group(3):
                e["areas"].add(m.group(3))
            for _, model, u in usage_rows(srows):
                e["calls"] += 1
                e["sub"].update(u)
                e["models"][model] += 1
            e["times"].extend(times)
    out = OrderedDict()
    order = ["lote", "funcional", "arquitetura", "consolidar", "spec-arquitetura", "uiux", "pratica", "visao", "design"]
    for doc in sorted(per_doc, key=lambda d: (order.index(d) if d in order else 99, d)):
        e = per_doc[doc]
        tot = e["main"] + e["sub"]
        out[doc] = {"sessions": len(e["sessions"]), "subagents": e["subagents"], "calls": e["calls"],
                    **{f: tot.get(f, 0) for f in FIELDS},
                    "mainCacheRead": e["main"].get("cache_read", 0), "minutes": round(active_minutes(e["times"], pause), 1),
                    "span": [datetime.fromtimestamp(min(e["times"])).isoformat(timespec="minutes"),
                             datetime.fromtimestamp(max(e["times"])).isoformat(timespec="minutes")] if e["times"] else None,
                    "models": dict(e["models"].most_common(3))}
    return out


def fmt_m(n):
    return f"{n / 1e6:.1f} M"


def report(module, data):
    L = [f"Custo da engenharia reversa — {module} (tokens dos transcripts; tempo = parede ativo)",
         f"{'documento':<17}{'cache lido':>11}{'(principal)':>12}{'cache grav.':>12}{'entrada':>9}{'saída':>9}{'chamadas':>9}{'subag.':>7}{'min':>7}"]
    tot = Counter()
    for doc, d in data.items():
        L.append(f"{doc:<17}{fmt_m(d['cache_read']):>11}{fmt_m(d['mainCacheRead']):>12}{fmt_m(d['cache_write']):>12}"
                 f"{d['input'] / 1e3:>8.0f}k{d['output'] / 1e3:>8.0f}k{d['calls']:>9}{d['subagents']:>7}{d['minutes']:>7.0f}")
        tot.update({k: d[k] for k in (*FIELDS, "calls", "subagents", "mainCacheRead")})
        tot["minutes"] += d["minutes"]
    if len(data) > 1:
        L.append(f"{'TOTAL':<17}{fmt_m(tot['cache_read']):>11}{fmt_m(tot['mainCacheRead']):>12}{fmt_m(tot['cache_write']):>12}"
                 f"{tot['input'] / 1e3:>8.0f}k{tot['output'] / 1e3:>8.0f}k{tot['calls']:>9}{tot['subagents']:>7}{tot['minutes']:>7.0f}")
    if not data:
        L.append("(nenhuma sessão de geração deste módulo nos transcripts — confira --desde/--projetos)")
    else:
        L.append("Tempo por documento somado; documentos em paralelo (funcional e arquitetura) se sobrepõem no relógio.")
    return "\n".join(L)


def main(argv):
    if len(argv) < 2 or argv[1] in ("-h", "--help"):
        print(__doc__)
        return 0
    opt = lambda n, d=None: argv[argv.index(n) + 1] if n in argv and argv.index(n) + 1 < len(argv) else d
    day = lambda v: datetime.fromisoformat(v).timestamp() if v else None
    data = collect(argv[1], os.path.expanduser(opt("--projetos", "~/.claude/projects")), day(opt("--desde")), day(opt("--ate")),
                   float(opt("--pausa", "5")))
    print(report(argv[1], data))
    if "--detalhe" in argv:
        for doc, d in data.items():
            print(f"  {doc}: {d['sessions']} sessão(ões) · {d['span'][0]} → {d['span'][1]} · modelos {d['models']}")
    if opt("--json"):
        with open(opt("--json"), "w", encoding="utf-8") as fh:
            json.dump({"module": argv[1], "docs": data}, fh, ensure_ascii=False, indent=1)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
