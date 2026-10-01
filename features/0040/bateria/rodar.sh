#!/usr/bin/env bash
# Bateria de perguntas de operação (0040): roda cada pergunta no "Pergunte à Base Solvace" e grava a cobertura.
# Uso: rodar.sh <saida.jsonl> [api-base]   (token em ~/.claude/prmake-token.txt)
set -uo pipefail
OUT="${1:?saida.jsonl}"; BASE="${2:-https://api.softhouse.app.br/api/v1}"
DIR="$(cd "$(dirname "$0")" && pwd)"; T=$(tr -d '\r\n' < ~/.claude/prmake-token.txt); : > "$OUT"
one() {
  local tema="${1%%|*}" q="${1#*|}"
  local r; r=$(curl -s --max-time 180 -X POST "$BASE/Architecture/ask" -H "x-api-key: $T" -H 'content-type: application/json' \
    -d "$(jq -n --arg q "$q" '{question:$q}')")
  jq -c --arg tema "$tema" --arg q "$q" '{tema:$tema, pergunta:$q, coverage:(.coverage // "erro"), kind:(.kind // null),
    secao:(if .suggestedSection then "\(.suggestedSection.projectKey)/\(.suggestedSection.sectionKey)" else null end),
    resposta:((.answer // .error // "")[0:300])}' <<<"$r" 2>/dev/null || jq -nc --arg tema "$tema" --arg q "$q" '{tema:$tema,pergunta:$q,coverage:"erro"}'
}
export -f one; export T BASE
xargs -P 4 -I{} bash -c 'one "$@"' _ {} < "$DIR/perguntas.txt" >> "$OUT"
jq -s 'group_by(.coverage) | map({(.[0].coverage): length}) | add' "$OUT"
