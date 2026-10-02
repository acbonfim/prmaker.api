#!/usr/bin/env bash
# Localiza e busca codigo nos repositorios Solvace para a analise de um bug.
#
# 0048: os repositorios vem do MAPA DA MAQUINA (~/.prmake/repos.json, montado por
# `prmake-skills.sh repos scan`): nome do repositorio pelo remote -> pasta local, em qualquer lugar da maquina.
# Variaveis valem por cima do mapa: EDV_SOLVACE_DIR (legado) e REVAMP_DIR (pasta com os modulos revamp).
# Sem mapa (ferramenta das skills antiga), cai nos caminhos padrao de antes:
#   - LEGADO: edv-solvace (modulos .NET Core + ASP Classic) em ~/repos/solvace/edv-solvace.
#   - REVAMP: cada modulo e um repositorio git proprio (micro-monolito, clean architecture) sob
#     ~/repos/solvace/revamp_separado. Building blocks compartilhados vem como pacotes NuGet
#     (Solvace.BuildingBlocks.*) do CodeArtifact — o fonte deles NAO fica aqui.
#
# Uso:
#   revamp-repos.sh list
#       lista os repositorios da maquina (tipo, nome pelo remote, branch atual, pasta).
#   revamp-repos.sh where <repo>
#       imprime a pasta do repositorio (aceita 'revamp-BOS', 'BOS', o nome da pasta). Exit 2 = nao esta no mapa,
#       3 = mais de um clone/candidato — pergunte ao usuario a pasta e fixe com: prmake-skills.sh repos set <repo> <pasta>
#   revamp-repos.sh grep <padrao> [escopo]
#       busca <padrao> no codigo. escopo: all (default) | revamp | legacy | <repo>. Saida limitada
#       (3 por arquivo, 80 linhas; env GREP_PER_FILE/GREP_MAX).
#
# Env: REVAMP_DIR, EDV_SOLVACE_DIR, PRMAKE_HOME (mapa), PRMAKE_SKILLS_TOOL (ferramenta das skills)
set -euo pipefail
# Windows/Git Bash (0035): jq sem CRLF e python3 de verdade, mesmo sem os atalhos de ~/bin no PATH.
case "$(uname -s 2>/dev/null)" in MINGW*|MSYS*|CYGWIN*)
  export PATH="$HOME/bin:$PATH" PYTHONUTF8=1
  if [[ "$(jq -rn '"x"' 2>/dev/null)" == $'x\r' ]]; then
    if [[ "$(command jq -b -rn '"x"' 2>/dev/null)" == x ]]; then jq() { command jq -b "$@"; }; else jq() { command jq "$@" | tr -d '\r'; }; fi
  fi ;;
esac

REVAMP_DIR_DEFAULT="$HOME/repos/solvace/revamp_separado"
EDV_SOLVACE_DIR_DEFAULT="$HOME/repos/solvace/edv-solvace"
REPOS_FILE="${PRMAKE_HOME:-$HOME/.prmake}/repos.json"
TOOL="${PRMAKE_SKILLS_TOOL:-$HOME/.claude/skills/.prmake/prmake-skills.sh}"

die() { echo "ERRO: $*" >&2; exit 1; }
# Mapa (0048): arquivo valido + ferramenta com o comando "repos". Sem ele, o comportamento de antes.
use_map() { [[ -s "$REPOS_FILE" && -f "$TOOL" ]] && command -v jq >/dev/null && jq -e '.version == 1' "$REPOS_FILE" >/dev/null 2>&1; }
REVAMP_DIR="${REVAMP_DIR:-$REVAMP_DIR_DEFAULT}"
EDV_SOLVACE_DIR="${EDV_SOLVACE_DIR:-$EDV_SOLVACE_DIR_DEFAULT}"
use_map || [[ -d "$REVAMP_DIR" ]] || echo "AVISO: REVAMP_DIR nao existe: $REVAMP_DIR (e sem mapa de repositorios: rode prmake-skills.sh repos scan)" >&2

# Repositorios do mapa: nome<TAB>tipo<TAB>pasta (variaveis por cima: EDV_SOLVACE_DIR substitui o edv-solvace).
map_repos() {
  jq -r '.repos | to_entries[] | [.key, (.value.kind // ""), .value.path] | @tsv' "$REPOS_FILE" |
    while IFS=$'\t' read -r name kind path; do
      if [[ -n "${EDV_SOLVACE_DIR_SET:-}" && "$(printf '%s' "$name" | tr '[:upper:]' '[:lower:]')" == edv-solvace ]]; then path="$EDV_SOLVACE_DIR_SET"; fi
      [[ -d "$path" ]] && printf '%s\t%s\t%s\n' "$name" "$kind" "$path"
    done
}
EDV_SOLVACE_DIR_SET=""; [[ "$EDV_SOLVACE_DIR" != "$EDV_SOLVACE_DIR_DEFAULT" ]] && EDV_SOLVACE_DIR_SET="$EDV_SOLVACE_DIR"
is_legacy() { [[ "$1" == legacy || "$1" == integration-api ]]; }

# lista as pastas de modulo revamp (que sao repos git)
revamp_modules() {
  [[ -d "$REVAMP_DIR" ]] || return 0
  for d in "$REVAMP_DIR"/*/; do
    [[ -d "${d}.git" ]] && basename "$d"
  done
}

# resolve o caminho de um modulo (aceita 'BOS', 'revamp-BOS', match case-insensitive)
resolve_module() {
  local q="$1" m match=() low_q
  low_q="$(printf '%s' "$q" | tr '[:upper:]' '[:lower:]')"
  for m in $(revamp_modules); do
    local low_m
    low_m="$(printf '%s' "$m" | tr '[:upper:]' '[:lower:]')"
    if [[ "$low_m" == "$low_q" || "$low_m" == "revamp-$low_q" || "$low_m" == *"$low_q"* ]]; then
      match+=("$m")
    fi
  done
  if [[ ${#match[@]} -eq 1 ]]; then printf '%s' "$REVAMP_DIR/${match[0]}"; return; fi
  if [[ ${#match[@]} -eq 0 ]]; then die "modulo revamp '$q' nao encontrado (rode: revamp-repos.sh list)"; fi
  { echo "ERRO: '$q' e ambiguo. Candidatos:"; printf '  %s\n' "${match[@]}"; } >&2; exit 1
}

# grep recursivo em <dir>, so codigo, ignorando ruido de build
do_grep() {
  local pat="$1" dir="$2" label="$3"
  [[ -d "$dir" ]] || return 0
  grep -rIn -m "${GREP_PER_FILE:-3}" --binary-files=without-match \
    --include='*.cs' --include='*.ts' --include='*.tsx' --include='*.js' --include='*.jsx' \
    --include='*.sql' --include='*.json' --include='*.cshtml' --include='*.razor' \
    --include='*.asp' --include='*.aspx' --include='*.vb' --include='*.config' --include='*.xml' \
    --exclude-dir=bin --exclude-dir=obj --exclude-dir=node_modules --exclude-dir=.git \
    --exclude-dir=dist --exclude-dir=packages --exclude-dir=.vs \
    -e "$pat" "$dir" 2>/dev/null | sed "s|^|[$label] |" || true
}

CMD="${1:-}"; shift || true
case "$CMD" in
  list)
    printf '%-16s %-26s %s\n' "TIPO" "REPOSITORIO" "BRANCH  ->  PASTA"
    if use_map; then
      map_repos | while IFS=$'\t' read -r name kind path; do
        br="$(git -C "$path" rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"
        printf '%-16s %-26s %s\n' "${kind:--}" "$name" "$br  ->  $path"
      done
      jq -r '(.ambiguous // {}) | to_entries[] | "AMBIGUO          \(.key) -> \(.value | join(" | ")) (pergunte ao usuario: prmake-skills.sh repos set \(.key) <pasta>)"' "$REPOS_FILE"
      jq -r '(.missing // []) | if length > 0 then "SEM CLONE        \(join(", "))" else empty end' "$REPOS_FILE"
    else
      if [[ -d "$EDV_SOLVACE_DIR" ]]; then
        br="$(git -C "$EDV_SOLVACE_DIR" rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"
        printf '%-16s %-26s %s\n' "legacy" "edv-solvace" "$br  ->  $EDV_SOLVACE_DIR"
      fi
      for m in $(revamp_modules); do
        br="$(git -C "$REVAMP_DIR/$m" rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"
        printf '%-16s %-26s %s\n' "revamp" "$m" "$br  ->  $REVAMP_DIR/$m"
      done
    fi
    ;;
  where)
    Q="${1:?informe o repositorio}"
    if use_map; then
      # Contrato do "repos path": 0 = pasta, 2 = nao esta no mapa, 3 = ambiguo.
      set +e; bash "$TOOL" repos path "$Q"; rc=$?; set -e
      [[ $rc -eq 0 || $rc -eq 2 || $rc -eq 3 ]] && exit $rc
    fi
    resolve_module "$Q"; echo
    ;;
  grep)
    PAT="${1:?informe o padrao}"; SCOPE="${2:-all}"
    # Saida limitada (0033): ate GREP_PER_FILE ocorrencias por arquivo, linhas cortadas e GREP_MAX linhas no total.
    exec > >(awk -v max="${GREP_MAX:-80}" 'NR<=max {print substr($0,1,220)} END {if (NR>max) print "…(" NR-max " linhas omitidas — refine o padrao ou o escopo; GREP_MAX/GREP_PER_FILE aumentam)"}')
    if use_map; then
      case "$SCOPE" in
        all|legacy|revamp)
          map_repos | while IFS=$'\t' read -r name kind path; do
            if [[ "$SCOPE" == legacy ]] && ! is_legacy "$kind"; then continue; fi
            if [[ "$SCOPE" == revamp ]] && is_legacy "$kind"; then continue; fi
            do_grep "$PAT" "$path" "$name"
          done ;;
        *)
          set +e; path="$(bash "$TOOL" repos path "$SCOPE")"; rc=$?; set -e
          [[ $rc -eq 0 ]] || exit $rc
          do_grep "$PAT" "$path" "$SCOPE" ;;
      esac
    else
      case "$SCOPE" in
        all)     do_grep "$PAT" "$EDV_SOLVACE_DIR" "legacy"
                 for m in $(revamp_modules); do do_grep "$PAT" "$REVAMP_DIR/$m" "$m"; done ;;
        legacy)  do_grep "$PAT" "$EDV_SOLVACE_DIR" "legacy" ;;
        revamp)  for m in $(revamp_modules); do do_grep "$PAT" "$REVAMP_DIR/$m" "$m"; done ;;
        *)       path="$(resolve_module "$SCOPE")"; do_grep "$PAT" "$path" "$(basename "$path")" ;;
      esac
    fi
    ;;
  ""|-h|--help|help) sed -n '2,24p' "$0" ;;
  *) die "subcomando desconhecido: '$CMD' (use: list | where | grep)" ;;
esac
