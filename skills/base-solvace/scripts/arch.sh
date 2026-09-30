#!/usr/bin/env bash
# Publicacao da engenharia reversa no PRMake (feature 0033) — escrita so para admin (api-key de admin).
#
# Uso: arch.sh <comando> [args]
#   list                                    projetos publicados (chave, tipo, commit, secoes)
#   project <chave> --name N --kind K [--repo URL] [--summary-file F] [--keywords "a,b"] [--repo-dir D] [--order N]
#           [--relations-file R.json]
#                                           cria/atualiza o projeto; --repo-dir grava o commit/branch atual (HEAD);
#                                           --relations-file: JSON [{target, kind, detail, evidence}] (substitui as relacoes)
#   section <chave> <secao> <arquivo.md> [--title T] [--order N] [--note N] [--source skill|admin]
#                                           grava a secao (conteudo igual nao cria versao)
#   get <chave> [secao]                     metadados do projeto ou o conteudo de uma secao
#   stale <chave> <repo-dir>                o que mudou no repositorio desde o commit mapeado (para atualizar)
#   publicar-pasta <pasta-kb> [projeto...]  publica cada <pasta-kb>/<chave>/ (projeto.json com relations + NNN-secao.md);
#                                           repoDir relativo a SOLVACE_REPOS (padrao ~/repos/solvace) grava o commit
#   suggest <chave> <secao|-> <arquivo.md> [--kind learning|divergence] [--card N]
#                                           PROPOE uma melhoria (qualquer usuario): vai para a fila do admin no PRMake,
#                                           nunca grava na secao
# Tipos: ecosystem legacy frontend integration revamp infra third-party auth business-rules other
set -uo pipefail
BASE="${PRMAKE_API_BASE:-https://api.softhouse.app.br/api/v1}"
CMD="${1:-}"; shift || true
die() { echo "ERRO: $*" >&2; exit 1; }
command -v jq >/dev/null || die "jq nao encontrado"
if [[ -n "${PRMAKE_TOKEN:-}" ]]; then TK="$PRMAKE_TOKEN"; elif [[ -f "$HOME/.claude/prmake-token.txt" ]]; then TK="$(tr -d '\n' < "$HOME/.claude/prmake-token.txt")"; else die "token do PRMake nao encontrado"; fi
TMP="$(mktemp -d "${TMPDIR:-/tmp}/arch.XXXXXX")"; trap 'rm -rf "$TMP"' EXIT

api() { # <METHOD> <path> [body-file]
  local args=(-s --max-time 60 -o "$TMP/resp" -w '%{http_code}' -X "$1" "$BASE/Architecture$2" -H "x-api-key: $TK" -H 'accept: application/json')
  [[ -n "${3:-}" ]] && args+=(-H 'content-type: application/json' --data-binary "@$3")
  CODE="$(curl "${args[@]}" 2>/dev/null)"; CODE="${CODE:-000}"
}
check() { [[ "$CODE" =~ ^2 ]] || die "HTTP $CODE: $(jq -r '.error // .title // .' "$TMP/resp" 2>/dev/null | head -c 300)"; }
opt() { # <nome> <padrao> <args...>
  local name="$1" def="$2"; shift 2
  while [[ $# -gt 0 ]]; do [[ "$1" == "$name" ]] && { printf '%s' "${2:-}"; return; }; shift; done
  printf '%s' "$def"
}

case "$CMD" in
  list)
    api GET /projects; check
    jq -r '.[] | "\(.key)\t\(.kind)\t\(.sourceCommit // "-" | .[0:8])\t\(.sections | length) secoes\t\(.name)"' "$TMP/resp" | column -t -s $'\t' ;;
  project)
    KEY="${1:?chave}"; shift
    NAME="$(opt --name "" "$@")"; [[ -n "$NAME" ]] || die "--name obrigatorio"
    SUMMARY=""; SF="$(opt --summary-file "" "$@")"; [[ -n "$SF" ]] && { [[ -f "$SF" ]] || die "resumo nao encontrado: $SF"; SUMMARY="$(cat "$SF")"; }
    RD="$(opt --repo-dir "" "$@")"; COMMIT=""; BRANCH=""
    if [[ -n "$RD" ]]; then COMMIT="$(git -C "$RD" rev-parse HEAD 2>/dev/null)" || die "$RD nao e um repositorio git"; BRANCH="$(git -C "$RD" rev-parse --abbrev-ref HEAD)"; fi
    ORDER="$(opt --order "" "$@")"
    RF="$(opt --relations-file "" "$@")"; if [[ -n "$RF" ]]; then [[ -f "$RF" ]] || die "relacoes nao encontradas: $RF"; RELS="$(cat "$RF")"; else RELS="null"; fi
    jq -n --argjson rels "$RELS" --arg name "$NAME" --arg kind "$(opt --kind other "$@")" --arg repo "$(opt --repo "" "$@")" --arg summary "$SUMMARY" \
      --arg kw "$(opt --keywords "" "$@")" --arg commit "$COMMIT" --arg branch "$BRANCH" --arg order "$ORDER" \
      'def nn: if . == "" then null else . end;
       {name:$name, kind:$kind, repository:($repo|nn), summary:($summary|nn),
        keywords:($kw|split(",")|map(gsub("^\\s+|\\s+$";""))|map(select(.!=""))),
        sourceCommit:($commit|nn), sourceBranch:($branch|nn), order:($order|nn|if . == null then null else tonumber end), relations:$rels}' > "$TMP/body"
    api PUT "/projects/$KEY" "$TMP/body"; check
    jq -r '"OK projeto \(.key) (\(.kind)) — commit \(.sourceCommit // "-" | .[0:8]) · \(.sections|length) secoes · \(.relations|length) relacoes"' "$TMP/resp" ;;
  section)
    KEY="${1:?chave}"; SEC="${2:?secao}"; FILE="${3:?arquivo.md}"; shift 3
    [[ -f "$FILE" ]] || die "arquivo nao encontrado: $FILE"
    ORDER="$(opt --order "" "$@")"
    jq -n --rawfile content "$FILE" --arg title "$(opt --title "" "$@")" --arg note "$(opt --note "" "$@")" \
      --arg source "$(opt --source skill "$@")" --arg order "$ORDER" \
      'def nn: if . == "" then null else . end;
       {content:$content, title:($title|nn), note:($note|nn), source:$source, order:($order|nn|if . == null then null else tonumber end)}' > "$TMP/body"
    api PUT "/projects/$KEY/sections/$SEC" "$TMP/body"; check
    jq -r '"OK \(.key) v\(.version) (\(.length) caracteres)"' "$TMP/resp" ;;
  get)
    KEY="${1:?chave}"
    if [[ -n "${2:-}" ]]; then api GET "/projects/$KEY/sections/$2"; check; jq -r '.content' "$TMP/resp"
    else api GET "/projects/$KEY"; check; jq -r '"\(.name) (\(.key), \(.kind)) commit \(.sourceCommit // "-") em \(.sourceMappedAt // "-")\n\(.summary // "")\nsecoes: \([.sections[] | "\(.key) v\(.version)"] | join(", "))"' "$TMP/resp"; fi ;;
  stale)
    KEY="${1:?chave}"; RD="${2:?repo-dir}"
    api GET "/projects/$KEY"; check
    C="$(jq -r '.sourceCommit // empty' "$TMP/resp")"
    [[ -n "$C" ]] || { echo "projeto sem commit registrado — mapeie do zero"; exit 0; }
    git -C "$RD" cat-file -e "$C^{commit}" 2>/dev/null || { echo "commit $C nao existe no repositorio local (git fetch?) — mapeie do zero"; exit 0; }
    N="$(git -C "$RD" rev-list --count "$C..HEAD")"
    echo "$N commit(s) desde ${C:0:8}"; [[ "$N" == "0" ]] && exit 0
    git -C "$RD" diff --stat "$C..HEAD" | tail -1
    echo "pastas alteradas (as secoes que falam delas precisam de revisao):"
    git -C "$RD" diff --name-only "$C..HEAD" | awk -F/ '{print $1"/"$2}' | sort | uniq -c | sort -rn | head -25 ;;
  suggest)
    KEY="${1:?chave}"; SEC="${2:?secao (ou - para o projeto)}"; FILE="${3:?arquivo.md}"; shift 3
    [[ -f "$FILE" ]] || die "arquivo nao encontrado: $FILE"
    jq -n --arg p "$KEY" --arg s "$SEC" --rawfile c "$FILE" --arg k "$(opt --kind learning "$@")" --arg card "$(opt --card "" "$@")" \
      '{projectKey:$p, sectionKey:(if $s == "-" then null else $s end), kind:$k, content:$c, cardNumber:(if $card == "" then null else $card end)}' > "$TMP/body"
    api POST /suggestions "$TMP/body"; check
    echo "OK sugestao registrada para $KEY${SEC:+/$SEC} — o admin aplica ou descarta na tela Base Solvace" ;;
  publicar-pasta)
    KB="${1:?pasta-kb}"; shift
    REPOS="${SOLVACE_REPOS:-$HOME/repos/solvace}"
    title_of() { case "$1" in
      visao-geral) echo "Visão geral";; modulos) echo "Módulos e fluxos";; dados) echo "Dados";; integracoes) echo "Integrações";;
      infra) echo "Infra e AWS";; autenticacao) echo "Login e permissões";; jobs) echo "Jobs e rotinas";;
      regras-de-negocio) echo "Regras de negócio";; armadilhas) echo "Armadilhas e bugs conhecidos";; *) echo "$1";; esac; }
    projects=("$@"); [[ ${#projects[@]} -gt 0 ]] || projects=($(cd "$KB" && ls -d */ 2>/dev/null | tr -d / | grep -v '^_'))
    SELF="$0"
    for key in "${projects[@]}"; do
      meta="$KB/$key/projeto.json"; [[ -f "$meta" ]] || { echo "sem $meta" >&2; continue; }
      jq -r '.summary // ""' "$meta" > "$TMP/summary.md"
      jq -c '.relations // null' "$meta" > "$TMP/rels.json"
      args=(--name "$(jq -r .name "$meta")" --kind "$(jq -r .kind "$meta")" --summary-file "$TMP/summary.md"
            --keywords "$(jq -r '(.keywords // []) | join(",")' "$meta")" --order "$(jq -r '.order // 0' "$meta")")
      [[ "$(cat "$TMP/rels.json")" != "null" ]] && args+=(--relations-file "$TMP/rels.json")
      repo="$(jq -r '.repository // empty' "$meta")"; [[ -n "$repo" ]] && args+=(--repo "$repo")
      dir="$(jq -r '.repoDir // empty' "$meta")"; [[ -n "$dir" && -d "$REPOS/$dir/.git" ]] && args+=(--repo-dir "$REPOS/$dir")
      bash "$SELF" project "$key" "${args[@]}" || continue
      for f in "$KB/$key"/[0-9][0-9][0-9]-*.md; do
        [[ -f "$f" ]] || continue
        name="$(basename "$f" .md)"; order="${name%%-*}"; section="${name#*-}"
        bash "$SELF" section "$key" "$section" "$f" --title "$(title_of "$section")" --order "$((10#$order))" --note "${KB_NOTE:-engenharia reversa (base-solvace)}" >/dev/null && printf '.'
      done; echo
    done ;;
  *) sed -n '2,16p' "$0"; exit 1 ;;
esac
