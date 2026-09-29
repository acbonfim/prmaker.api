#!/usr/bin/env bash
# Busca (somente leitura) tudo que é preciso para gerar o PR no PRMake.
# Uso: prmake-fetch.sh <cardNumber> <branchFull> [repository]
#   <branchFull>  = ex.: hotfix/54969  (usado para listar commits)
#   [repository]  = default: derivado do git remote origin; fallback edv-solvace
# Saída: escreve arquivos em $OUTDIR (default /tmp/prmake) e imprime um manifesto.
set -euo pipefail

# --- resolve repo slug: arg $3 > git remote origin (nome do repo) > edv-solvace
resolve_repo() {
  if [[ -n "${1:-}" ]]; then printf '%s' "$1"; return; fi
  local url slug
  url="$(git remote get-url origin 2>/dev/null || true)"
  if [[ -n "$url" ]]; then
    slug="${url##*/}"; slug="${slug%.git}"
    if [[ -n "$slug" ]]; then printf '%s' "$slug"; return; fi
  fi
  printf '%s' "edv-solvace"
}

CARD="${1:?informe o numero do card}"
BRANCH="${2:?informe a branch completa, ex: hotfix/54969}"
REPO="$(resolve_repo "${3:-}")"
BASE="${PRMAKE_BASE:-https://api.softhouse.app.br/api/v1}"
OUTDIR="${OUTDIR:-/tmp/prmake}"
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
check() { # <http_code> <label>
  [[ "$1" == "200" ]] || { echo "ERRO: $2 retornou HTTP $1 (token expirado? card inexistente?)" >&2; exit 1; }
}

# --- 2) card ---
RESP="$(get "$BASE/Azure/card/$CARD" "${AUTH[@]}")"; CODE="${RESP##*$'\n'}"; BODY="${RESP%$'\n'*}"
check "$CODE" "GET card"
printf '%s' "$BODY" > "$OUTDIR/card.json"
WIT="$(jq -r '.fields["System.WorkItemType"]' "$OUTDIR/card.json")"
TITLE="$(jq -r '.fields["System.Title"] // ""' "$OUTDIR/card.json")"

# --- 3) prompts (id=3) ---
RESP="$(get "$BASE/PluginConfiguration/get-all-by-id?id=3" "${AUTH[@]}")"; CODE="${RESP##*$'\n'}"; BODY="${RESP%$'\n'*}"
check "$CODE" "GET prompts"
printf '%s' "$BODY" > "$OUTDIR/prompts.json"

# escolhe prompt e description conforme o tipo do card
if [[ "$WIT" == "Bug" ]]; then
  jq -r '.configurations.PromptBug' "$OUTDIR/prompts.json" > "$OUTDIR/prompt.txt"
  # description = ReproSteps (remove tags HTML de forma simples)
  jq -r '.fields["Microsoft.VSTS.TCM.ReproSteps"] // ""' "$OUTDIR/card.json" \
    | sed -e 's/<[^>]*>/ /g' -e 's/&nbsp;/ /g' -e 's/  */ /g' > "$OUTDIR/description.txt"
  IS_BUG=1
else
  jq -r '.configurations.PromptUS' "$OUTDIR/prompts.json" > "$OUTDIR/prompt.txt"
  jq -r '.fields["System.Description"] // ""' "$OUTDIR/card.json" \
    | sed -e 's/<[^>]*>/ /g' -e 's/&nbsp;/ /g' -e 's/  */ /g' > "$OUTDIR/description.txt"
  IS_BUG=0
fi

# prompt do resumo nao-tecnico (feature 0011 do CIME: AI Configurations -> BugSummaryPrompt), o efetivo do
# usuario (GET /Azure/actions/config, o mesmo da tela); sem resposta, o global do plugin.
# Vazio (US ou plugin sem o campo) = a skill usa as regras do passo 4b do SKILL.md.
if [[ "$IS_BUG" == "1" ]]; then
  RESP="$(get "$BASE/Azure/actions/config" "${AUTH[@]}")"; CODE="${RESP##*$'\n'}"; BODY="${RESP%$'\n'*}"
  if [[ "$CODE" == "200" ]] && printf '%s' "$BODY" | jq -e '.available' >/dev/null 2>&1; then
    printf '%s' "$BODY" > "$OUTDIR/actions_config.json"
    jq -r '.bug.summaryPrompt // ""' "$OUTDIR/actions_config.json" > "$OUTDIR/summary_prompt.txt"
  else
    jq -r '.configurations.BugSummaryPrompt // ""' "$OUTDIR/prompts.json" > "$OUTDIR/summary_prompt.txt"
  fi
else
  : > "$OUTDIR/summary_prompt.txt"
fi

# --- 5) commits ---
BRANCH_ENC="${BRANCH//\//%2F}"
RESP="$(get "$BASE/GitHub/commits?repository=$REPO&branch=$BRANCH_ENC" "${AUTH[@]}")"; CODE="${RESP##*$'\n'}"; BODY="${RESP%$'\n'*}"
check "$CODE" "GET commits"
printf '%s' "$BODY" > "$OUTDIR/commits.json"

# seleciona SHAs: commits cujo titulo contem o numero do card;
# se nenhum, pega do topo ate o primeiro commit de merge.
SHALIST="$(jq -r --arg c "$CARD" '[.[] | select(.title|test($c))] | .[].sha' "$OUTDIR/commits.json")"
if [[ -z "$SHALIST" ]]; then
  SHALIST="$(jq -r 'reduce .[] as $x ([]; if (length>0) and ($x.title|test("^Merge")) then . else .+[$x.sha] end)
                    | .[0:20][]' "$OUTDIR/commits.json")"
fi

# --- 6) diffs (um por vez, com timeout) + concatena ---
rm -f "$OUTDIR"/d_*.json
NSHAS=0
while IFS= read -r sha; do
  [[ -z "$sha" ]] && continue
  NSHAS=$((NSHAS+1))
  RESP="$(get "$BASE/GitHub/commit/$sha/diff?repository=$REPO" "${AUTH[@]}")"; CODE="${RESP##*$'\n'}"; BODY="${RESP%$'\n'*}"
  check "$CODE" "GET diff $sha"
  printf '%s' "$BODY" > "$OUTDIR/d_$sha.json"
done <<< "$SHALIST"
if ls "$OUTDIR"/d_*.json >/dev/null 2>&1; then
  jq -rs 'map(.files[] | "### " + .filename + "\n" + .patch) | join("\n")' "$OUTDIR"/d_*.json > "$OUTDIR/diff.txt"
else
  : > "$OUTDIR/diff.txt"
fi

# --- manifesto ---
cat <<EOF
OK
card=$CARD
workItemType=$WIT
isBug=$IS_BUG
title=$TITLE
commitsSelecionados=$NSHAS
outdir=$OUTDIR
arquivos:
  prompt.txt      ($(wc -c < "$OUTDIR/prompt.txt") bytes)
  description.txt ($(wc -c < "$OUTDIR/description.txt") bytes)
  summary_prompt.txt ($(wc -c < "$OUTDIR/summary_prompt.txt") bytes)
  diff.txt        ($(wc -c < "$OUTDIR/diff.txt") bytes, $(grep -c '^### ' "$OUTDIR/diff.txt" 2>/dev/null || echo 0) arquivos)
EOF
