#!/usr/bin/env bash
# Posta (ou atualiza) um comentario nao-tecnico (PT-BR + EN-US) na DISCUSSION do card
# no Azure DevOps. Escreve DIRETAMENTE na API do Azure DevOps, autenticando como o dono do PAT.
# Uso: azure-comment.sh <cardNumber> <commentMdFile> [commentId]
#   <commentMdFile> = arquivo Markdown com o comentario (sera convertido para HTML)
#   [commentId]     = se informado (ou env COMMENT_ID), ATUALIZA (PATCH) esse comentario
#                     em vez de criar um novo (POST). Util para iterar sem poluir o card.
set -euo pipefail

CARD="${1:?informe o numero do card}"
MD_FILE="${2:?informe o arquivo markdown do comentario}"
COMMENT_ID="${3:-${COMMENT_ID:-}}"
ORG="${AZURE_ORG:-solvacelabs}"
PROJECT="${AZURE_PROJECT:-Solvace Product Improvement}"
API_VERSION="${AZURE_API_VERSION:-7.1-preview.4}"

[[ -f "$MD_FILE" ]] || { echo "ERRO: arquivo de comentario nao encontrado: $MD_FILE" >&2; exit 1; }

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

# a discussion do DevOps renderiza HTML -> converte o Markdown do comentario
HTML="$(python3 "$(dirname "$0")/md2html.py" "$MD_FILE")"
jq -n --arg t "$HTML" '{text:$t}' > /tmp/prmake_comment_body.json

# URL-encode do nome do projeto (espacos)
PROJECT_ENC="$(printf '%s' "$PROJECT" | sed 's/ /%20/g')"
BASE_URL="https://dev.azure.com/$ORG/$PROJECT_ENC/_apis/wit/workItems/$CARD/comments"

if [[ -n "$COMMENT_ID" ]]; then
  METHOD="PATCH"; URL="$BASE_URL/$COMMENT_ID?api-version=$API_VERSION"; ACAO="atualizado"
else
  METHOD="POST";  URL="$BASE_URL?api-version=$API_VERSION";           ACAO="postado"
fi

echo ">> $METHOD $URL"
CODE=$(curl -s --max-time 60 -o /tmp/prmake_comment_resp.json -w "%{http_code}" -X "$METHOD" "$URL" \
  -H "Authorization: Basic $AUTH_B64" -H 'content-type: application/json' \
  --data @/tmp/prmake_comment_body.json)
echo "   HTTP $CODE (commentId: $(jq -r '.id // "?"' /tmp/prmake_comment_resp.json 2>/dev/null))"
[[ "$CODE" =~ ^2 ]] || { echo "ERRO ao $METHOD comentario na discussion:"; cat /tmp/prmake_comment_resp.json; exit 1; }
echo "CONCLUIDO (comentario $ACAO na discussion do card $CARD)"
