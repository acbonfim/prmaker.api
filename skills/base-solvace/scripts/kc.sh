#!/usr/bin/env bash
# Knowledge Center (regras de negocio) — ver o cabecalho de kc.py. Roda no venv da skill (psycopg).
#   kc.sh check | sync [--full] [--dry-run] [--quiet] | search <termos> [--limit N] | article <n|ART-n>
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
if [[ ! -x "$VENV" ]]; then
  # search/article funcionam sem o venv (espelho local / API); check/sync precisam do psycopg.
  case "${1:-}" in
    search|article) exec python3 "$DIR/kc.py" "$@" ;;
    *) echo "ERRO: venv nao encontrado em $VENV (rode: bash ~/.claude/skills/.prmake/prmake-skills.sh update --force base-solvace)" >&2; exit 1 ;;
  esac
fi
exec "$VENV" "$DIR/kc.py" "$@"
