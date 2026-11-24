#!/usr/bin/env bash
# gestao.sh — atalho do gestao.py (tarefas de gestao, 0099): consulta ao DevOps pelo PRMake, relatorio, duplicados,
# parados, arquivos e PDF do item, conferencia do RCA. Uso e opcoes: gestao.sh --help (o mesmo do gestao.py).
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
export PYTHONIOENCODING=utf-8
python3 "$(cd "$(dirname "$0")" && pwd)/gestao.py" "$@"
