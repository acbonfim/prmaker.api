#!/usr/bin/env bash
# Busca (somente leitura) os dados do card necessarios para a analise inicial de um bug.
# Uso: bug-fetch.sh <cardNumber>
# Saida: escreve arquivos em $OUTDIR (default /tmp/bug-analysis) e imprime um manifesto.
#   card.json       -> dados brutos do card (para conferencia)
#   description.txt  -> ReproSteps (Bug) ou System.Description (US), sem HTML
# Env opcional:
#   PRMAKE_TOKEN = sobrescreve o token (senao resolve via arquivos, igual ao gerar-prmake).
#   OUTDIR       = diretorio de saida (default /tmp/bug-analysis)
set -euo pipefail

CARD="${1:?informe o numero do card}"
BASE="${PRMAKE_BASE:-https://api.softhouse.app.br/api/v1}"
OUTDIR="${OUTDIR:-/tmp/bug-analysis}"
mkdir -p "$OUTDIR"

# --- resolve token: env PRMAKE_TOKEN > ~/.claude > $CLAUDE_PROJECT_DIR/.claude > repo/.claude > relativo ao script
resolve_token() {
  if [[ -n "${PRMAKE_TOKEN:-}" ]]; then printf '%s' "$PRMAKE_TOKEN"; return; fi
  local candidates=(
    "$HOME/.claude/prmake-token.txt"
    "${CLAUDE_PROJECT_DIR:-}/.claude/prmake-token.txt"
    "$(git rev-parse --show-toplevel 2>/dev/null)/solvace-core/.claude/prmake-token.txt"
    "$(git rev-parse --show-toplevel 2>/dev/null)/.claude/prmake-token.txt"
    "$(cd "$(dirname "$0")/../.." && pwd)/prmake-token.txt"
  )
  for f in "${candidates[@]}"; do
    if [[ -f "$f" ]]; then tr -d '\n' < "$f"; return; fi
  done
  echo "ERRO: token nao encontrado (defina PRMAKE_TOKEN ou crie ~/.claude/prmake-token.txt)" >&2
  exit 1
}
TOKEN="$(resolve_token)"
AUTH=(-H "accept: application/json" -H "x-api-key: $TOKEN")

get() { curl -s --max-time 60 -w '\n%{http_code}' "$@"; }

# --- card ---
RESP="$(get "$BASE/Azure/card/$CARD" "${AUTH[@]}")"; CODE="${RESP##*$'\n'}"; BODY="${RESP%$'\n'*}"
[[ "$CODE" == "200" ]] || { echo "ERRO: GET card retornou HTTP $CODE (token expirado? card inexistente?)" >&2; exit 1; }
printf '%s' "$BODY" > "$OUTDIR/card.json"

WIT="$(jq -r '.fields["System.WorkItemType"] // ""' "$OUTDIR/card.json")"
TITLE="$(jq -r '.fields["System.Title"] // ""' "$OUTDIR/card.json")"
STATE="$(jq -r '.fields["System.State"] // ""' "$OUTDIR/card.json")"
AREA="$(jq -r '.fields["System.AreaPath"] // ""' "$OUTDIR/card.json")"
# Fluxo de branches pela area do card, conforme o PRMake (Skills Configurations → BranchFlowByArea, 0030).
FLOW=""
RESP="$(get "$BASE/Skills/config" "${AUTH[@]}")"; CODE="${RESP##*$'\n'}"; BODY="${RESP%$'\n'*}"
if [[ "$CODE" == "200" ]]; then
  printf '%s' "$BODY" > "$OUTDIR/skills-config.json"
  FLOW="$(jq -r --arg a "$AREA" '[.settings.BranchFlowByArea // [] | .[] | . as $r
    | select($a != "" and (($a | ascii_downcase) | contains($r.areaContains | ascii_downcase))) | $r.flow][0] // empty' "$OUTDIR/skills-config.json")"
fi
FLOW="${FLOW:-perguntar}"

# description = ReproSteps (Bug) ou System.Description (US), sem HTML
if [[ "$WIT" == "Bug" ]]; then
  jq -r '.fields["Microsoft.VSTS.TCM.ReproSteps"] // ""' "$OUTDIR/card.json" \
    | sed -e 's/<[^>]*>/ /g' -e 's/&nbsp;/ /g' -e 's/&amp;/\&/g' -e 's/&lt;/</g' -e 's/&gt;/>/g' -e 's/  */ /g' > "$OUTDIR/description.txt"
  IS_BUG=1
else
  jq -r '.fields["System.Description"] // ""' "$OUTDIR/card.json" \
    | sed -e 's/<[^>]*>/ /g' -e 's/&nbsp;/ /g' -e 's/&amp;/\&/g' -e 's/&lt;/</g' -e 's/&gt;/>/g' -e 's/  */ /g' > "$OUTDIR/description.txt"
  IS_BUG=0
fi

cat <<EOF
OK
card=$CARD
workItemType=$WIT
isBug=$IS_BUG
state=$STATE
area=$AREA
fluxo=$FLOW
title=$TITLE
outdir=$OUTDIR
arquivos:
  card.json       ($(wc -c < "$OUTDIR/card.json") bytes)
  description.txt ($(wc -c < "$OUTDIR/description.txt") bytes)
EOF
