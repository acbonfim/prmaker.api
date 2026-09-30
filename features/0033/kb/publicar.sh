#!/usr/bin/env bash
# Publica a engenharia reversa inicial (feature 0033) no PRMake — rodar DEPOIS do deploy, com api-key de admin.
# Uso: bash features/0033/kb/publicar.sh [projeto...]      (sem argumento: todos)
# Env: PRMAKE_API_BASE (padrao producao), PRMAKE_TOKEN, SOLVACE_REPOS (padrao ~/repos/solvace — para gravar o commit de origem)
set -euo pipefail
KB="$(cd "$(dirname "$0")" && pwd)"
ARCH="${ARCH:-$KB/../../../skills/base-solvace/scripts/arch.sh}"
REPOS="${SOLVACE_REPOS:-$HOME/repos/solvace}"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/kb-publicar.XXXXXX")"; trap 'rm -rf "$TMP"' EXIT
title_of() { case "$1" in
  visao-geral) echo "Visão geral";; modulos) echo "Módulos e fluxos";; dados) echo "Dados";; integracoes) echo "Integrações";;
  infra) echo "Infra e AWS";; autenticacao) echo "Login e permissões";; jobs) echo "Jobs e rotinas";;
  regras-de-negocio) echo "Regras de negócio";; armadilhas) echo "Armadilhas e bugs conhecidos";; *) echo "$1";; esac; }
projects=("$@"); [[ ${#projects[@]} -gt 0 ]] || projects=($(cd "$KB" && ls -d */ | tr -d /))
for key in "${projects[@]}"; do
  meta="$KB/$key/projeto.json"; [[ -f "$meta" ]] || { echo "sem $meta" >&2; continue; }
  jq -r '.summary // ""' "$meta" > "$TMP/summary.md"
  args=(--name "$(jq -r .name "$meta")" --kind "$(jq -r .kind "$meta")" --summary-file "$TMP/summary.md"
        --keywords "$(jq -r '(.keywords // []) | join(",")' "$meta")" --order "$(jq -r '.order // 0' "$meta")")
  repo="$(jq -r '.repository // empty' "$meta")"; [[ -n "$repo" ]] && args+=(--repo "$repo")
  dir="$(jq -r '.repoDir // empty' "$meta")"; [[ -n "$dir" && -d "$REPOS/$dir/.git" ]] && args+=(--repo-dir "$REPOS/$dir")
  bash "$ARCH" project "$key" "${args[@]}"
  for f in "$KB/$key"/[0-9][0-9][0-9]-*.md; do
    name="$(basename "$f" .md)"; order="${name%%-*}"; section="${name#*-}"
    bash "$ARCH" section "$key" "$section" "$f" --title "$(title_of "$section")" --order "$((10#$order))" --note "engenharia reversa inicial (0033)"
  done
done
echo "Pronto. Confira: bash ~/.claude/skills/base-solvace/scripts/kb.sh sync && kb.sh index"
