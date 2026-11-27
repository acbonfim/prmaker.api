#!/usr/bin/env bash
# Termos do Multilingual (0107) — ver o cabecalho de mlg.py. Roda no venv da skill (psycopg); sem venv, python3 + psql.
#   mlg.sh search "<texto>" [--limit N] [--json] | exists "<termo>" [...] | check [--repo <pasta>] [--base origin/master] [--diff <arquivo>|-]
set -euo pipefail
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
DIR="$(cd "$(dirname "$0")" && pwd)"
VENV="$DIR/../.venv/bin/python"
[[ -x "$VENV" ]] || [[ ! -x "$DIR/../.venv/Scripts/python.exe" ]] || VENV="$DIR/../.venv/Scripts/python.exe"  # Windows
export PYTHONUTF8=1
if [[ -x "$VENV" ]]; then exec "$VENV" "$DIR/mlg.py" "$@"; fi
exec python3 "$DIR/mlg.py" "$@"
