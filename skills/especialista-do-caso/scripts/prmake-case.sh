#!/usr/bin/env bash
# prmake-case.sh — arquivos da conversa do caso para o Especialista do caso (0080).
#
#   prmake-case.sh file   <card> <n>          baixa o arquivo #n da conversa (imagem, áudio, documento) e imprime o caminho
#   prmake-case.sh upload <card> <arquivo>    sobe um arquivo gerado (imagem do mock, script, CSV) e imprime "arquivo #n"
#   prmake-case.sh context <card>             contexto compacto do caso (o mesmo de prmake_case_context)
#
# Token: PRMAKE_TOKEN (no executor é o token do caso — só lê e conversa) ou ~/.claude/prmake-token.txt.
# Arquivos baixados ficam em ~/.prmake/cards/<card>/case/.
set -uo pipefail
# Windows/Git Bash (0035): jq sem CRLF e python3 de verdade, mesmo sem os atalhos de ~/bin no PATH.
case "$(uname -s 2>/dev/null)" in MINGW*|MSYS*|CYGWIN*)
  export PATH="$HOME/bin:$PATH" PYTHONUTF8=1
  if [[ "$(jq -rn '"x"' 2>/dev/null)" == $'x\r' ]]; then
    if [[ "$(command jq -b -rn '"x"' 2>/dev/null)" == x ]]; then jq() { command jq -b "$@"; }; else jq() { command jq "$@" | tr -d '\r'; }; fi
  fi
  if ! python3 -c '' >/dev/null 2>&1; then
    if py -3 -c '' >/dev/null 2>&1; then python3() { py -3 "$@"; }; elif python -c '' >/dev/null 2>&1; then python3() { python "$@"; }; fi
  fi ;;
esac

die() { echo "ERRO: $*" >&2; exit 1; }

CMD="${1:-}"; CARD="${2:-}"
[[ -n "$CMD" && -n "$CARD" ]] || die "uso: prmake-case.sh file|upload|context <card> [...]"
command -v jq >/dev/null || die "jq nao encontrado"
BASE="${PRMAKE_API_BASE:-https://api.softhouse.app.br/api/v1}"
CARD="${CARD#\#}"

token() {
  if [[ -n "${PRMAKE_TOKEN:-}" ]]; then printf '%s' "$PRMAKE_TOKEN"; return; fi
  [[ -f "$HOME/.claude/prmake-token.txt" ]] && { tr -d '\r\n' < "$HOME/.claude/prmake-token.txt"; return; }
  die "token nao encontrado (PRMAKE_TOKEN ou ~/.claude/prmake-token.txt)"
}
TOKEN="$(token)"
DIR="${CARDS_DIR:-$HOME/.prmake/cards}/$CARD/case"
mkdir -p "$DIR"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/prmake-case.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT

case "$CMD" in
  file)
    N="${3:-}"; N="${N#\#}"
    [[ "$N" =~ ^[0-9]+$ ]] || die "uso: prmake-case.sh file <card> <n>"
    code="$(curl -s --max-time 120 -o "$TMP/body" -D "$TMP/headers" -w '%{http_code}' -H "x-api-key: $TOKEN" \
      "$BASE/CaseChat/$CARD/files/number/$N/content")"
    [[ "$code" == 200 ]] || die "arquivo #$N: HTTP $code $(head -c 300 "$TMP/body" 2>/dev/null)"
    name="$(grep -i '^x-case-file-name:' "$TMP/headers" | head -1 | cut -d: -f2- | tr -d '\r' | sed 's/^ *//')"
    name="$(printf '%b' "${name//%/\\x}")"
    [[ -n "$name" ]] || name="arquivo-$N"
    name="${name//\//_}"
    out="$DIR/$(printf '%03d' "$N")-$name"
    mv "$TMP/body" "$out"
    echo "$out"
    ;;
  upload)
    F="${3:-}"
    [[ -f "$F" ]] || die "uso: prmake-case.sh upload <card> <arquivo>"
    code="$(curl -s --max-time 120 -o "$TMP/resp" -w '%{http_code}' -H "x-api-key: $TOKEN" -F "file=@$F" "$BASE/CaseChat/$CARD/files")"
    [[ "$code" == 200 ]] || die "upload: HTTP $code $(head -c 300 "$TMP/resp" 2>/dev/null)"
    jq -r '"arquivo #\(.number) \(.name)"' "$TMP/resp"
    ;;
  context)
    code="$(curl -s --max-time 60 -o "$TMP/resp" -w '%{http_code}' -H "x-api-key: $TOKEN" "$BASE/CaseChat/$CARD/context")"
    [[ "$code" == 200 ]] || die "contexto: HTTP $code $(head -c 300 "$TMP/resp" 2>/dev/null)"
    cat "$TMP/resp"
    ;;
  *) die "comando desconhecido: $CMD (file|upload|context)" ;;
esac
