#!/usr/bin/env bash
# Esteiras de deploy do card na AWS (feature 0100) — SOMENTE LEITURA (CLI da AWS + git local).
#
# Uso:
#   aws.sh collect <card> [--request <id>] [--dry-run] [--out <arquivo>] [--skip-index] [--no-logs] [--envs dev,qa] [--repo <nome>]
#                         coleta e grava o retrato no PRMake (aba AWS da Bancada; MCP prmake_aws_status)
#   aws.sh index [--full] [--seed-dir <pasta>]   só o índice de pipelines do PRMake (novas/alteradas/removidas)
#   aws.sh show <card>                           o retrato gravado no PRMake, resumido
# Perfil: AWS_PROFILE ou o perfil da CLI com acesso à conta da Solvace (PRMAKE_AWS_ACCOUNT, padrão 367983645102).
# Exit: 0 ok · 1 erro (sem CLI/perfil/token, PRMake fora).
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

DIR="$(cd "$(dirname "$0")" && pwd)"
command -v aws >/dev/null 2>&1 || { echo "ERRO: CLI da AWS (aws) não encontrada nesta máquina — instale e rode 'aws configure'" >&2; exit 1; }
[[ $# -ge 1 ]] || { sed -n '2,11p' "$0" >&2; exit 1; }
export PYTHONUNBUFFERED=1
python3 "$DIR/aws_deploy.py" "$@"
