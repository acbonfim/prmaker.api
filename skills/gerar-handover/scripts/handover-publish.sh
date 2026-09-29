#!/usr/bin/env bash
# Salva a Passagem de Conhecimento (handover) de um card no PRMake (POST /Handover = upsert por card).
# Uso: handover-publish.sh <cardNumber> <arquivoMarkdown> [repositoryId]
#   [repositoryId] = default: repositoryId do PR salvo ($OUTDIR/pr.json), se houver
# Env opcional:
#   IS_PUBLIC   = true|false - visibilidade na CRIACAO (default true, igual a tela).
#                 Em regeneracao a API preserva a visibilidade atual.
#   VISIBILITY  = true|false - forca a visibilidade apos salvar (PUT /Handover/<card>/visibility)
#   PRMAKE_BASE = URL base da API (default https://api.softhouse.app.br/api/v1)
#   WEB_BASE    = URL do front para montar o link publico (opcional)
set -euo pipefail

CARD="${1:?informe o numero do card}"
FILE="${2:?informe o arquivo markdown do handover}"
OUTDIR="${OUTDIR:-/tmp/handover}"
BASE="${PRMAKE_BASE:-https://api.softhouse.app.br/api/v1}"
IS_PUBLIC="${IS_PUBLIC:-true}"

[[ -s "$FILE" ]] || { echo "ERRO: arquivo $FILE vazio ou inexistente" >&2; exit 1; }

REPO="${3:-}"
if [[ -z "$REPO" && -f "$OUTDIR/pr.json" ]]; then
  REPO="$(jq -r '.repositoryId // ""' "$OUTDIR/pr.json" 2>/dev/null || true)"
fi

resolve_token() {
  if [[ -n "${PRMAKE_TOKEN:-}" ]]; then printf '%s' "$PRMAKE_TOKEN"; return; fi
  local candidates=(
    "$HOME/.claude/prmake-token.txt"
    "${CLAUDE_PROJECT_DIR:-}/.claude/prmake-token.txt"
    "$(git rev-parse --show-toplevel 2>/dev/null)/.claude/prmake-token.txt"
  )
  for f in "${candidates[@]}"; do [[ -f "$f" ]] && { tr -d '\n' < "$f"; return; }; done
  echo "ERRO: token nao encontrado (defina PRMAKE_TOKEN ou crie ~/.claude/prmake-token.txt)" >&2; exit 1
}
TOKEN="$(resolve_token)"
AUTH=(-H "accept: application/json" -H "x-api-key: $TOKEN" -H "Content-Type: application/json")
CARD_ENC="$(jq -rn --arg c "$CARD" '$c|@uri')"

# Remove cerca ```markdown ... ``` caso tenha escapado (a tela faz o mesmo).
CONTENT="$(sed -e '1s/^```[a-zA-Z]*[[:space:]]*$//' -e '$s/^```[[:space:]]*$//' "$FILE" | sed '/./,$!d')"

BODY="$(jq -n --arg card "$CARD" --arg content "$CONTENT" --arg repo "$REPO" --argjson pub "$IS_PUBLIC" \
  '{cardNumber:$card, content:$content, isPublic:$pub} + (if $repo != "" then {repositoryId:$repo} else {} end)')"

echo ">> POST $BASE/Handover"
RESP="$(curl -s --max-time 60 -w '\n%{http_code}' -X POST "$BASE/Handover" "${AUTH[@]}" -d "$BODY")"
CODE="${RESP##*$'\n'}"; OUT="${RESP%$'\n'*}"
echo "HTTP $CODE"
if [[ "$CODE" != "200" ]]; then
  echo "ERRO: ${OUT:0:500}" >&2
  [[ "$CODE" == "401" ]] && echo "   -> token PRMake expirado (~/.claude/prmake-token.txt ou PRMAKE_TOKEN)" >&2
  exit 1
fi
printf '%s' "$OUT" > "$OUTDIR/handover_salvo.json"

if [[ -n "${VISIBILITY:-}" ]]; then
  echo ">> PUT $BASE/Handover/$CARD/visibility (isPublic=$VISIBILITY)"
  RESP="$(curl -s --max-time 60 -w '\n%{http_code}' -X PUT "$BASE/Handover/$CARD_ENC/visibility" "${AUTH[@]}" \
    -d "$(jq -n --argjson v "$VISIBILITY" '{isPublic:$v}')")"
  CODE="${RESP##*$'\n'}"; OUT="${RESP%$'\n'*}"
  echo "HTTP $CODE"
  [[ "$CODE" == "200" ]] && printf '%s' "$OUT" > "$OUTDIR/handover_salvo.json" || echo "AVISO: falha ao alterar visibilidade: ${OUT:0:300}" >&2
fi

jq -r '"id=\(.id)\ncardNumber=\(.cardNumber)\nrepositoryId=\(.repositoryId // "")\nisPublic=\(.isPublic)\ncreatedAt=\(.createdAt)\nupdatedAt=\(.updatedAt // "")"' "$OUTDIR/handover_salvo.json"
[[ -n "${WEB_BASE:-}" ]] && echo "linkPublico=${WEB_BASE%/}/handover/$CARD_ENC"
echo "apiPublica=$BASE/Handover/public/$CARD_ENC"
