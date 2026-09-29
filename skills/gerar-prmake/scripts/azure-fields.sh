#!/usr/bin/env bash
# Preenche os campos custom de classificacao no card do Azure DevOps (JSON Patch),
# escrevendo DIRETAMENTE na API do Azure DevOps, autenticando como o dono do PAT.
# Uso: azure-fields.sh <cardNumber>
# Env opcional (sobrescrevem os defaults; defina como "" para NAO tocar no campo):
#   RESOLUTION_TYPE        (default "Code Fix")                     -> Custom.ResolutionType
#   GENERAL_CLASSIFICATION (default "Code")                         -> Custom.GeneralClassification
#   CLASSIFICATION         (default "Code Required - Code Defect")  -> Custom.Classification
set -euo pipefail

CARD="${1:?informe o numero do card}"
ORG="${AZURE_ORG:-solvacelabs}"
PROJECT="${AZURE_PROJECT:-Solvace Product Improvement}"
API_VERSION="${AZURE_API_VERSION:-7.1-preview.3}"

RESOLUTION_TYPE="${RESOLUTION_TYPE-Code Fix}"
GENERAL_CLASSIFICATION="${GENERAL_CLASSIFICATION-Code}"
CLASSIFICATION="${CLASSIFICATION-Code Required - Code Defect}"

# --- resolve o PAT do Azure: env AZURE_DEVOPS_TOKEN > ~/.claude > $CLAUDE_PROJECT_DIR/.claude > relativo ao script
resolve_azure_token() {
  if [[ -n "${AZURE_DEVOPS_TOKEN:-}" ]]; then printf '%s' "$AZURE_DEVOPS_TOKEN"; return; fi
  local candidates=(
    "$HOME/.claude/azure-devops-token.txt"
    "${CLAUDE_PROJECT_DIR:-}/.claude/azure-devops-token.txt"
    "$(cd "$(dirname "$0")/../.." && pwd)/azure-devops-token.txt"
  )
  for f in "${candidates[@]}"; do [[ -f "$f" ]] && { tr -d '\n' < "$f"; return; }; done
  echo "ERRO: token azure nao encontrado (defina AZURE_DEVOPS_TOKEN ou crie ~/.claude/azure-devops-token.txt)" >&2
  exit 1
}
PAT="$(resolve_azure_token)"
AUTH_B64="$(printf ':%s' "$PAT" | base64 | tr -d '\n')"

# --- monta o JSON Patch apenas com os campos que tem valor (env vazio = pula o campo) ---
PATCH="$(jq -n \
  --arg rt "$RESOLUTION_TYPE" --arg gc "$GENERAL_CLASSIFICATION" --arg cl "$CLASSIFICATION" '
  [
    {ref:"Custom.ResolutionType",        v:$rt},
    {ref:"Custom.GeneralClassification", v:$gc},
    {ref:"Custom.Classification",        v:$cl}
  ]
  | map(select(.v != ""))
  | map({op:"add", path:("/fields/"+.ref), value:.v})')"

if [[ "$PATCH" == "[]" ]]; then
  echo ">> nenhum campo custom para preencher (todos vazios) - pulado"
  exit 0
fi
printf '%s' "$PATCH" > /tmp/prmake_fields_body.json

PROJECT_ENC="$(printf '%s' "$PROJECT" | sed 's/ /%20/g')"
URL="https://dev.azure.com/$ORG/$PROJECT_ENC/_apis/wit/workitems/$CARD?api-version=$API_VERSION"

echo ">> PATCH $URL"
echo "   campos: ResolutionType='$RESOLUTION_TYPE' GeneralClassification='$GENERAL_CLASSIFICATION' Classification='$CLASSIFICATION'"
CODE=$(curl -s --max-time 60 -o /tmp/prmake_fields_resp.json -w "%{http_code}" -X PATCH "$URL" \
  -H "Authorization: Basic $AUTH_B64" -H 'content-type: application/json-patch+json' \
  --data @/tmp/prmake_fields_body.json)
echo "   HTTP $CODE (rev: $(jq -r '.rev // "?"' /tmp/prmake_fields_resp.json 2>/dev/null))"
[[ "$CODE" =~ ^2 ]] || { echo "ERRO ao preencher campos custom no card $CARD:"; cat /tmp/prmake_fields_resp.json; exit 1; }
echo "CONCLUIDO (campos custom preenchidos no card $CARD)"
