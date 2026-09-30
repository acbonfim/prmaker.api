#!/usr/bin/env bash
# Knowledge Center (regras de negocio) — ver o cabecalho de kc.py. Roda no venv da skill (psycopg).
#   kc.sh check | sync [--full] [--dry-run] [--quiet] | search <termos> [--limit N] | article <n|ART-n>
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
VENV="$DIR/../.venv/bin/python"
if [[ ! -x "$VENV" ]]; then
  # search/article funcionam sem o venv (espelho local / API); check/sync precisam do psycopg.
  case "${1:-}" in
    search|article) exec python3 "$DIR/kc.py" "$@" ;;
    *) echo "ERRO: venv nao encontrado em $VENV (rode: bash ~/.claude/skills/.prmake/prmake-skills.sh update --force base-solvace)" >&2; exit 1 ;;
  esac
fi
exec "$VENV" "$DIR/kc.py" "$@"
