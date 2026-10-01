#!/usr/bin/env bash
# Escreve uma entrada na Timeline do PRMake para um card.
# Uso: prmake-timeline.sh <cardNumber> <description>
#   <cardNumber>  = numero do card (ex.: 72517). Se omitido, tenta derivar da branch atual
#                   (hotfix/<card> ou bugfix/<card>).
#   <description> = texto a registrar na timeline. Se omitido, le do STDIN.
# Env opcional:
#   PRMAKE_TOKEN  = sobrescreve o token (senao resolve via arquivos, igual ao gerar-prmake).
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

BASE="${PRMAKE_TIMELINE_BASE:-https://api.softhouse.app.br/api/v1}"

resolve_token() {
  if [[ -n "${PRMAKE_TOKEN:-}" ]]; then printf '%s' "$PRMAKE_TOKEN"; return; fi
  local candidates=(
    "$HOME/.claude/prmake-token.txt"
    "${CLAUDE_PROJECT_DIR:-}/.claude/prmake-token.txt"
    "$(git rev-parse --show-toplevel 2>/dev/null)/solvace-core/.claude/prmake-token.txt"
    "$(git rev-parse --show-toplevel 2>/dev/null)/.claude/prmake-token.txt"
    "$(cd "$(dirname "$0")/../.." && pwd)/prmake-token.txt"
  )
  for f in "${candidates[@]}"; do [[ -f "$f" ]] && { tr -d '\r\n' < "$f"; return; }; done
  echo "ERRO: token nao encontrado (defina PRMAKE_TOKEN ou crie ~/.claude/prmake-token.txt)" >&2; exit 1
}

# --- card: arg $1 ou derivado da branch (hotfix/<card> | bugfix/<card>)
CARD="${1:-}"
if [[ -z "$CARD" ]]; then
  BRANCH="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || true)"
  if [[ "$BRANCH" =~ ^(hotfix|bugfix)/([0-9]+) ]]; then
    CARD="${BASH_REMATCH[2]}"
  fi
fi
[[ -n "$CARD" ]] || { echo "ERRO: informe o numero do card (arg 1) ou esteja numa branch hotfix/<card>|bugfix/<card>" >&2; exit 1; }

# --- description: arg $2 ou STDIN
DESC="${2:-}"
if [[ -z "$DESC" ]]; then DESC="$(cat)"; fi
[[ -n "$DESC" ]] || { echo "ERRO: descricao vazia (arg 2 ou STDIN)" >&2; exit 1; }

TOKEN="$(resolve_token)"

jq -n --arg card "$CARD" --arg desc "$DESC" \
  '{cardNumber:$card, description:$desc}' > /tmp/prmake_timeline_body.json

echo ">> POST $BASE/Timeline (card $CARD)"
CODE=$(curl -s --max-time 60 -o /tmp/prmake_timeline_resp.json -w "%{http_code}" -X POST "$BASE/Timeline" \
  -H 'accept: application/json, text/plain, */*' \
  -H 'content-type: application/json' \
  -H "x-api-key: $TOKEN" \
  --data @/tmp/prmake_timeline_body.json)
echo "   HTTP $CODE -> $(cat /tmp/prmake_timeline_resp.json)"
[[ "$CODE" =~ ^2 ]] || { echo "ERRO ao escrever na timeline" >&2; exit 1; }
echo "OK"
