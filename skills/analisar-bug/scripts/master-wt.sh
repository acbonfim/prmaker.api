#!/usr/bin/env bash
# master-wt.sh <pasta> — a mesma pasta numa COPIA SO DE LEITURA em origin/master, atualizada agora (feature 0067).
#
# As analises e a engenharia reversa leem a regra ATUAL de producao: a master de cada repositorio, depois de um fetch.
# Nunca mexe no clone de trabalho (branch, arquivos nao commitados): usa um worktree proprio em
# <pasta-do-clone>/../.prmake-wt/master/<repo> (a mesma convencao das correcoes; a busca de repositorios ignora .prmake-wt).
# Imprime a pasta correspondente dentro da copia (o mesmo subcaminho). Sem git, sem rede ou sem origin/master: avisa no
# stderr e imprime a pasta original (nada quebra). PRMAKE_MASTER_BRANCH troca a branch (padrao: master).
set -uo pipefail
P="${1:?informe a pasta (do clone ou dentro dele)}"
[[ -e "$P" ]] || { echo "ERRO: nao existe: $P" >&2; exit 2; }
BR="${PRMAKE_MASTER_BRANCH:-master}"
D="$P"; [[ -f "$D" ]] && D="$(dirname "$D")"
TOP="$(git -C "$D" rev-parse --show-toplevel 2>/dev/null)" || { echo "AVISO: $P nao e um repositorio git — lendo como esta" >&2; printf '%s\n' "$P"; exit 0; }
case "$TOP" in */.prmake-wt/master/*) printf '%s\n' "$P"; exit 0 ;; esac   # ja e a copia da master
COMMON="$(git -C "$TOP" rev-parse --path-format=absolute --git-common-dir 2>/dev/null || git -C "$TOP" rev-parse --git-common-dir)"
MAIN="$(cd "$(dirname "$COMMON")" && pwd)"          # clone principal (mesmo se $P estiver num worktree de correcao)
[[ -d "$MAIN/.git" ]] || MAIN="$TOP"
REL="$(python3 -c 'import os,sys;r=os.path.relpath(os.path.realpath(sys.argv[1]),os.path.realpath(sys.argv[2]));print("" if r=="." else r)' "$P" "$TOP")"
WT="$(dirname "$MAIN")/.prmake-wt/master/$(basename "$MAIN")"
LOCK="$(dirname "$MAIN")/.prmake-wt/master/.$(basename "$MAIN").lock"
mkdir -p "$(dirname "$WT")"
# pasta-pai que tambem e repositorio (ex.: revamp_separado): a .prmake-wt nao aparece como arquivo novo nele
PARENT_GIT="$(git -C "$(dirname "$MAIN")" rev-parse --git-dir 2>/dev/null)"
if [[ -n "$PARENT_GIT" && -d "$PARENT_GIT/info" ]] && ! grep -qx '.prmake-wt/' "$PARENT_GIT/info/exclude" 2>/dev/null; then
  echo '.prmake-wt/' >> "$PARENT_GIT/info/exclude" 2>/dev/null || true
fi
# uma sessao por vez atualiza a copia (funcional e arquitetura rodam em paralelo)
[[ -d "$LOCK" && -n "$(find "$LOCK" -maxdepth 0 -mmin +10 2>/dev/null)" ]] && rmdir "$LOCK" 2>/dev/null   # trava esquecida
for _ in $(seq 1 120); do mkdir "$LOCK" 2>/dev/null && break; sleep 1; done
trap 'rmdir "$LOCK" 2>/dev/null' EXIT
if ! git -C "$MAIN" fetch --quiet origin "$BR" 2>/dev/null; then
  echo "AVISO: git fetch falhou em $MAIN (rede/credencial) — usando a origin/$BR que ja estava na maquina" >&2
fi
if ! git -C "$MAIN" rev-parse --verify -q "origin/$BR^{commit}" >/dev/null; then
  echo "AVISO: $MAIN nao tem origin/$BR — lendo a pasta como esta (branch $(git -C "$TOP" rev-parse --abbrev-ref HEAD 2>/dev/null))" >&2
  printf '%s\n' "$P"; exit 0
fi
TARGET="$(git -C "$MAIN" rev-parse "origin/$BR")"
if [[ ! -e "$WT/.git" ]]; then
  git -C "$MAIN" worktree prune >/dev/null 2>&1
  if ! git -C "$MAIN" worktree add --detach --quiet "$WT" "$TARGET" >/dev/null 2>&1; then
    echo "AVISO: nao consegui criar a copia da master em $WT — lendo a pasta como esta" >&2
    printf '%s\n' "$P"; exit 0
  fi
elif [[ "$(git -C "$WT" rev-parse HEAD 2>/dev/null)" != "$TARGET" ]]; then
  git -C "$WT" checkout --detach --force --quiet "$TARGET" >/dev/null 2>&1 && git -C "$WT" clean -fdq >/dev/null 2>&1 \
    || { echo "AVISO: nao consegui atualizar $WT — lendo a pasta como esta" >&2; printf '%s\n' "$P"; exit 0; }
fi
echo "master $(git -C "$WT" rev-parse --short HEAD) ($(git -C "$WT" log -1 --format=%cd --date=short)) em $WT" >&2
printf '%s\n' "$WT${REL:+/$REL}"
