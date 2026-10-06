#!/usr/bin/env bash
# test-changed.sh <worktree> [<base-ref>] [--max-seconds N] — valida a correcao rodando SO os testes do que mudou (0069).
#
# Arquivos alterados = commits desde a base + nao commitados + novos. Para cada arquivo de codigo, o spec ao lado
# (x.ts -> x.spec.ts / x.test.ts) e os specs alterados. Roda UMA vez, sem cobertura, com limite de tempo que mata o
# runner inteiro (padrao 420 s — abaixo dos 600 s do Bash). Base: <base-ref>, senao a upstream da branch (quando nao e
# a propria branch no origin), senao a branch padrao mais proxima (master, development, qa...).
# Saida curta (PASS/FAIL/resumo; falha -> fim do log) e o log inteiro num arquivo.
# Exit: 0 passou (ou nenhum spec para o que mudou) · 1 falhou · 124 LENTO (nao repita: o CI do PR valida) ·
#       3 sem runner conhecido (valide pelo build do repositorio).
set -uo pipefail
# Windows/Git Bash (0035): jq sem CRLF e python3 de verdade, mesmo sem os atalhos de ~/bin no PATH.
case "$(uname -s 2>/dev/null)" in MINGW*|MSYS*|CYGWIN*)
  export PATH="$HOME/bin:$PATH" PYTHONUTF8=1
  if ! python3 -c '' >/dev/null 2>&1; then
    if py -3 -c '' >/dev/null 2>&1; then python3() { py -3 "$@"; }; elif python -c '' >/dev/null 2>&1; then python3() { python "$@"; }; fi
  fi ;;
esac

WT=""; BASE=""; MAX="${PRMAKE_TEST_MAX_SECONDS:-420}"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --max-seconds) MAX="${2:?informe os segundos}"; shift 2 ;;
    -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
    *) if [[ -z "$WT" ]]; then WT="$1"; else BASE="$1"; fi; shift ;;
  esac
done
[[ -n "$WT" ]] || { echo "uso: test-changed.sh <worktree> [<base-ref>] [--max-seconds N]" >&2; exit 2; }
TOP="$(git -C "$WT" rev-parse --show-toplevel 2>/dev/null)" || { echo "ERRO: nao e um repositorio git: $WT" >&2; exit 2; }
cd "$TOP" || exit 2

BRANCH="$(git rev-parse --abbrev-ref HEAD 2>/dev/null)"
if [[ -z "$BASE" ]]; then
  UP="$(git rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' 2>/dev/null)"
  [[ -n "$UP" && "$UP" != "origin/$BRANCH" ]] && BASE="$UP"
fi
if [[ -z "$BASE" ]]; then
  BEST=""
  for B in master main development develop qa release-candidate release-version edge; do
    git rev-parse --verify -q "origin/$B^{commit}" >/dev/null || continue
    N="$(git rev-list --count "origin/$B..HEAD" 2>/dev/null)" || continue
    if [[ -z "$BEST" || "$N" -lt "$BEST" ]]; then BEST="$N"; BASE="origin/$B"; fi
  done
fi
[[ -n "$BASE" ]] || { echo "ERRO: nao achei a branch base — informe: test-changed.sh $WT origin/<base>" >&2; exit 2; }

CHANGED="$( { git diff --name-only --diff-filter=d "$BASE...HEAD" 2>/dev/null
              git diff --name-only --diff-filter=d HEAD 2>/dev/null
              git ls-files --others --exclude-standard 2>/dev/null; } | sort -u)"
SPECS=()
add_spec() { local s; for s in "${SPECS[@]+"${SPECS[@]}"}"; do [[ "$s" == "$1" ]] && return; done; SPECS+=("$1"); }
while IFS= read -r F; do
  [[ -n "$F" && -f "$F" ]] || continue
  case "$F" in
    *.spec.ts|*.spec.tsx|*.spec.js|*.spec.jsx|*.test.ts|*.test.tsx|*.test.js|*.test.jsx) add_spec "$F" ;;
    *.d.ts) ;;
    *.ts|*.tsx|*.js|*.jsx)
      for C in "${F%.*}.spec.${F##*.}" "${F%.*}.test.${F##*.}"; do [[ -f "$C" ]] && add_spec "$C"; done ;;
  esac
done <<< "$CHANGED"

echo "base=$BASE alterados=$(grep -c . <<< "$CHANGED") specs=${#SPECS[@]}"
if [[ ${#SPECS[@]} -eq 0 ]]; then
  echo "SEM-SPEC: nenhum teste para os arquivos alterados — o CI do PR roda o build e os testes."
  exit 0
fi
printf '  %s\n' "${SPECS[@]}"

if [[ ! -f node_modules/jest/bin/jest.js ]]; then
  if [[ -f package.json ]] && grep -q '"jest' package.json; then
    echo "SEM-RUNNER: o worktree nao tem node_modules (o prmake-plan.sh worktree liga o do clone principal em macOS/Linux)." \
         "Nao instale dependencias aqui: valide pelo CI do PR."
  else
    echo "SEM-RUNNER: nao e um projeto jest — valide pelo build do repositorio (ex.: dotnet build / mvn -q compile)."
  fi
  exit 3
fi

LOG="$(mktemp "${TMPDIR:-/tmp}/test-changed.XXXXXX")"
START=$SECONDS
python3 - "$MAX" "$LOG" node --max-old-space-size=2048 node_modules/jest/bin/jest.js --coverage=false --maxWorkers=2 \
  --runTestsByPath "${SPECS[@]}" <<'PY'
import os, signal, subprocess, sys
limit, log, cmd = int(sys.argv[1]), sys.argv[2], sys.argv[3:]
posix = os.name == "posix"
with open(log, "wb") as out:
    p = subprocess.Popen(cmd, stdout=out, stderr=subprocess.STDOUT, start_new_session=posix)
    try:
        sys.exit(p.wait(timeout=limit))
    except subprocess.TimeoutExpired:
        if posix:
            os.killpg(p.pid, signal.SIGKILL)
        else:
            subprocess.run(["taskkill", "/F", "/T", "/PID", str(p.pid)], capture_output=True)
        p.wait()
        sys.exit(124)
PY
RC=$?
ELAPSED=$((SECONDS - START))
SUMMARY='^(PASS|FAIL) |^Tests:|^Test Suites:|error TS[0-9]+'
case "$RC" in
  0) grep -E "$SUMMARY" "$LOG"; echo "OK: testes passaram em ${ELAPSED}s (log: $LOG)" ;;
  124) grep -E "$SUMMARY" "$LOG"
       echo "LENTO: o jest nao terminou em ${MAX}s e foi encerrado (log: $LOG). Nao repita com outros parametros:" \
            "registre no plano (log) e siga para o PR — o CI do PR roda o build e os testes." ;;
  *) grep -E "$SUMMARY" "$LOG"; echo "----- fim do log -----"; tail -80 "$LOG"
     echo "FALHOU (exit $RC) em ${ELAPSED}s (log: $LOG)"; RC=1 ;;
esac
exit "$RC"
