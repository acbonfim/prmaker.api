#!/usr/bin/env bash
# Cria (idempotente) e imprime a estrutura de pasta de um card.
# Uso: card-init.sh <cardNumber>
# Estrutura criada em $CARDS_DIR/<card> (default ~/.claude/cards/<card>):
#   scripts/   -> scripts executaveis (SQL etc.). Quando a ORDEM importa,
#                 nomeie 01_nome.sql, 02_nome.sql, ...; rollback como 99_rollback_*.sql
#   analises/  -> textos da analise (markdown), copias do que foi p/ a timeline
#   dados/     -> evidencias/saidas de consulta (json/csv/txt)
# Env: CARDS_DIR sobrescreve a raiz (ex.: um caminho dentro de um repo).
set -euo pipefail
CARD="${1:?informe o numero do card}"
BASE="${CARDS_DIR:-$HOME/.claude/cards}"
DIR="$BASE/$CARD"
mkdir -p "$DIR/scripts" "$DIR/analises" "$DIR/dados"
printf '%s\n' "$DIR"
