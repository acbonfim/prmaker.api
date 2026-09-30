#!/usr/bin/env bash
# Localiza e busca codigo nos repositorios Solvace para a analise de um bug.
#
# Ha dois mundos de codigo:
#   - LEGADO: edv-solvace (modulos .NET Core + ASP Classic). Raiz resolvida por env
#     EDV_SOLVACE_DIR, senao ~/repos/solvace/edv-solvace.
#   - REVAMP: reescrita dos modulos legados. Cada modulo e um repositorio git proprio
#     (micro-monolito, clean architecture) sob REVAMP_DIR (default
#     ~/repos/solvace/revamp_separado). Building blocks compartilhados vem como pacotes
#     NuGet (Solvace.BuildingBlocks.*) do CodeArtifact — o fonte deles NAO fica aqui.
#
# Uso:
#   revamp-repos.sh list
#       lista o repo legado e cada modulo revamp (pasta, modulo, branch atual).
#   revamp-repos.sh where <modulo>
#       imprime o caminho do modulo revamp (aceita 'BOS' ou 'revamp-BOS').
#   revamp-repos.sh grep <padrao> [escopo]
#       busca <padrao> no codigo. escopo: all (default) | revamp | legacy | <modulo>. Saida limitada
#       (3 por arquivo, 80 linhas; env GREP_PER_FILE/GREP_MAX).
#
# Env: REVAMP_DIR, EDV_SOLVACE_DIR
set -euo pipefail

REVAMP_DIR="${REVAMP_DIR:-$HOME/repos/solvace/revamp_separado}"
EDV_SOLVACE_DIR="${EDV_SOLVACE_DIR:-$HOME/repos/solvace/edv-solvace}"

die() { echo "ERRO: $*" >&2; exit 1; }
[[ -d "$REVAMP_DIR" ]] || echo "AVISO: REVAMP_DIR nao existe: $REVAMP_DIR" >&2

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
    printf '%-14s %-22s %s\n' "TIPO" "MODULO/REPO" "BRANCH  ->  CAMINHO"
    if [[ -d "$EDV_SOLVACE_DIR" ]]; then
      br="$(git -C "$EDV_SOLVACE_DIR" rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"
      printf '%-14s %-22s %s\n' "legacy" "edv-solvace" "$br  ->  $EDV_SOLVACE_DIR"
    fi
    for m in $(revamp_modules); do
      br="$(git -C "$REVAMP_DIR/$m" rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"
      printf '%-14s %-22s %s\n' "revamp" "$m" "$br  ->  $REVAMP_DIR/$m"
    done
    ;;
  where)
    resolve_module "${1:?informe o modulo}"; echo
    ;;
  grep)
    PAT="${1:?informe o padrao}"; SCOPE="${2:-all}"
    # Saida limitada (0033): ate GREP_PER_FILE ocorrencias por arquivo, linhas cortadas e GREP_MAX linhas no total.
    exec > >(awk -v max="${GREP_MAX:-80}" 'NR<=max {print substr($0,1,220)} END {if (NR>max) print "…(" NR-max " linhas omitidas — refine o padrao ou o escopo; GREP_MAX/GREP_PER_FILE aumentam)"}')
    case "$SCOPE" in
      all)     do_grep "$PAT" "$EDV_SOLVACE_DIR" "legacy"
               for m in $(revamp_modules); do do_grep "$PAT" "$REVAMP_DIR/$m" "$m"; done ;;
      legacy)  do_grep "$PAT" "$EDV_SOLVACE_DIR" "legacy" ;;
      revamp)  for m in $(revamp_modules); do do_grep "$PAT" "$REVAMP_DIR/$m" "$m"; done ;;
      *)       path="$(resolve_module "$SCOPE")"; do_grep "$PAT" "$path" "$(basename "$path")" ;;
    esac
    ;;
  ""|-h|--help|help) sed -n '2,33p' "$0" ;;
  *) die "subcomando desconhecido: '$CMD' (use: list | where | grep)" ;;
esac
