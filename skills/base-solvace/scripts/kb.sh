#!/usr/bin/env bash
# Espelho local da Base Solvace (feature 0033): engenharia reversa + Knowledge Center filtrado, em
# ~/.claude/solvace-kb. Ler arquivo local nao custa rede nem chamadas — leia o INDEX.md primeiro e abra so o que precisa.
#
# Uso: kb.sh <comando> [args]
#   sync [--quiet]            baixa o pacote do PRMake so quando o hash muda
#   path                      imprime a pasta do espelho
#   index                     imprime o indice (sincroniza antes se nao houver espelho)
#   show <projeto> [secao]    lista as secoes do projeto ou imprime uma secao (chave ou parte do nome)
#   find <termo> [max=20]     onde o termo aparece no espelho (arquivo:linha, trecho curto)
#   status                    hash local x publicado, quantidades
set -uo pipefail
BASE="${PRMAKE_API_BASE:-https://api.softhouse.app.br/api/v1}"
KB="${SOLVACE_KB_DIR:-$HOME/.claude/solvace-kb}"
CMD="${1:-}"; shift || true
QUIET=0; for a in "$@"; do [[ "$a" == "--quiet" ]] && QUIET=1; done
die() { echo "ERRO: $*" >&2; exit 1; }
say() { [[ $QUIET -eq 1 ]] || echo "$*"; }

token() {
  if [[ -n "${PRMAKE_TOKEN:-}" ]]; then printf '%s' "$PRMAKE_TOKEN"; return; fi
  [[ -f "$HOME/.claude/prmake-token.txt" ]] && { tr -d '\n' < "$HOME/.claude/prmake-token.txt"; return; }
  return 1
}

local_hash() { cat "$KB/.hash" 2>/dev/null; }

do_sync() {
  command -v curl >/dev/null && command -v jq >/dev/null && command -v unzip >/dev/null || { say "kb: curl/jq/unzip ausentes"; return 1; }
  local tk; tk="$(token)" || { say "kb: sem token do PRMake"; return 1; }
  local tmp; tmp="$(mktemp -d "${TMPDIR:-/tmp}/solvace-kb.XXXXXX")"
  local code; code="$(curl -s --max-time 20 -o "$tmp/m.json" -w '%{http_code}' -H "x-api-key: $tk" "$BASE/Architecture/export/manifest")"
  if [[ "$code" != "200" ]]; then rm -rf "$tmp"; say "kb: manifest HTTP $code (espelho mantido)"; return 1; fi
  local remote; remote="$(jq -r '.hash // empty' "$tmp/m.json")"
  if [[ -n "$remote" && "$remote" == "$(local_hash)" && -f "$KB/INDEX.md" ]]; then rm -rf "$tmp"; say "kb: espelho em dia ($remote)"; return 0; fi
  code="$(curl -s --max-time 120 -o "$tmp/kb.zip" -w '%{http_code}' -H "x-api-key: $tk" "$BASE/Architecture/export")"
  if [[ "$code" != "200" ]]; then rm -rf "$tmp"; say "kb: export HTTP $code (espelho mantido)"; return 1; fi
  mkdir -p "$tmp/new" && unzip -q "$tmp/kb.zip" -d "$tmp/new" || { rm -rf "$tmp"; say "kb: pacote invalido"; return 1; }
  printf '%s' "$remote" > "$tmp/new/.hash"
  mkdir -p "$(dirname "$KB")"
  rm -rf "$KB.old"; [[ -d "$KB" ]] && mv "$KB" "$KB.old"
  mv "$tmp/new" "$KB" && rm -rf "$KB.old" "$tmp"
  echo "Base Solvace atualizada ($remote): $(jq -r '"\(.projects) projetos, \(.sections) secoes, \(.knowledgeArticles) artigos do KC (\(.knowledgeEnvironment))"' "$KB/manifest.json")"
}

case "$CMD" in
  sync) do_sync ;;
  path) echo "$KB" ;;
  index)
    [[ -f "$KB/INDEX.md" ]] || do_sync >/dev/null || die "espelho indisponivel e sem conexao com o PRMake"
    cat "$KB/INDEX.md" ;;
  show)
    P="${1:?projeto}"; SEC="${2:-}"
    [[ -d "$KB/projects/$P" ]] || { P="$(ls "$KB/projects" 2>/dev/null | grep -i -- "$P" | head -1)"; [[ -n "$P" ]] || die "projeto '${1}' nao esta no espelho (kb.sh index)"; }
    if [[ -z "$SEC" ]]; then ls "$KB/projects/$P" | sed 's/\.md$//'; exit 0; fi
    F="$(ls "$KB/projects/$P" | grep -i -- "$SEC" | head -1)"; [[ -n "$F" ]] || die "secao '$SEC' nao encontrada em $P"
    cat "$KB/projects/$P/$F" ;;
  find)
    T="${1:?termo}"; MAX="${2:-20}"
    [[ -d "$KB" ]] || die "espelho vazio — rode kb.sh sync"
    grep -rIin --include='*.md' -- "$T" "$KB" 2>/dev/null | sed "s|$KB/||" | cut -c1-220 | head -n "$MAX" ;;
  status)
    echo "local: $(local_hash || echo nenhum) em $KB"
    tk="$(token)" && curl -s --max-time 20 -H "x-api-key: $tk" "$BASE/Architecture/export/manifest" | jq -c . ;;
  *) sed -n '2,13p' "$0"; exit 1 ;;
esac
