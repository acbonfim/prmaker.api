#!/usr/bin/env python3
"""Consumo da sessao do Claude no plano, lido do transcript (.jsonl).

0033 tokens/turnos · 0041 MCP x script · 0045 base x buscas no codigo · 0047 por modelo ·
0055 de onde a analise LEU: engenharia reversa x base antiga/KC x codigo (confirmacao x exploracao), com os tokens
estimados de cada resultado (tamanho do texto / 4 — sem custo de IA).

Uso: usage_scan.py <transcript.jsonl> <desde-ISO|""> <sessionId> <host>  -> JSON no stdout
"""
import json
import os
import re
import sys

CODE_EXT = r"(?:asp|aspx|ascx|asa|asax|inc|cs|cshtml|razor|vb|ts|tsx|js|jsx|mjs|html|htm|scss|css|sql|py|java|kt|go|xml|config|yml|yaml|sh|ps1|vue|json)"
# Evidencia citada nos itens da engenharia reversa ("**Onde:** `sa3_registro.asp:3184-3199`", "helpers/X.cs:52").
EVIDENCE = re.compile(r"([\w@~.\\/-]*[\w-]\." + CODE_EXT + r")\b(?::\d+(?:-\d+)?)?", re.I)
RE_BLOCK = re.compile(r"=== ENGENHARIA REVERSA.*?(?=\n=== |\Z)", re.S)
RE_REF = re.compile(r"[A-Z]{2,4}-\d{1,4}")
SEARCH = re.compile(r"(^|[\s|;&(])(grep|rg|ag|find|ack)\s")
READ_CMD = re.compile(r"(^|[\s|;&(])(cat|sed|head|tail|less|more|awk|nl)\s")
NOT_CODE = ("/.claude/", "/.prmake/", "solvace-kb", "/tmp/", "/private/", "/scratchpad/", "/anexos", "/.t00", "/skills/")
CARD_FILES = ("dados/", "anexos/", "anexos-prmake/", "analise", "plano", "./")  # relativos = pasta do card
NOT_CODE_EXT = (".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf", ".bmp", ".svg", ".ico", ".zip")
SOURCES = ("re", "base", "code-confirm", "code-explore", "code-search")


def tokens(text):
    return (len(text) + 3) // 4


def result_text(content):
    if isinstance(content, str):
        return content
    out = []
    for c in content or []:
        if isinstance(c, dict) and c.get("type") == "text":
            out.append(str(c.get("text") or ""))
    return "\n".join(out)


def is_code_path(p):
    low = p.lower().replace("\\", "/")
    return (bool(p) and not any(x in low for x in NOT_CODE) and not low.endswith(NOT_CODE_EXT)
            and not low.startswith(CARD_FILES) and not os.path.basename(low).startswith("prmake"))


def file_key(p):
    """O mesmo arquivo lido com caminho relativo e absoluto (ou de outro worktree) conta junto."""
    parts = [x for x in p.replace("\\", "/").split("/") if x]
    return "/".join(parts[-3:]) or p


def classify(name, inp):
    """(origem, arquivo) da chamada; origem None = nao e leitura medida. "code-read" vira confirm/explore depois."""
    cmd = str(inp.get("command", ""))
    path = str(inp.get("file_path", "") or inp.get("path", "") or inp.get("notebook_path", ""))
    if name.startswith("mcp__prmake__prmake_base"):  # 0052: a base pelo MCP
        if name.endswith("_get"):
            refs = [r.strip() for r in str(inp.get("refs", "")).split(",")]
            item = any("#" in r or (RE_REF.fullmatch(r) and not r.upper().startswith("ART-")) for r in refs)
            return ("re" if item else "base"), None
        return "re", None
    if name == "Bash":
        if re.search(r"\bcontexto\b", cmd) and ("prmake-plan" in cmd or "$PLAN" in cmd):
            return "context", None  # so o bloco da engenharia reversa conta (abaixo)
        if "kb.sh" in cmd or re.search(r"\$\{?KB\b", cmd):
            return ("re" if re.search(r"(kb\.sh|\$\{?KB\}?)\S*\s+re\b", cmd) else "base"), None
        if "/re.sh" in cmd:
            return ("re" if re.search(r"re\.sh\S*\s+(get|find|impact|armadilhas|ids|status)\b", cmd) else None), None
        if "solvace-kb" in cmd:
            return ("re" if "/reverse/" in cmd or "-re-" in cmd else "base"), None
        if SEARCH.search(cmd) and "/.claude/" not in cmd and "prmake-" not in cmd:
            return "code-search", None
        if READ_CMD.search(cmd):
            for m in EVIDENCE.finditer(cmd):
                if m.start() > 0 and cmd[m.start() - 1] in "$}":  # $D/x.sh: variavel, nao um arquivo do produto
                    continue
                if is_code_path(m.group(1)) and ("/" in m.group(1) or "\\" in m.group(1)):
                    return "code-read", m.group(1)
        return None, None
    if name in ("Grep", "Glob"):
        return (None if "/.claude/" in path else "code-search"), None
    if name in ("Read", "NotebookRead"):
        if "solvace-kb" in path:
            return ("re" if "/reverse/" in path or "-re-" in os.path.basename(path) else "base"), None
        return ("code-read", path) if is_code_path(path) else (None, None)
    if name in ("Agent", "Task") and str(inp.get("subagent_type", "")).lower() == "explore":
        return "code-explore", "(subagente Explore)"
    return None, None


def scan(path, since):
    seen = {}; model = None; model_of = {}
    mcp, script, kb, search = set(), set(), set(), set()
    calls = {}  # tool_use id -> (origem, arquivo)
    cited = set()  # nomes de arquivo citados pelos itens da engenharia reversa lidos ate aqui
    stats = {k: {"key": k, "calls": 0, "tokens": 0} for k in SOURCES}
    explored = {}
    with open(path, encoding="utf-8") as fh:
        lines = fh.readlines()
    for line in lines:
        try:
            d = json.loads(line)
        except Exception:
            continue
        if since and (d.get("timestamp") or "") < since:
            continue
        m = d.get("message") or {}
        if not isinstance(m, dict):
            continue
        if d.get("type") == "assistant":
            for part in m.get("content") or []:
                if not isinstance(part, dict) or part.get("type") != "tool_use" or part.get("id") in calls:
                    continue
                pid, name, inp = part.get("id"), part.get("name") or "", part.get("input") or {}
                # 0041: chamadas ao PRMake pelo MCP x pelo script (comparativo de consumo no PRMake).
                if name.startswith("mcp__prmake__"): mcp.add(pid)
                elif name == "Bash" and "prmake-plan.sh" in str(inp.get("command", "")): script.add(pid)
                src, f = classify(name, inp)
                if src == "code-read":
                    # 0055: confirmacao = o arquivo que um item da engenharia reversa ja lido cita no Onde:
                    src = "code-confirm" if os.path.basename(f.replace("\\", "/")).lower() in cited else "code-explore"
                calls[pid] = (src, f)
                if src in ("re", "base"): kb.add(pid)  # 0045: kbCalls/searchCalls continuam (comparativos antigos)
                elif src == "code-search": search.add(pid)
                if src in stats: stats[src]["calls"] += 1
            u = m.get("usage")
            if isinstance(u, dict):
                key = m.get("id") or d.get("uuid")
                seen[key] = u
                model = m.get("model") if m.get("model") and m.get("model") != "<synthetic>" else model
                # 0047: cada resposta no modelo que a gerou; "<synthetic>" nao tem custo.
                if m.get("model") and m.get("model") != "<synthetic>": model_of[key] = m.get("model").split("[")[0]
        elif d.get("type") == "user" and isinstance(m.get("content"), list):
            for part in m["content"]:
                if not isinstance(part, dict) or part.get("type") != "tool_result" or part.get("tool_use_id") not in calls:
                    continue
                src, f = calls[part["tool_use_id"]]
                if src is None:
                    continue
                text = result_text(part.get("content"))
                if src == "context":
                    block = RE_BLOCK.search(text)
                    if not block:
                        continue
                    text, src = block.group(0), "re"
                    stats["re"]["calls"] += 1
                if src == "re":
                    cited.update(os.path.basename(x.group(1).replace("\\", "/")).lower() for x in EVIDENCE.finditer(text))
                n = tokens(text)
                stats[src]["tokens"] += n
                if src == "code-explore":
                    key = file_key(f or "?")[-200:]
                    e = explored.setdefault(key, {"path": key, "reads": 0, "tokens": 0})
                    e["reads"] += 1; e["tokens"] += n
    tot = lambda k: sum(int(u.get(k) or 0) for u in seen.values())
    by_model = {}
    for key, u in seen.items():
        if key not in model_of: continue
        e = by_model.setdefault(model_of[key], {"model": model_of[key], "turns": 0, "inputTokens": 0, "outputTokens": 0, "cacheReadTokens": 0, "cacheWriteTokens": 0})
        e["turns"] += 1
        for f, k in (("inputTokens", "input_tokens"), ("outputTokens", "output_tokens"), ("cacheReadTokens", "cache_read_input_tokens"), ("cacheWriteTokens", "cache_creation_input_tokens")):
            e[f] += int(u.get(k) or 0)
    return {"turns": len(seen), "inputTokens": tot("input_tokens"), "outputTokens": tot("output_tokens"),
            "cacheReadTokens": tot("cache_read_input_tokens"), "cacheWriteTokens": tot("cache_creation_input_tokens"),
            "model": model, "mcpCalls": len(mcp), "scriptCalls": len(script), "kbCalls": len(kb), "searchCalls": len(search),
            "models": list(by_model.values()),
            "sources": [stats[k] for k in SOURCES],
            "exploredFiles": sorted(explored.values(), key=lambda e: -e["tokens"])[:10]}


if __name__ == "__main__":
    transcript, since, sid, host = (sys.argv[1:5] + ["", "", "", ""])[:4]
    out = {"sessionId": sid, "host": host}
    out.update(scan(transcript, since))
    print(json.dumps(out))
