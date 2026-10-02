#!/usr/bin/env bash
# Cria (idempotente) e imprime a estrutura de pasta de um card.
# Uso: card-init.sh <cardNumber>
# Estrutura criada em $CARDS_DIR/<card> (default ~/.prmake/cards/<card>; 0046 — fora de ~/.claude):
#   scripts/   -> scripts executaveis (SQL etc.). Quando a ORDEM importa,
#                 nomeie 01_nome.sql, 02_nome.sql, ...; rollback como 99_rollback_*.sql
#   analises/  -> textos da analise (markdown), copias do que foi p/ a timeline
#   dados/     -> evidencias/saidas de consulta (json/csv/txt)
# Env: CARDS_DIR sobrescreve a raiz (ex.: um caminho dentro de um repo).
set -euo pipefail
CARD="${1:?informe o numero do card}"
# 0046: a pasta dos cards saiu de ~/.claude/cards — o Claude Code protege ~/.claude e nega gravar ali (o executor, em
# dontAsk, nao conseguia salvar scripts/analises; nem regra Write(~/.claude/cards/**) libera). Sem CARDS_DIR, o card
# que ainda estiver na pasta antiga vem para a nova (mv; se as duas existirem, copia o que falta sem sobrescrever).
LEGACY_CARDS_ROOT="$HOME/.claude/cards"
migrate_card_dir() { # <raiz> <card>
  local root="$1" card="$2" old="$LEGACY_CARDS_ROOT/$2"
  [[ -z "${CARDS_DIR:-}" && -d "$old" && "$root/$card" != "$old" ]] || return 0
  mkdir -p "$root"
  if [[ ! -e "$root/$card" ]]; then
    mv "$old" "$root/$card" 2>/dev/null || cp -R "$old" "$root/$card"
  else
    cp -R -n "$old/." "$root/$card/" 2>/dev/null || true
  fi
}
BASE="${CARDS_DIR:-$HOME/.prmake/cards}"
migrate_card_dir "$BASE" "$CARD"
DIR="$BASE/$CARD"
mkdir -p "$DIR/scripts" "$DIR/analises" "$DIR/dados"
printf '%s\n' "$DIR"
