#!/usr/bin/env bash
# Le so o trecho necessario das referencias da analisar-bug. Tudo o que entra no contexto e relido em cada resposta
# seguinte: um arquivo inteiro lido cedo custa caro ate o fim da sessao — leia a secao, na hora em que for usar.
#
# Uso: ref.sh                       lista as referencias e as secoes de cada uma (com ~tokens)
#      ref.sh <arquivo>             lista as secoes de um arquivo (parte do nome: plano, consultas, catalogo, analise, correcao)
#      ref.sh <arquivo> <secao>     imprime so a secao (parte do titulo, sem diferenciar maiusculas: "fechamento", "7.", "cognito")
set -uo pipefail
DIR="$(cd "$(dirname "$0")/../references" 2>/dev/null && pwd)" || { echo "ERRO: pasta references nao encontrada" >&2; exit 1; }

# Secoes = titulos "## " e "### " fora de blocos de codigo (comentarios "# " dentro de ```bash nao contam).
sections() {
  awk '
    /^[> ]*```/ { fence = !fence }
    !fence && /^##+ / { if (t != "") printf "  %s (~%d tok)\n", t, n / 4; t = $0; sub(/^#+ /, "", t); n = 0 }
    { n += length($0) + 1 }
    END { if (t != "") printf "  %s (~%d tok)\n", t, n / 4 }' "$1"
}

if [[ $# -eq 0 ]]; then
  for f in "$DIR"/*.md; do echo "$(basename "$f" .md)"; sections "$f"; done
  echo "(ref.sh <arquivo> <secao> imprime so a secao)"
  exit 0
fi

F="$(ls "$DIR" | grep -i -- "$1" | head -1)"
[[ -n "$F" ]] || { echo "ERRO: referencia '$1' nao existe — opcoes: $(ls "$DIR" | sed 's/\.md$//' | tr '\n' ' ')" >&2; exit 1; }
if [[ $# -lt 2 ]]; then echo "${F%.md}"; sections "$DIR/$F"; exit 0; fi

OUT="$(awk -v q="$(printf '%s' "$2" | tr 'A-Z' 'a-z')" '
  /^[> ]*```/ { fence_toggle = 1 }
  {
    if (!fence && /^##+ /) {
      lvl = index($0, " ") - 1
      if (on && lvl <= level) exit
      if (!on && index(tolower($0), q) > 0) { on = 1; level = lvl }
    }
    if (on) print
    if (fence_toggle) { fence = !fence; fence_toggle = 0 }
  }' "$DIR/$F")"
[[ -n "$OUT" ]] || { echo "ERRO: secao '$2' nao encontrada em ${F%.md} — secoes:" >&2; sections "$DIR/$F" >&2; exit 1; }
printf '%s\n' "$OUT"
