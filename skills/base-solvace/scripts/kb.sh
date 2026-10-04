#!/usr/bin/env bash
# Espelho local da Base Solvace (feature 0033): engenharia reversa + Knowledge Center filtrado, em
# ~/.claude/solvace-kb. Ler arquivo local nao custa rede nem chamadas — leia o INDEX.md primeiro e abra so o que precisa.
#
# Uso: kb.sh <comando> [args]
#   sync [--quiet]            baixa o pacote do PRMake so quando o hash muda
#   path                      imprime a pasta do espelho
#   index [termos] [--max N] [--full]
#                             sem termos: indice COMPACTO (uma linha curta por projeto: chave, nome, palavras-chave, ~tokens);
#                             com termos: so os projetos/artigos que casam (linha completa, com as secoes), os N melhores
#                             (default 5); --full: o INDEX.md inteiro (~20 KB — evite na analise). Confere o PRMake se
#                             o espelho tem mais de 6 h; avisa se > 24 h
#   show <projeto> [secao]    sem secao: a ficha do projeto (resumo, depende de / usado por, com evidencia) e as secoes;
#                             com secao: imprime a secao (chave ou parte do nome)
#   find <termo> [max=20]     onde o termo aparece no espelho (arquivo:linha, trecho curto)
#   re find <termos> [--module m] [--kind RN]
#                             ENGENHARIA REVERSA (0052) no espelho: itens publicados (modulo#ID, tipo, titulo, tabelas)
#                             — offline, pelo reverse/<modulo>.tsv. Na analise prefira o MCP prmake_base_search (registra)
#   re get <modulo>#<ID> [...] [--card N]
#                             so o bloco do item (do documento re-* do espelho); --card registra a consulta no PRMake
#   status                    hash local x publicado, quantidades, idade do espelho e agendamento
#   agendar install|uninstall|status|run
#                             sincronizacao em segundo plano (LaunchAgent, a cada KB_SYNC_INTERVAL=7200 s): Knowledge
#                             Center -> PRMake (so com credencial local) e PRMake -> espelho
set -uo pipefail
# Windows/Git Bash (0035): jq sem CRLF e python3 de verdade, mesmo sem os atalhos de ~/bin no PATH.
case "$(uname -s 2>/dev/null)" in MINGW*|MSYS*|CYGWIN*)
  export PATH="$HOME/bin:$PATH" PYTHONUTF8=1
  if [[ "$(jq -rn '"x"' 2>/dev/null)" == $'x\r' ]]; then
    if [[ "$(command jq -b -rn '"x"' 2>/dev/null)" == x ]]; then jq() { command jq -b "$@"; }; else jq() { command jq "$@" | tr -d '\r'; }; fi
  fi
  if ! python3 -c '' >/dev/null 2>&1; then
    if py -3 -c '' >/dev/null 2>&1; then python3() { py -3 "$@"; }; elif python -c '' >/dev/null 2>&1; then python3() { python "$@"; }; fi
  fi ;;
esac
BASE="${PRMAKE_API_BASE:-https://api.softhouse.app.br/api/v1}"
KB="${SOLVACE_KB_DIR:-$HOME/.claude/solvace-kb}"
CMD="${1:-}"; shift || true
QUIET=0; for a in "$@"; do [[ "$a" == "--quiet" ]] && QUIET=1; done
die() { echo "ERRO: $*" >&2; exit 1; }
say() { [[ $QUIET -eq 1 ]] || echo "$*"; }

SELF="$(cd "$(dirname "$0")" && pwd)/$(basename "$0")"
HERE="$(dirname "$SELF")"
PLIST="$HOME/Library/LaunchAgents/br.app.softhouse.solvace-kb.plist"
LOG="$HOME/.claude/solvace-kb-sync.log"
INTERVAL="${KB_SYNC_INTERVAL:-7200}"

token() {
  if [[ -n "${PRMAKE_TOKEN:-}" ]]; then printf '%s' "$PRMAKE_TOKEN"; return; fi
  [[ -f "$HOME/.claude/prmake-token.txt" ]] && { tr -d '\r\n' < "$HOME/.claude/prmake-token.txt"; return; }
  return 1
}

local_hash() { cat "$KB/.hash" 2>/dev/null; }

# Descompacta sem depender do unzip (Windows/Git Bash nao tem): bsdtar do sistema ou zipfile do Python.
extract() {
  if command -v unzip >/dev/null; then unzip -q "$1" -d "$2"; return; fi
  local win_tar="${SYSTEMROOT:-/c/Windows}/System32/tar.exe"
  if [[ -x "$win_tar" ]]; then (cd "$2" && "$win_tar" -xf "$1"); return; fi
  if tar --version 2>/dev/null | grep -q bsdtar; then tar -xf "$1" -C "$2"; return; fi
  python3 -m zipfile -e "$1" "$2"
}

# segundos desde a ultima conferencia com o PRMake (999999 = nunca)
age() {
  local f="$KB/.checked"; [[ -f "$f" ]] || { echo 999999; return; }
  echo $(( $(date +%s) - $(stat -f %m "$f" 2>/dev/null || stat -c %Y "$f") ))
}

do_sync() {
  command -v curl >/dev/null && command -v jq >/dev/null || { say "kb: curl/jq ausentes"; return 1; }
  local tk; tk="$(token)" || { say "kb: sem token do PRMake"; return 1; }
  local tmp; tmp="$(mktemp -d "${TMPDIR:-/tmp}/solvace-kb.XXXXXX")"
  local code; code="$(curl -s --max-time 20 -o "$tmp/m.json" -w '%{http_code}' -H "x-api-key: $tk" "$BASE/Architecture/export/manifest")"
  if [[ "$code" != "200" ]]; then rm -rf "$tmp"; say "kb: manifest HTTP $code (espelho mantido)"; return 1; fi
  local remote; remote="$(jq -r '.hash // empty' "$tmp/m.json")"
  if [[ -n "$remote" && "$remote" == "$(local_hash)" && -f "$KB/INDEX.md" ]]; then
    rm -rf "$tmp"; touch "$KB/.checked"; say "kb: espelho em dia ($remote)"; return 0
  fi
  code="$(curl -s --max-time 120 -o "$tmp/kb.zip" -w '%{http_code}' -H "x-api-key: $tk" "$BASE/Architecture/export")"
  if [[ "$code" != "200" ]]; then rm -rf "$tmp"; say "kb: export HTTP $code (espelho mantido)"; return 1; fi
  mkdir -p "$tmp/new" && extract "$tmp/kb.zip" "$tmp/new" || { rm -rf "$tmp"; say "kb: pacote invalido"; return 1; }
  printf '%s' "$remote" > "$tmp/new/.hash"; touch "$tmp/new/.checked"
  mkdir -p "$(dirname "$KB")"
  rm -rf "$KB.old"; [[ -d "$KB" ]] && mv "$KB" "$KB.old"
  mv "$tmp/new" "$KB" && rm -rf "$KB.old" "$tmp"
  echo "Base Solvace atualizada ($remote): $(jq -r '"\(.projects) projetos, \(.sections) secoes, \(.knowledgeArticles) artigos do KC (\(.knowledgeEnvironment))"' "$KB/manifest.json")"
}

# Indice sem gastar contexto: compacto, filtrado por termos ou inteiro (--full).
index_view() {
  local full=0 max=5 terms=()
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --full) full=1 ;;
      --max) max="${2:-5}"; shift ;;
      --quiet) ;;
      *) terms+=("$1") ;;
    esac
    shift
  done
  if [[ $full -eq 1 ]]; then cat "$KB/INDEX.md"; return; fi
  python3 - "$KB/INDEX.md" "$max" "${terms[*]:-}" "$KB/graph.json" <<'PY'
import json, os, re, sys, unicodedata
path, mx, terms = sys.argv[1], int(sys.argv[2]), sys.argv[3]
graph = sys.argv[4] if len(sys.argv) > 4 else ""
def norm(t): return "".join(c for c in unicodedata.normalize("NFD", t.lower()) if unicodedata.category(c) != "Mn")
PROJ = re.compile(r"^- \*\*(.+?)\*\* `([^`]+)` — (.*)$")
ART = re.compile(r"^- (ART-\d+) (.*)$")
lines = open(path, encoding="utf-8").read().splitlines()
projects, articles = [], []
for l in lines:
    m = PROJ.match(l)
    if m:
        title, key, rest = m.groups()
        parts = rest.split(" · ")
        kw = next((p[4:] for p in parts if p.startswith("kw: ")), "")
        tok = next((re.search(r"\(~\d+ tok\)", p).group(0) for p in parts if re.search(r"\(~\d+ tok\)", p)), "")
        dep = next((p for p in parts if p.startswith("⇄")), "")
        projects.append(dict(line=l, key=key, title=title, desc=parts[0], kw=kw, tok=tok, dep=dep))
        continue
    m = ART.match(l)
    if m:
        articles.append(dict(line=l, key=m.group(1), title=m.group(2).split(" · ")[0]))
if not terms.strip():
    sec = None
    print("# Base Solvace — indice compacto (filtre: kb.sh index <termos> · ficha: kb.sh show <projeto> · secao: kb.sh show <projeto> <secao>)")
    for l in lines:
        if l.startswith("## "):
            print("\n" + l)
            continue
        m = PROJ.match(l)
        if m:
            p = next(x for x in projects if x["line"] == l)
            short = p["title"].split(" — ")[-1]
            kws = ", ".join(p["kw"].split(", ")[:4])
            print(f"- {p['key']} · {short} · {kws} {p['tok']}{(' ' + p['dep']) if p['dep'] else ''}".rstrip())
            continue
        m = ART.match(l)
        if m:
            print(f"- {m.group(1)} {m.group(2).split(' · ')[0][:90]}")
    sys.exit(0)
stop = set("""para com sem uma uns umas dos das nos nas pelo pela que nao when with from that this have into cannot could
be added account erro error bug card solvace product improvement development team revamp legado
legacy modulo module tela screen production producao site planta""".split())
words = [w for w in dict.fromkeys(re.findall(r"[a-z0-9_]{3,}", norm(terms))) if w not in stop]
def score(text, w): return 1 if re.search(r"(?<![a-z0-9])" + re.escape(w), text) else 0
def best(items, n):
    # so o que chega perto do melhor resultado (corta o ruido de palavras soltas)
    items = sorted((x for x in items if x[0] > 0), key=lambda x: -x[0])
    return [l for s, l in items if s >= 0.4 * items[0][0]][:n] if items else []
ps = best([(sum(6 * score(norm(p["key"]), w) + 4 * score(norm(p["title"]), w) + 3 * score(norm(p["kw"]), w)
                + score(norm(p["desc"]), w) for w in words), p["line"][:420]) for p in projects], mx)
# 0045: os dois mundos do mesmo modulo — casou o revamp, mostra o legado (e vice-versa): o bug pode estar em qualquer um.
pairs = {}
try:
    for e in json.load(open(graph, encoding="utf-8")).get("edges", []) if graph and os.path.exists(graph) else []:
        a, b = e.get("source", ""), e.get("target", "")
        if e.get("kind") == "other" and {a.split("-")[0], b.split("-")[0]} == {"legado", "revamp"}:
            pairs.setdefault(a, []).append(b); pairs.setdefault(b, []).append(a)
except Exception:
    pairs = {}
by_line = {p["line"][:420]: p for p in projects}
by_key = {p["key"]: p for p in projects}
chosen = {by_line[l]["key"] for l in ps if l in by_line}
extra = []
for l in list(ps):
    for k in pairs.get(by_line.get(l, {}).get("key", ""), []):
        if k in by_key and k not in chosen and len(extra) < 3:
            chosen.add(k)
            extra.append("  ↳ mesmo modulo no outro mundo: " + by_key[k]["line"][:420])
ps = ps + extra
arts = best([(sum(score(norm(a["title"]), w) for w in words), a["line"][:160]) for a in articles], 3)
ps = [l for l in ps if l]; arts = [l for l in arts if l]
if not ps and not arts:
    print("(nada da Base Solvace casou com: " + " ".join(words) + " — veja o indice compacto: kb.sh index)")
else:
    print("\n".join(ps + arts))
PY
}

scheduled() { launchctl list 2>/dev/null | grep -q br.app.softhouse.solvace-kb; }

case "$CMD" in
  sync) do_sync ;;
  path) echo "$KB" ;;
  index)
    if [[ ! -f "$KB/INDEX.md" ]]; then
      do_sync >/dev/null || die "espelho indisponivel e sem conexao com o PRMake"
    elif (( $(age) > 21600 )); then
      QUIET=1; do_sync >/dev/null 2>&1
    fi
    if (( $(age) > 86400 )); then
      echo "AVISO: Base Solvace sem conferir com o PRMake ha $(( $(age) / 3600 )) h — pode estar desatualizada (kb.sh sync)" >&2
    fi
    index_view "$@" ;;
  show)
    P="${1:?projeto}"; SEC="${2:-}"
    [[ -d "$KB/projects/$P" ]] || { P="$(ls "$KB/projects" 2>/dev/null | grep -i -- "$P" | head -1)"; [[ -n "$P" ]] || die "projeto '${1}' nao esta no espelho (kb.sh index)"; }
    if [[ -z "$SEC" ]]; then
      [[ -f "$KB/projects/$P/000-projeto.md" ]] && cat "$KB/projects/$P/000-projeto.md"
      echo; echo "Arquivos: $(ls "$KB/projects/$P" | sed 's/\.md$//' | tr '\n' ' ')"
      exit 0
    fi
    F="$(ls "$KB/projects/$P" | grep -i -- "$SEC" | head -1)"; [[ -n "$F" ]] || die "secao '$SEC' nao encontrada em $P"
    cat "$KB/projects/$P/$F" ;;
  find)
    T="${1:?termo}"; MAX="${2:-20}"
    [[ -d "$KB" ]] || die "espelho vazio — rode kb.sh sync"
    grep -rIin --include='*.md' -- "$T" "$KB" 2>/dev/null | sed "s|$KB/||" | cut -c1-220 | head -n "$MAX" ;;
  re)
    SUB="${1:-}"; shift || true
    [[ -d "$KB/reverse" ]] || { do_sync >/dev/null 2>&1; [[ -d "$KB/reverse" ]] || die "nenhuma engenharia reversa publicada no espelho (kb.sh sync)"; }
    case "$SUB" in
      find)
        MODF=""; KINDF=""; TERMS=()
        while [[ $# -gt 0 ]]; do case "$1" in --module) MODF="$2"; shift 2;; --kind) KINDF="$2"; shift 2;; *) TERMS+=("$1"); shift;; esac; done
        python3 - "$KB/reverse" "$MODF" "$KINDF" "${TERMS[@]:-}" <<'PY'
import os, sys, unicodedata, re
root, mod, kinds, terms = sys.argv[1], sys.argv[2], sys.argv[3], [t for t in sys.argv[4:] if t]
def n(t): return "".join(c for c in unicodedata.normalize("NFD", t or "") if unicodedata.category(c) != "Mn").lower()
kinds = {k.strip().upper() for k in kinds.split(",") if k.strip()}
words = [w[:-2] if len(w) > 5 and not re.search(r"[\d_]", w) else w for w in re.findall(r"[\w-]{3,}", n(" ".join(terms)))]
hits = []
for f in sorted(os.listdir(root)):
    if not f.endswith(".tsv") or (mod and not f.startswith(mod)): continue
    m = f[:-4]
    for line in open(os.path.join(root, f), encoding="utf-8").read().splitlines()[1:]:
        c = line.split("\t")
        if len(c) < 4 or (kinds and c[1] not in kinds): continue
        text = n(" ".join(c))
        score = sum((4 if w in n(c[3]) else 0) + (2 if w in n(" ".join(c[4:])) else 0) + (1 if w in text else 0) for w in words)
        if words and score == 0: continue
        hits.append((score, f"{m}#{c[0]} [{c[1]}] {c[3]}" + (f" · {c[4]}" if len(c) > 4 and c[4] else "") + f" ({c[2]})"))
hits.sort(key=lambda h: -h[0])
print("\n".join(h[1] for h in hits[:25]) or "Nada no indice da engenharia reversa.")
PY
        ;;
      get)
        CARDF=""; REFS=()
        while [[ $# -gt 0 ]]; do case "$1" in --card) CARDF="$2"; shift 2;; *) REFS+=("$1"); shift;; esac; done
        [[ ${#REFS[@]} -gt 0 ]] || die "uso: kb.sh re get <modulo>#<ID> [...]"
        for R in "${REFS[@]}"; do
          M="${R%%#*}"; ID="${R#*#}"; [[ "$M" == "$R" ]] && die "use <modulo>#<ID> (ex.: revamp-kaizen#RN-012)"
          DOC="$(awk -F'\t' -v id="$ID" '$1 == id { print $3; exit }' "$KB/reverse/$M.tsv" 2>/dev/null)"
          [[ -n "$DOC" ]] || { echo "--- $R: nao encontrado no espelho"; continue; }
          F="$(ls "$KB/projects/$M" 2>/dev/null | grep -- "-re-$DOC.md$" | head -1)"
          echo "--- $R ($DOC)"
          awk -v id="$ID" '
            /^```/ { fence = !fence }
            !fence && /^#+ / { lvl = index($0, " ") - 1
              if (on && lvl <= start) exit
              t = $0; sub(/^#+ +/, "", t); gsub(/[*`]/, "", t)
              if (!on && index(t, id) == 1) { on = 1; start = lvl } }
            on { print }' "$KB/projects/$M/$F"
        done
        if [[ -n "$CARDF" ]] && tk="$(token)"; then
          jq -n --arg c "$CARDF" --args '{card: $c, refs: $ARGS.positional}' "${REFS[@]}" > "${TMPDIR:-/tmp}/kb-consulted.$$"
          curl -s --max-time 10 -o /dev/null -X POST "$BASE/ReverseEngineering/consulted" -H "x-api-key: $tk" -H 'content-type: application/json' \
            --data-binary "@${TMPDIR:-/tmp}/kb-consulted.$$" 2>/dev/null; rm -f "${TMPDIR:-/tmp}/kb-consulted.$$"
        fi
        ;;
      *) die "uso: kb.sh re find <termos> [--module m] [--kind RN] | kb.sh re get <modulo>#<ID> [--card N]" ;;
    esac ;;
  status)
    echo "local: $(local_hash || echo nenhum) em $KB"
    A="$(age)"; if (( A < 999999 )); then echo "conferido com o PRMake ha $(( A / 60 )) min"; else echo "nunca conferido"; fi
    if scheduled; then echo "agendamento: ligado (log: $LOG)"; else echo "agendamento: desligado (kb.sh agendar install)"; fi
    tk="$(token)" && curl -s --max-time 20 -H "x-api-key: $tk" "$BASE/Architecture/export/manifest" | jq -c . ;;
  agendar)
    case "${1:-}" in
      run)
        # throttle: chamado pelo hook de toda sessao e pelo LaunchAgent; KB_FORCE=1 ignora
        RAN="$KB/../.solvace-kb-ran"
        if [[ -z "${KB_FORCE:-}" && -f "$RAN" ]] && (( $(date +%s) - $(stat -f %m "$RAN" 2>/dev/null || stat -c %Y "$RAN") < 1800 )); then exit 0; fi
        mkdir -p "$(dirname "$RAN")"; touch "$RAN"
        {
          echo "$(date '+%F %T') sincronizando"
          # Knowledge Center -> PRMake so em maquina com a credencial (somente leitura); sem ela vale a base do PRMake.
          if [[ -f "$HOME/.claude/knowledgecenter-credentials.json" ]]; then
            bash "$HERE/kc.sh" sync --quiet || echo "kc: sync falhou (segue com a base do PRMake)"
          fi
          QUIET=1; do_sync || echo "kb: sync falhou (espelho mantido)"
        } >> "$LOG" 2>&1 ;;
      install)
        mkdir -p "$(dirname "$PLIST")"
        cat > "$PLIST" <<PLISTEOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>br.app.softhouse.solvace-kb</string>
  <key>ProgramArguments</key><array><string>/bin/bash</string><string>$SELF</string><string>agendar</string><string>run</string></array>
  <key>EnvironmentVariables</key><dict><key>PATH</key><string>$PATH</string></dict>
  <key>StartInterval</key><integer>$INTERVAL</integer>
  <key>RunAtLoad</key><true/>
  <key>StandardErrorPath</key><string>$LOG</string>
</dict></plist>
PLISTEOF
        launchctl unload "$PLIST" 2>/dev/null
        launchctl load "$PLIST" && echo "Sincronizacao agendada a cada $(( INTERVAL / 60 )) min (log: $LOG). Desligar: kb.sh agendar uninstall" ;;
      uninstall) launchctl unload "$PLIST" 2>/dev/null; rm -f "$PLIST"; echo "Agendamento desligado." ;;
      status)
        if scheduled; then echo "agendamento: ligado"; else echo "agendamento: desligado"; fi
        tail -5 "$LOG" 2>/dev/null ;;
      *) die "uso: kb.sh agendar install|uninstall|status|run" ;;
    esac ;;
  *) sed -n '2,15p' "$0"; exit 1 ;;
esac
