#!/usr/bin/env bash
# Preenche a classificacao do card (Resolution Type / General Classification / Classification) PELO PRMAKE
# (feature 0028): POST /Azure/card/<card>/actions/classify — usa a integracao do Azure do proprio usuario
# (Minhas integracoes) e registra na Timeline do card. Nada de PAT local nem chamada direta ao Azure.
# Uso: azure-fields.sh <cardNumber>
# Env opcional:
#   RESOLUTION_TYPE        (default "Code Fix")
#   GENERAL_CLASSIFICATION (default "Code")
#   CLASSIFICATION         (default "Code Required - Code Defect")
#   CLASSIFICATION_PRESET  (ex.: user-education) — usa uma opcao do PRMake (GET /Azure/actions/classifications)
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
CARD="${1:?informe o numero do card}"
BASE="${PRMAKE_BASE:-https://api.softhouse.app.br/api/v1}"

resolve_token() {
  if [[ -n "${PRMAKE_TOKEN:-}" ]]; then printf '%s' "$PRMAKE_TOKEN"; return; fi
  local f
  for f in "$HOME/.claude/prmake-token.txt" "${CLAUDE_PROJECT_DIR:-}/.claude/prmake-token.txt"; do
    [[ -f "$f" ]] && { tr -d '\r\n' < "$f"; return; }
  done
  echo "ERRO: token PRMake nao encontrado (defina PRMAKE_TOKEN ou crie ~/.claude/prmake-token.txt)" >&2; exit 1
}
TOKEN="$(resolve_token)"

if [[ -n "${CLASSIFICATION_PRESET:-}" ]]; then
  BODY="$(jq -n --arg p "$CLASSIFICATION_PRESET" '{preset:$p}')"
else
  BODY="$(jq -n --arg rt "${RESOLUTION_TYPE-Code Fix}" --arg gc "${GENERAL_CLASSIFICATION-Code}" \
    --arg cl "${CLASSIFICATION-Code Required - Code Defect}" \
    '{resolutionType:$rt, generalClassification:$gc, classification:$cl}')"
fi

echo ">> POST $BASE/Azure/card/$CARD/actions/classify"
CODE=$(curl -s --max-time 60 -o /tmp/prmake_classify_resp.json -w "%{http_code}" -X POST "$BASE/Azure/card/$CARD/actions/classify" \
  -H 'content-type: application/json' -H "x-api-key: $TOKEN" --data "$BODY")
echo "   HTTP $CODE -> $(jq -r '.message // .error // .' /tmp/prmake_classify_resp.json 2>/dev/null)"
[[ "$CODE" =~ ^2 ]] || { echo "ERRO ao classificar o card (HTTP $CODE)" >&2; exit 1; }
echo "OK (rev $(jq -r '.rev // "?"' /tmp/prmake_classify_resp.json))"
