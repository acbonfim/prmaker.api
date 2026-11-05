#!/usr/bin/env bash
# prmake-case.sh — arquivos da conversa do caso para o Especialista do caso (0080).
#
#   prmake-case.sh file   <card> <n>          baixa o arquivo #n da conversa (imagem, áudio, documento) e imprime o caminho
#   prmake-case.sh upload <card> <arquivo>    sobe um arquivo gerado (imagem do mock, script, CSV) e imprime "arquivo #n"
#   prmake-case.sh context <card>             contexto compacto do caso (o mesmo de prmake_case_context)
#   prmake-case.sh rules   <card> [--refresh] regras do PRMake para o card (o mesmo de prmake_rules), cache 10 min
#   prmake-case.sh items   <card> [--refresh] itens de trabalho do card (resumo + fingerprint), cache 10 min
#   prmake-case.sh catalog <card>             tipos de sub-tarefa e modelos de item ja preenchidos para o card
#
# Token: PRMAKE_TOKEN (no executor é o token do caso — só lê e conversa) ou ~/.claude/prmake-token.txt.
# Arquivos baixados ficam em ~/.prmake/cards/<card>/case/; regras/itens/catalogo em ~/.prmake/cards/<card>/dados/
# (regras.md, itens.json, catalogo.json). Cache: PRMAKE_CASE_TTL_MIN (padrao 10); --refresh ignora o cache; se o PRMake
# falhar, usa o cache antigo (com AVISO). Saida: 0 ok, 1 erro (sem dados), 2 uso incorreto.
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
usage() { echo "uso: $*" >&2; exit 2; }
warn() { echo "AVISO: $*" >&2; }

CMD="${1:-}"; CARD="${2:-}"
[[ -n "$CMD" && -n "$CARD" ]] || usage "prmake-case.sh file|upload|context|rules|items|catalog <card> [...]"
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
DADOS="${CARDS_DIR:-$HOME/.prmake/cards}/$CARD/dados"
mkdir -p "$DIR" "$DADOS"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/prmake-case.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT
TTL="${PRMAKE_CASE_TTL_MIN:-10}"; [[ "$TTL" =~ ^[0-9]+$ ]] || TTL=10
REFRESH=0; [[ "${3:-}" == "--refresh" ]] && REFRESH=1

# GET no PRMake: grava o corpo em $TMP/resp e devolve o HTTP (000 = sem rede/timeout).
get() { curl -s --max-time 60 -o "$TMP/resp" -w '%{http_code}' -H "x-api-key: $TOKEN" "$BASE/$1" 2>/dev/null || true; }
# Cache valido = existe, nao vazio e mais novo que TTL minutos (find -mmin: igual no macOS, Linux e Git Bash).
fresh() { [[ -s "$1" ]] && [[ -n "$(find "$1" -mmin "-$TTL" 2>/dev/null)" ]]; }
# Busca <path> e grava em <arquivo>; se falhar e houver cache antigo, usa o cache com AVISO.
fetch_or_cache() { # <path> <arquivo> <nome> <filtro-jq-que-valida>
  local path="$1" file="$2" name="$3" check="$4" code
  code="$(get "$path")"
  if [[ "$code" =~ ^2 ]] && jq -e "$check" "$TMP/resp" >/dev/null 2>&1; then
    cp "$TMP/resp" "$file"; return 0
  fi
  if [[ -s "$file" ]]; then
    warn "$name: PRMake indisponivel (HTTP ${code:-000}) — usando o cache de $(date -r "$file" '+%d/%m %H:%M' 2>/dev/null || echo 'antes') ($file)"
    return 0
  fi
  die "$name: HTTP ${code:-000} $(head -c 300 "$TMP/resp" 2>/dev/null) — use a ferramenta MCP equivalente"
}

# Resumo legivel da lista de itens (WorkItemResponse[]).
items_summary() { # <itens.json>
  jq -r --arg card "$CARD" '
    def params: ((.params // {}) | to_entries | map("\(.key)=\(.value)") | join(", ")) as $p | if $p == "" then "" else "  {\($p)}" end;
    if (type != "array") or length == 0 then "Nenhum item de trabalho no card \($card)."
    else
      "Itens de trabalho do card \($card) (\(length)):",
      (sort_by(.number)[] |
        "#\(.number) [\(.type // "bug")] \(.status) \(.progress.done // 0)/\(.progress.total // (.tasks | length)) — \(.title)" +
        "  (origem: \(.origin // "?")" + (if .planId then ", plano \(.planPhase // "")" else "" end) +
        (if .editable then ", editavel" else "" end) + (if .cancelReason then ", cancelado: \(.cancelReason)" else "" end) +
        ", fp: \((.fingerprint // "")[0:12]))",
        ((.tasks // []) | sort_by(.order)[] |
          "   \(.order). #\(.number) \(.kind) \(.status) — \(.title)\(params)"))
    end' "$1"
}

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
  rules)
    F="$DADOS/regras.md"
    if (( REFRESH )) || ! fresh "$F"; then
      code="$(get "Skills/rules?card=$CARD")"
      if [[ "$code" =~ ^2 ]] && [[ -s "$TMP/resp" ]]; then
        # Aceita markdown puro, string JSON ou objeto {markdown|content|text|rules}.
        if jq -e 'type == "string"' "$TMP/resp" >/dev/null 2>&1; then jq -r '.' "$TMP/resp" > "$TMP/md"
        elif jq -e 'type == "object"' "$TMP/resp" >/dev/null 2>&1; then
          jq -r '.markdown // .content // .text // .rules // empty | if type == "string" then . else tojson end' "$TMP/resp" > "$TMP/md"
        else cp "$TMP/resp" "$TMP/md"; fi
        [[ -s "$TMP/md" ]] || die "regras: resposta vazia do PRMake — use prmake_rules"
        { echo "<!-- regras do PRMake para o card $CARD, obtidas em $(date '+%Y-%m-%d %H:%M') -->"; cat "$TMP/md"; } > "$F"
      elif [[ -s "$F" ]]; then
        warn "regras: PRMake indisponivel (HTTP ${code:-000}) — usando o cache antigo ($F)"
      else
        die "regras: HTTP ${code:-000} $(head -c 300 "$TMP/resp" 2>/dev/null) — use prmake_rules"
      fi
    fi
    cat "$F"
    ;;
  items)
    F="$DADOS/itens.json"
    if (( REFRESH )) || ! fresh "$F"; then
      fetch_or_cache "WorkItems/card/$CARD" "$F" "itens" 'type == "array"'
    fi
    items_summary "$F"
    echo "(lista completa: $F)"
    ;;
  catalog)
    F="$DADOS/catalogo.json"
    fetch_or_cache "WorkItems/catalog?card=$CARD" "$F" "catalogo" 'type == "object"'
    jq -r '
      def params: ((.params // {}) | to_entries | map("\(.key)=\(.value)") | join(", ")) as $p | if $p == "" then "" else "  {\($p)}" end;
      "Tipos de sub-tarefa:",
      ((.taskKinds // [])[] | "  \(.kind) — \(.label // .kind)" +
        (if ((.params // []) | length) > 0 then "  (parametros: \(.params | map(if type == "string" then . else (.key // .name // tostring) end) | join(", ")))" else "" end)),
      "",
      "Modelos (ja preenchidos para o card — use os valores como estao, nao invente nomes):",
      ((.templates // [])[] |
        "  \(.key) — \(.title)" + (if .type then " [\(.type)]" else "" end) + (if .description then ": \(.description)" else "" end),
        ((.tasks // []) | to_entries[] | "     \(.key + 1). \(.value.kind) — \(.value.title)\(.value | params)"))' "$F"
    echo "(catalogo completo: $F)"
    ;;
  *) usage "comando desconhecido: $CMD (file|upload|context|rules|items|catalog)" ;;
esac
