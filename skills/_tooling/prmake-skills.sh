#!/usr/bin/env bash
# Skills do PRMake no Claude Code (feature 0024): instala e mantém atualizadas em ~/.claude/skills.
# Servido pela API (GET /api/v1/Skills/tool) e instalado em ~/.claude/skills/.prmake/prmake-skills.sh.
#
# Uso:
#   prmake-skills.sh install [skill...]            instala (todas, se nenhuma for informada) + hook de atualização
#   prmake-skills.sh update  [--quiet] [--force] [skill...]
#                                                  atualiza o que mudou no PRMake (--quiet: só fala se atualizou)
#   prmake-skills.sh status                        versão instalada × publicada
#
# Arquivo alterado à mão numa skill instalada NÃO é sobrescrito (só avisa); --force substitui.
# Token: env PRMAKE_TOKEN ou ~/.claude/prmake-token.txt. API: env PRMAKE_API_BASE (padrão abaixo).
set -uo pipefail

TOOL_VERSION="__PRMAKE_TOOL_VERSION__"
BASE="${PRMAKE_API_BASE:-__PRMAKE_API_BASE__}"
SKILLS_DIR="${CLAUDE_SKILLS_DIR:-$HOME/.claude/skills}"
TOOL_DIR="$SKILLS_DIR/.prmake"
SETTINGS="${CLAUDE_SETTINGS_FILE:-$HOME/.claude/settings.json}"
TOKEN_FILE="$HOME/.claude/prmake-token.txt"
MANIFEST=".prmake-skill.json"
HOOK_CMD='bash "$HOME/.claude/skills/.prmake/prmake-skills.sh" update --quiet'

QUIET=0
FORCE=0
say()  { [[ $QUIET -eq 1 ]] || echo "$*"; }
warn() { echo "AVISO: $*" >&2; }
die()  { echo "ERRO: $*" >&2; exit 1; }

TMP="$(mktemp -d "${TMPDIR:-/tmp}/prmake-skills.XXXXXX")"
trap 'rm -rf "$TMP"; rmdir "$TOOL_DIR/.lock" 2>/dev/null' EXIT

token() {
  if [[ -n "${PRMAKE_TOKEN:-}" ]]; then printf '%s' "$PRMAKE_TOKEN"; return; fi
  [[ -f "$TOKEN_FILE" ]] && { tr -d '\n' < "$TOKEN_FILE"; return; }
  return 1
}

sha256() { if command -v shasum >/dev/null; then shasum -a 256 "$1" | cut -d' ' -f1; else sha256sum "$1" | cut -d' ' -f1; fi; }

# GET autenticado; $1 = caminho (a partir de /Skills), $2 = arquivo de saída. Devolve o HTTP code.
get() {
  local tk; tk="$(token)" || { echo 000; return; }
  curl -s --max-time 60 -o "$2" -w '%{http_code}' -H "x-api-key: $tk" "$BASE/Skills$1" 2>/dev/null || echo 000
}

need() {
  local missing=()
  for c in curl jq unzip rsync; do command -v "$c" >/dev/null || missing+=("$c"); done
  [[ ${#missing[@]} -eq 0 ]] || die "instale antes: ${missing[*]}"
}

catalog() {
  local code; code="$(get "" "$TMP/catalog.json")"
  [[ "$code" == "200" ]] || return 1
  jq -e '.skills | type == "array"' "$TMP/catalog.json" >/dev/null 2>&1
}

local_version() { [[ -f "$SKILLS_DIR/$1/$MANIFEST" ]] && jq -r '.version // empty' "$SKILLS_DIR/$1/$MANIFEST"; }

# Arquivos da skill instalada que diferem do que foi instalado (edição à mão).
modified_files() {
  local dir="$SKILLS_DIR/$1" f rel expected
  [[ -f "$dir/$MANIFEST" ]] || return 0
  jq -r '.files | to_entries[] | "\(.key)\t\(.value)"' "$dir/$MANIFEST" | while IFS=$'\t' read -r rel expected; do
    f="$dir/$rel"
    if [[ ! -f "$f" ]] || [[ "$(sha256 "$f")" != "$expected" ]]; then echo "$rel"; fi
  done
}

install_one() { # <nome> <versão> <modo: install|update>
  local name="$1" version="$2" mode="$3" dir="$SKILLS_DIR/$1" changed
  if [[ "$mode" == "update" && $FORCE -eq 0 ]]; then
    changed="$(modified_files "$name")"
    if [[ -n "$changed" ]]; then
      warn "$name tem arquivos alterados à mão ($(echo "$changed" | tr '\n' ' ')) — não atualizei. Use: prmake-skills.sh update --force $name"
      return 0
    fi
  fi

  local code; code="$(get "/$name/package" "$TMP/$name.zip")"
  [[ "$code" == "200" ]] || { warn "não consegui baixar $name (HTTP $code)"; return 1; }
  rm -rf "$TMP/$name" && mkdir -p "$TMP/$name" && unzip -q -o "$TMP/$name.zip" -d "$TMP/$name" || { warn "pacote inválido: $name"; return 1; }

  mkdir -p "$dir"
  # Mantém o .venv (e o que a skill cria em execução); remove arquivos que saíram da skill.
  rsync -a --delete --exclude '.venv' --exclude "$MANIFEST" --exclude '.DS_Store' "$TMP/$name/" "$dir/"
  find "$dir/scripts" -type f \( -name '*.sh' -o -name '*.py' \) -exec chmod +x {} + 2>/dev/null

  # Manifesto: versão + hash de cada arquivo (para detectar edição à mão).
  (cd "$TMP/$name" && find . -type f ! -name "$MANIFEST" | sed 's#^\./##' | sort) > "$TMP/$name.files"
  local files_json='{}' rel
  while IFS= read -r rel; do
    files_json="$(jq --arg k "$rel" --arg v "$(sha256 "$TMP/$name/$rel")" '. + {($k): $v}' <<<"$files_json")"
  done < "$TMP/$name.files"
  jq -n --arg n "$name" --arg v "$version" --arg at "$(date -u +%Y-%m-%dT%H:%M:%SZ)" --argjson f "$files_json" \
    '{name:$n, version:$v, installedAt:$at, files:$f}' > "$dir/$MANIFEST"

  run_setup "$name"
  return 0
}

run_setup() {
  local dir="$SKILLS_DIR/$1" check runcmd
  [[ -f "$dir/skill.json" ]] || return 0
  check="$(jq -r '.setup.check // empty' "$dir/skill.json")"
  runcmd="$(jq -r '.setup.run // empty' "$dir/skill.json")"
  [[ -n "$runcmd" ]] || return 0
  if [[ -n "$check" ]] && (cd "$dir" && bash -c "$check") >/dev/null 2>&1; then return 0; fi
  say "   preparando $1 (setup)…"
  (cd "$dir" && bash -c "$runcmd") >"$TMP/setup-$1.log" 2>&1 || warn "setup de $1 falhou (veja: $runcmd)"
}

# Hook SessionStart do Claude Code: atualiza as skills a cada sessão (idempotente).
ensure_hook() {
  mkdir -p "$(dirname "$SETTINGS")"
  [[ -f "$SETTINGS" ]] || echo '{}' > "$SETTINGS"
  if ! jq -e . "$SETTINGS" >/dev/null 2>&1; then
    warn "$SETTINGS não é um JSON válido — não instalei o hook de atualização"
    return 0
  fi
  if jq -e --arg c "$HOOK_CMD" '[.hooks.SessionStart[]?.hooks[]?.command] | index($c) != null' "$SETTINGS" >/dev/null; then
    return 0
  fi
  cp "$SETTINGS" "$SETTINGS.bak-prmake"
  jq --arg c "$HOOK_CMD" '.hooks.SessionStart = ((.hooks.SessionStart // []) + [{"hooks": [{"type": "command", "command": $c, "timeout": 60}]}])' \
    "$SETTINGS" > "$TMP/settings.json" && mv "$TMP/settings.json" "$SETTINGS"
  say "   hook de atualização automática instalado em $SETTINGS (backup: settings.json.bak-prmake)"
}

# A própria ferramenta também se atualiza (vale a partir da próxima execução).
self_update() {
  local published; published="$(jq -r '.toolVersion // empty' "$TMP/catalog.json")"
  [[ -n "$published" && "$published" != "$TOOL_VERSION" ]] || return 0
  local code; code="$(get "/tool" "$TMP/tool.sh")"
  [[ "$code" == "200" ]] && bash -n "$TMP/tool.sh" 2>/dev/null && cp "$TMP/tool.sh" "$TOOL_DIR/prmake-skills.sh" && chmod +x "$TOOL_DIR/prmake-skills.sh"
}

lock() {
  mkdir -p "$TOOL_DIR"
  local i
  for i in 1 2 3 4 5 6 7 8 9 10; do mkdir "$TOOL_DIR/.lock" 2>/dev/null && return 0; sleep 1; done
  # Trava antiga (execução interrompida): assume.
  return 0
}

selected() { # imprime os nomes do catálogo filtrados pelos argumentos
  if [[ $# -eq 0 ]]; then jq -r '.skills[].name' "$TMP/catalog.json"; return; fi
  local n; for n in "$@"; do jq -e --arg n "$n" '.skills[] | select(.name == $n)' "$TMP/catalog.json" >/dev/null && echo "$n" || warn "skill desconhecida: $n"; done
}

cmd="${1:-}"; shift || true
ARGS=()
for a in "$@"; do
  case "$a" in
    --quiet) QUIET=1 ;;
    --force) FORCE=1 ;;
    *) ARGS+=("$a") ;;
  esac
done

case "$cmd" in
  install)
    need; lock
    token >/dev/null || die "sem token: rode com PRMAKE_TOKEN=<sua api-key do PRMake> ou crie $TOKEN_FILE"
    if [[ -n "${PRMAKE_TOKEN:-}" ]]; then
      if [[ ! -f "$TOKEN_FILE" ]] || [[ "$(tr -d '\n' < "$TOKEN_FILE")" != "$PRMAKE_TOKEN" ]]; then
        [[ -f "$TOKEN_FILE" ]] && cp "$TOKEN_FILE" "$TOKEN_FILE.bak"
        printf '%s' "$PRMAKE_TOKEN" > "$TOKEN_FILE" && chmod 600 "$TOKEN_FILE"
        say "   token salvo em $TOKEN_FILE"
      fi
    fi
    catalog || die "não consegui ler o catálogo de skills em $BASE (token válido? internet?)"
    mkdir -p "$TOOL_DIR"
    [[ -f "$0" && "$(cd "$(dirname "$0")" && pwd)" != "$TOOL_DIR" ]] && cp "$0" "$TOOL_DIR/prmake-skills.sh" 2>/dev/null
    chmod +x "$TOOL_DIR/prmake-skills.sh" 2>/dev/null
    for name in $(selected ${ARGS[@]+"${ARGS[@]}"}); do
      v="$(jq -r --arg n "$name" '.skills[] | select(.name == $n) | .version' "$TMP/catalog.json")"
      install_one "$name" "$v" install && say "✔ $name ($v)"
    done
    ensure_hook
    self_update
    say "Pronto. As skills estão em $SKILLS_DIR e se atualizam sozinhas a cada sessão do Claude Code."
    ;;

  update)
    command -v jq >/dev/null && command -v curl >/dev/null || exit 0
    lock
    # Sem rede/token: silencioso no modo --quiet (o hook não pode atrapalhar a sessão).
    catalog || { [[ $QUIET -eq 1 ]] && exit 0; die "não consegui ler o catálogo de skills em $BASE"; }
    updated=()
    for name in $(selected ${ARGS[@]+"${ARGS[@]}"}); do
      v="$(jq -r --arg n "$name" '.skills[] | select(.name == $n) | .version' "$TMP/catalog.json")"
      cur="$(local_version "$name")"
      [[ -d "$SKILLS_DIR/$name" && "$cur" == "$v" ]] && continue
      # Só atualiza o que já está instalado (update não instala skill nova, a menos que seja pedida pelo nome).
      [[ ! -d "$SKILLS_DIR/$name" && ${#ARGS[@]} -eq 0 ]] && continue
      install_one "$name" "$v" update && [[ "$(local_version "$name")" == "$v" ]] && updated+=("$name ${cur:-novo} → $v")
    done
    self_update
    if [[ ${#updated[@]} -gt 0 ]]; then
      # No hook SessionStart esta linha vai para o contexto do Claude: ele sabe que deve reler a SKILL.md.
      echo "Skills do PRMake atualizadas: ${updated[*]}. Releia a SKILL.md antes de usar."
    else
      say "Skills do PRMake já estão na versão publicada."
    fi
    ;;

  status)
    catalog || die "não consegui ler o catálogo de skills em $BASE"
    jq -r '.skills[] | "\(.name)\t\(.version)"' "$TMP/catalog.json" | while IFS=$'\t' read -r name v; do
      cur="$(local_version "$name")"
      mod="$(modified_files "$name" | wc -l | tr -d ' ')"
      printf '%-18s instalada: %-14s publicada: %-14s %s\n' "$name" "${cur:--}" "$v" \
        "$([[ "$cur" == "$v" ]] && echo "ok" || echo "desatualizada")$([[ "$mod" != "0" ]] && echo " ($mod arquivo(s) alterado(s) à mão)")"
    done
    echo "ferramenta: $TOOL_VERSION (publicada: $(jq -r '.toolVersion' "$TMP/catalog.json"))"
    ;;

  *) die "uso: prmake-skills.sh install|update|status [--quiet] [--force] [skill...]" ;;
esac
