#!/usr/bin/env bash
# Skills do PRMake no Claude Code (feature 0024): instala e mantém atualizadas em ~/.claude/skills.
# Servido pela API (GET /api/v1/Skills/tool) e instalado em ~/.claude/skills/.prmake/prmake-skills.sh.
#
# Uso:
#   prmake-skills.sh install [skill...]            instala (todas, se nenhuma for informada) + hook de atualização
#   prmake-skills.sh update  [--quiet] [--force] [skill...]
#                                                  atualiza o que mudou no PRMake (--quiet: só fala se atualizou)
#   prmake-skills.sh status                        versão instalada × publicada
#   prmake-skills.sh doctor                        diagnóstico da máquina (comandos, jq/python3, token, hook, permissões)
#   prmake-skills.sh permissions                   (re)libera no Claude Code as regras dos comandos das skills
#   prmake-skills.sh db-credentials [list]         cadastra (no SEU terminal) as credenciais de leitura dos bancos
#   prmake-skills.sh mcp [remove]                  registra no Claude Code o MCP remoto do PRMake (0039)
#   prmake-skills.sh agent [install|update|status|uninstall]
#                                                  executor do PRMake (0039): roda a analise pela tela, sem terminal
#   prmake-skills.sh repos [scan [--quiet] | set <repo> <pasta> | unset <repo> | path <repo>]
#                                                  repositórios desta máquina (0048): mapa remote → pasta em ~/.prmake/repos.json
#
# Arquivo alterado à mão numa skill instalada NÃO é sobrescrito (só avisa); --force substitui.
# Token: env PRMAKE_TOKEN ou ~/.claude/prmake-token.txt. API: env PRMAKE_API_BASE (padrão abaixo).
# Windows (0035): roda no Git Bash (o mesmo shell do Claude Code); rsync/unzip são opcionais e os atalhos
# python3/jq ficam em ~/bin.
set -uo pipefail

case "$(uname -s 2>/dev/null)" in MINGW*|MSYS*|CYGWIN*) WINDOWS=1 ;; *) WINDOWS=0 ;; esac
if [[ $WINDOWS -eq 1 ]]; then export PATH="$HOME/bin:$PATH" PYTHONUTF8=1; else export PATH="$PATH:$HOME/.local/bin"; fi
if [[ $WINDOWS -eq 1 && "$(jq -rn '"x"' 2>/dev/null)" == $'x\r' ]]; then
  if [[ "$(command jq -b -rn '"x"' 2>/dev/null)" == x ]]; then jq() { command jq -b "$@"; }; else jq() { command jq "$@" | tr -d '\r'; }; fi
fi

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
LOCKED=0
# Só quem criou a trava a remove (0048: o repos scan em segundo plano não pode soltar a trava de outro update).
trap 'rm -rf "$TMP"; [[ $LOCKED -eq 1 ]] && rmdir "$TOOL_DIR/.lock" 2>/dev/null' EXIT

token() {
  if [[ -n "${PRMAKE_TOKEN:-}" ]]; then printf '%s' "$PRMAKE_TOKEN"; return; fi
  [[ -f "$TOKEN_FILE" ]] && { tr -d '\r\n' < "$TOKEN_FILE"; return; }
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
  for c in curl jq; do command -v "$c" >/dev/null || missing+=("$c"); done
  [[ ${#missing[@]} -eq 0 ]] || die "instale antes: ${missing[*]} (ou rode o instalador da tela Skills do PRMake, que instala as dependências)"
}

# Descompacta <zip> em <pasta>: unzip; senão bsdtar (tar do macOS e do Windows 10+) ou o zipfile do Python.
extract() {
  if command -v unzip >/dev/null; then unzip -q -o "$1" -d "$2"; return; fi
  local bsdtar=""
  if [[ $WINDOWS -eq 1 && -x "${SYSTEMROOT:-/c/Windows}/System32/tar.exe" ]]; then bsdtar="${SYSTEMROOT:-/c/Windows}/System32/tar.exe"
  elif tar --version 2>/dev/null | grep -q bsdtar; then bsdtar=tar; fi
  if [[ -n "$bsdtar" ]]; then (cd "$2" && "$bsdtar" -xf "$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"); return; fi
  command -v python3 >/dev/null && python3 -m zipfile -e "$1" "$2" && return
  warn "não consigo descompactar: instale o unzip (ou o python3) e rode de novo"
  return 1
}

# Copia <origem>/ para <destino>/ removendo o que saiu da skill; mantém o .venv e o manifesto. rsync se houver.
sync_tree() {
  local src="$1" dst="$2" rel
  if command -v rsync >/dev/null; then
    rsync -a --delete --exclude '.venv' --exclude "$MANIFEST" --exclude '.DS_Store' "$src/" "$dst/"
    return
  fi
  (cd "$dst" && find . -path ./.venv -prune -o -type f ! -name "$MANIFEST" ! -name .DS_Store -print) | sed 's#^\./##' |
    while IFS= read -r rel; do [[ -e "$src/$rel" ]] || rm -f "$dst/$rel"; done
  find "$dst" -mindepth 1 -depth -type d -empty ! -path "$dst/.venv*" -exec rmdir {} + 2>/dev/null
  cp -R "$src/." "$dst/"
}

# Windows (Git Bash): o "python3" da Microsoft Store não roda e o jq.exe escreve CRLF (quebra os scripts).
# Cria atalhos em ~/bin que resolvem os dois; no ~/.bashrc, ~/bin no PATH e Python em UTF-8.
windows_prepare() {
  [[ $WINDOWS -eq 1 ]] || return 0
  mkdir -p "$HOME/bin"
  local real=""
  if ! python3 -c 'import sys; sys.exit(0 if sys.version_info[0] == 3 else 1)' >/dev/null 2>&1; then
    if py -3 -c 'import sys' >/dev/null 2>&1; then real='py -3'
    elif python -c 'import sys; sys.exit(0 if sys.version_info[0] == 3 else 1)' >/dev/null 2>&1; then real='python'; fi
    if [[ -n "$real" ]]; then
      printf '#!/usr/bin/env bash\n# Skills do PRMake: python3 no Git Bash\nexport PYTHONUTF8=1\nexec %s "$@"\n' "$real" > "$HOME/bin/python3"
      chmod +x "$HOME/bin/python3"
      say "   atalho python3 → $real criado em ~/bin"
    else
      warn "Python 3 não encontrado — no PowerShell: winget install -e --id Python.Python.3.12 (algumas skills precisam dele)"
    fi
  fi
  if command -v jq >/dev/null && [[ "$(jq -rn '"x"' 2>/dev/null)" == $'x\r' ]]; then
    real="$(type -P jq.exe || type -P jq)"  # o .exe: ~/bin/jq (o atalho) não pode apontar para si mesmo
    printf '#!/usr/bin/env bash\n# Skills do PRMake: jq sem CRLF no Git Bash\nexec "%s" -b "$@"\n' "$real" > "$HOME/bin/jq"
    chmod +x "$HOME/bin/jq"
    if [[ "$(jq -rn '"x"' 2>/dev/null)" == "x" ]]; then say "   atalho jq (sem CRLF) criado em ~/bin"; else rm -f "$HOME/bin/jq"; warn "o jq escreve CRLF — atualize: winget install -e --id jqlang.jq"; fi
  fi
  local rc="$HOME/.bashrc" line
  for line in 'export PATH="$HOME/bin:$PATH"' 'export PYTHONUTF8=1'; do
    grep -qsF "$line" "$rc" || printf '%s # Skills do PRMake\n' "$line" >> "$rc"
  done
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
  rm -rf "$TMP/$name" && mkdir -p "$TMP/$name" && extract "$TMP/$name.zip" "$TMP/$name" && [[ -f "$TMP/$name/SKILL.md" ]] || { warn "pacote inválido: $name"; return 1; }

  mkdir -p "$dir"
  # Mantém o .venv (e o que a skill cria em execução); remove arquivos que saíram da skill.
  sync_tree "$TMP/$name" "$dir"
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
  install_depends "$name"
  return 0
}

# Dependências declaradas no skill.json ("depends": ["base-solvace"]) — instaladas quando faltam (0033).
install_depends() {
  local dep v
  for dep in $(jq -r '.depends[]? // empty' "$SKILLS_DIR/$1/skill.json" 2>/dev/null); do
    [[ -f "$SKILLS_DIR/$dep/$MANIFEST" || -f "$SKILLS_DIR/$dep/SKILL.md" ]] && continue
    v="$(jq -r --arg n "$dep" '.skills[] | select(.name == $n) | .version' "$TMP/catalog.json")"
    [[ -n "$v" ]] || { warn "dependência $dep de $1 não está publicada"; continue; }
    install_one "$dep" "$v" install && say "✔ $dep ($v) — dependência de $1"
  done
}

# Espelho local da Base Solvace (0033): atualiza só quando o hash muda; silencioso sem rede.
sync_kb() {
  local kb="$SKILLS_DIR/base-solvace/scripts/kb.sh"
  [[ -f "$kb" ]] || return 0
  # No hook de sessao (--quiet): Knowledge Center -> PRMake (so com credencial local) e PRMake -> espelho em segundo
  # plano, no maximo a cada 30 min — nao atrasa a abertura da sessao. Log: ~/.claude/solvace-kb-sync.log
  if [[ $QUIET -eq 1 ]]; then (nohup bash "$kb" agendar run >/dev/null 2>&1 &) ; else bash "$kb" sync || true; fi
}

run_setup() {
  local dir="$SKILLS_DIR/$1" check runcmd
  [[ -f "$dir/skill.json" ]] || return 0
  # No Windows o venv usa .venv/Scripts/python.exe: skill.json pode trazer setup.windows (check/run).
  local key='.setup'; [[ $WINDOWS -eq 1 ]] && key='(.setup.windows // .setup)'
  check="$(jq -r "$key.check // empty" "$dir/skill.json")"
  runcmd="$(jq -r "$key.run // empty" "$dir/skill.json")"
  [[ -n "$runcmd" ]] || return 0
  if [[ -n "$check" ]] && (cd "$dir" && bash -c "$check") >/dev/null 2>&1; then return 0; fi
  say "   preparando $1 (setup)…"
  (cd "$dir" && bash -c "$runcmd") >"$TMP/setup-$1.log" 2>&1 || warn "setup de $1 falhou (veja: $runcmd)"
}

# Windows: o hook roda no shell padrão dos hooks do Claude Code (Git Bash; "shell": "powershell" nem sempre é
# respeitado). Caminhos absolutos entre aspas duplas funcionam no Git Bash e no cmd — e não caem no bash do WSL.
if [[ $WINDOWS -eq 1 ]] && command -v cygpath >/dev/null; then
  GIT_BASH="$(cygpath -m /)/bin/bash.exe"
  [[ -f "$GIT_BASH" ]] && HOOK_CMD="\"$GIT_BASH\" \"$(cygpath -m "$TOOL_DIR/prmake-skills.sh")\" update --quiet"
fi

# Hook SessionStart do Claude Code: atualiza as skills a cada sessão (idempotente).
ensure_hook() {
  mkdir -p "$(dirname "$SETTINGS")"
  [[ -f "$SETTINGS" ]] || echo '{}' > "$SETTINGS"
  if ! jq -e . "$SETTINGS" >/dev/null 2>&1; then
    warn "$SETTINGS não é um JSON válido — não instalei o hook de atualização"
    return 0
  fi
  # Já está certo: exatamente um hook nosso, com o comando atual e sem "shell" (o de PowerShell da 0035 não rodava).
  if jq -e --arg c "$HOOK_CMD" '[.hooks.SessionStart[]?.hooks[]? | select((.command // "") | contains("prmake-skills.sh"))]
      | length == 1 and .[0].command == $c and .[0].shell == null' "$SETTINGS" >/dev/null; then
    return 0
  fi
  cp "$SETTINGS" "$SETTINGS.bak-prmake"
  jq --arg c "$HOOK_CMD" '
    .hooks.SessionStart = ([(.hooks.SessionStart // [])[]
        | .hooks = [(.hooks // [])[] | select(((.command // "") | contains("prmake-skills.sh")) | not)]
        | select((.hooks | length) > 0)]
      + [{"hooks": [{"type": "command", "command": $c, "timeout": 60}]}])' \
    "$SETTINGS" > "$TMP/settings.json" && mv "$TMP/settings.json" "$SETTINGS"
  say "   hook de atualização automática instalado em $SETTINGS (backup: settings.json.bak-prmake)"
}

# Permissões do Claude Code para os comandos das skills que não podem ficar presos no prompt/auto mode:
# - devops-v1: fechamento pelo PRMake (`prmake-plan.sh devops ...`: root cause, resumo, classificação, mover o card);
# - readonly-v2 (0037): consultas somente leitura da análise (`sql-query.sh`, `cognito-query.sh`) — o auto mode barrava
#   a leitura do banco de produção e a análise ficava parada (card 74669). O próprio script só aceita SELECT/WITH e
#   sempre faz ROLLBACK.
# Cada grupo é adicionado uma única vez (marcador em $TOOL_DIR): se o usuário remover a regra, não volta sozinha —
# `prmake-skills.sh permissions` reaplica. PRMAKE_SKIP_PERMISSIONS=1 não mexe nas permissões.
PERMISSION_GROUPS=(
  "devops-v1|analisar-bug/scripts/prmake-plan.sh devops:*|fechamento pelo PRMake (prmake-plan.sh devops)"
  "readonly-v2|analisar-bug/scripts/sql-query.sh:*;analisar-bug/scripts/cognito-query.sh:*|consultas somente leitura (sql-query.sh, cognito-query.sh)"
)
# Regras (JSON) de uma lista de scripts separados por ';' — nas formas de caminho que o Claude usa (~, $HOME, absoluto).
permission_rules() {
  local s out=() list
  IFS=';' read -r -a list <<< "$1"
  for s in "${list[@]}"; do
    if [[ "$SKILLS_DIR" == "$HOME/.claude/skills" ]]; then
      out+=("Bash(bash ~/.claude/skills/$s)" "Bash(bash \$HOME/.claude/skills/$s)")
    fi
    out+=("Bash(bash $SKILLS_DIR/$s)")
  done
  printf '%s\n' "${out[@]}" | jq -R . | jq -s -c .
}
ensure_permissions() { # [force=0]
  [[ "${PRMAKE_SKIP_PERMISSIONS:-0}" == "1" ]] && return 0
  [[ -d "$SKILLS_DIR/analisar-bug" ]] || return 0
  local force="${1:-0}" g id scripts label mark rules
  for g in "${PERMISSION_GROUPS[@]}"; do
    IFS='|' read -r id scripts label <<< "$g"
    mark="$TOOL_DIR/.permissions-$id"
    [[ "$force" != "1" && -f "$mark" ]] && continue
    mkdir -p "$(dirname "$SETTINGS")"
    [[ -f "$SETTINGS" ]] || echo '{}' > "$SETTINGS"
    if ! jq -e . "$SETTINGS" >/dev/null 2>&1; then
      warn "$SETTINGS não é um JSON válido — não liberei: $label"
      return 0
    fi
    rules="$(permission_rules "$scripts")"
    cp "$SETTINGS" "$SETTINGS.bak-prmake"
    jq --argjson r "$rules" '.permissions.allow = (((.permissions.allow // []) + $r) | unique)' \
      "$SETTINGS" > "$TMP/settings.json" && mv "$TMP/settings.json" "$SETTINGS" || continue
    mkdir -p "$TOOL_DIR"; : > "$mark"
    say "   permissão do Claude Code liberada em $SETTINGS: $label"
  done
  ensure_cards_access "$force"
}
# 0046: pasta dos cards fora de ~/.claude (o Claude Code protege ~/.claude e nega gravar nela, mesmo com regra
# Write(~/.claude/cards/**) — scripts e análises do card não eram salvos). Libera ~/.prmake/cards como diretório de
# trabalho do Claude Code e a edição dos arquivos ali, para o Claude gravar sem prompt nem bloqueio do auto mode.
CARDS_ROOT_DEFAULT="$HOME/.prmake/cards"
ensure_cards_access() { # [force=0]
  local force="${1:-0}" mark="$TOOL_DIR/.permissions-cards-v1" root="${CARDS_DIR:-$CARDS_ROOT_DEFAULT}"
  [[ "$force" != "1" && -f "$mark" ]] && return 0
  mkdir -p "$root" "$(dirname "$SETTINGS")"
  [[ -f "$SETTINGS" ]] || echo '{}' > "$SETTINGS"
  jq -e . "$SETTINGS" >/dev/null 2>&1 || { warn "$SETTINGS não é um JSON válido — não liberei a pasta dos cards"; return 0; }
  cp "$SETTINGS" "$SETTINGS.bak-prmake"
  # Caminho como o Claude Code entende: no Windows (Git Bash) C:/...; regra com ~/ (home) ou // (absoluto) — "/x" seria
  # relativo à pasta do settings.json.
  local dir="$root" rule
  command -v cygpath >/dev/null 2>&1 && dir="$(cygpath -m "$root")"
  if [[ "$root" == "$HOME"/* ]]; then rule="Edit(~/${root#"$HOME"/}/**)"; else rule="Edit(/$dir/**)"; fi
  jq --arg d "$dir" --arg r "$rule" \
    '.permissions.additionalDirectories = (((.permissions.additionalDirectories // []) + [$d]) | unique)
     | .permissions.allow = (((.permissions.allow // []) + [$r]) | unique)' \
    "$SETTINGS" > "$TMP/settings.json" && mv "$TMP/settings.json" "$SETTINGS" || return 0
  mkdir -p "$TOOL_DIR"; : > "$mark"
  say "   pasta dos cards liberada no Claude Code: $root"
}
# Diagnóstico: cada script das regras tem ao menos uma forma de caminho liberada?
permissions_report() {
  local g id scripts label s list missing
  for g in "${PERMISSION_GROUPS[@]}"; do
    IFS='|' read -r id scripts label <<< "$g"
    IFS=';' read -r -a list <<< "$scripts"
    missing=()
    for s in "${list[@]}"; do
      jq -e --arg s "skills/$s)" '[.permissions.allow[]? | select(endswith($s))] | length > 0' "$SETTINGS" >/dev/null 2>&1 \
        || missing+=("${s##*/}")
    done
    if [[ ${#missing[@]} -eq 0 ]]; then
      printf '  %-28s %s\n' "permissão $id" "ok — $label"
    else
      printf '  %-28s %s\n' "permissão $id" "NÃO liberada (${missing[*]}) — rode: bash ~/.claude/skills/.prmake/prmake-skills.sh permissions"
    fi
  done
  local root="${CARDS_DIR:-$CARDS_ROOT_DEFAULT}"
  command -v cygpath >/dev/null 2>&1 && root="$(cygpath -m "$root")"
  if jq -e --arg d "$root" '(.permissions.additionalDirectories // []) | index($d) != null' "$SETTINGS" >/dev/null 2>&1; then
    printf '  %-28s %s\n' "pasta dos cards" "ok — $root"
  else
    printf '  %-28s %s\n' "pasta dos cards" "NÃO liberada ($root) — rode: bash ~/.claude/skills/.prmake/prmake-skills.sh permissions"
  fi
}

# Credenciais de leitura dos bancos dos clientes (0037), usadas pelo sql-query.sh da analisar-bug. Ficam só na
# máquina do usuário, com acesso restrito a ele; a senha é digitada sem eco e nunca passa pelo chat do Claude nem pelo PRMake.
CREDS_FILE="${SQLSERVER_CREDENTIALS:-$HOME/.claude/sqlserver-credentials.json}"
file_mode() { stat -f '%Lp' "$1" 2>/dev/null || stat -c '%a' "$1" 2>/dev/null; }
creds_report() { # linha do doctor
  if [[ ! -s "$CREDS_FILE" ]]; then
    printf '  %-28s %s\n' "credenciais de banco" "NÃO — o Claude não lê o banco dos clientes. No seu terminal: bash ~/.claude/skills/.prmake/prmake-skills.sh db-credentials"
    return
  fi
  local aliases mode
  aliases="$(jq -r '[(.servers // [])[] | .alias // "?"] | join(", ")' "$CREDS_FILE" 2>/dev/null)" || aliases="arquivo inválido"
  mode="$(file_mode "$CREDS_FILE")"
  printf '  %-28s %s\n' "credenciais de banco" "${aliases:-nenhum servidor} ($CREDS_FILE$([[ $WINDOWS -eq 0 && -n "$mode" && "$mode" != "600" ]] && echo " — permissão $mode: rode chmod 600"))"
}
db_credentials() {
  if [[ "${1:-}" == "list" ]]; then
    [[ -s "$CREDS_FILE" ]] || { echo "Nenhuma credencial de banco nesta máquina ($CREDS_FILE)."; return 0; }
    echo "Servidores com credencial em $CREDS_FILE (senhas não são mostradas):"
    jq -r '(.servers // [])[] | "  \(.alias // "?")\t\((.hosts // []) | join(", "))\tusuário: \(.user // "?")"' "$CREDS_FILE"
    return 0
  fi
  # A senha não pode passar pela conversa: sem terminal interativo (ex.: rodado pelo Claude) recusa.
  [[ -t 0 && -t 1 ]] || die "rode no SEU terminal, fora do chat do Claude: bash ~/.claude/skills/.prmake/prmake-skills.sh db-credentials"
  command -v jq >/dev/null || die "precisa do jq"
  echo "Credenciais de LEITURA dos bancos dos clientes — gravadas só nesta máquina em $CREDS_FILE (acesso só seu)."
  echo "Use as credenciais recebidas pelo canal oficial (gestor/infra, cofre de senhas), de preferência um usuário somente leitura."
  echo "Nunca cole a senha no chat do Claude, no PRMake ou no Teams. Ctrl+C cancela sem gravar."
  local alias hosts user pass more test mode
  mkdir -p "$(dirname "$CREDS_FILE")"
  if [[ ! -s "$CREDS_FILE" ]]; then
    ( umask 077; echo '{"port":1433,"servers":[]}' > "$CREDS_FILE" )
  elif ! jq -e . "$CREDS_FILE" >/dev/null 2>&1; then
    die "$CREDS_FILE não é um JSON válido — corrija ou renomeie o arquivo (não vou sobrescrever)"
  fi
  while :; do
    echo
    db_credentials list | sed -n '2,$p'
    read -rp "Nome curto do servidor (alias, ex.: prod, prod1, prod3, prod4): " alias
    [[ -n "$alias" ]] || { echo "alias obrigatório"; continue; }
    read -rp "Host (hostname completo; mais de um separado por vírgula): " hosts
    read -rp "Usuário: " user
    read -rsp "Senha (não aparece): " pass; echo
    [[ -n "$hosts" && -n "$user" && -n "$pass" ]] || { echo "host, usuário e senha são obrigatórios — nada gravado"; continue; }
    ( umask 077
      # A senha vai por variável de ambiente (não aparece na lista de processos).
      DBC_ALIAS="$alias" DBC_HOSTS="$hosts" DBC_USER="$user" DBC_PASS="$pass" jq '
        .port = (.port // 1433)
        | .servers = ([(.servers // [])[] | select(.alias != env.DBC_ALIAS)]
            + [{alias: env.DBC_ALIAS, hosts: (env.DBC_HOSTS | split(",") | map(gsub("^\\s+|\\s+$"; "")) | map(select(length > 0))),
                user: env.DBC_USER, password: env.DBC_PASS}])' "$CREDS_FILE" > "$CREDS_FILE.tmp" ) \
      && mv "$CREDS_FILE.tmp" "$CREDS_FILE" || { rm -f "$CREDS_FILE.tmp"; die "não consegui gravar $CREDS_FILE"; }
    pass=""
    chmod 600 "$CREDS_FILE" 2>/dev/null
    echo "✔ $alias gravado em $CREDS_FILE"
    if [[ -x "$SKILLS_DIR/analisar-bug/scripts/sql-query.sh" || -f "$SKILLS_DIR/analisar-bug/scripts/sql-query.sh" ]]; then
      read -rp "Testar a conexão agora (precisa da VPN)? [S/n] " test
      [[ "$test" =~ ^[nN] ]] || bash "$SKILLS_DIR/analisar-bug/scripts/sql-query.sh" --host "$alias" --ping
    fi
    read -rp "Cadastrar outro servidor? [s/N] " more
    [[ "$more" =~ ^[sS] ]] || break
  done
  echo "Pronto. Na próxima análise o Claude já consegue ler esses bancos (somente leitura)."
}

# A própria ferramenta também se atualiza (vale a partir da próxima execução).
self_update() {
  local published; published="$(jq -r '.toolVersion // empty' "$TMP/catalog.json")"
  [[ -n "$published" && "$published" != "$TOOL_VERSION" ]] || return 0
  local code; code="$(get "/tool" "$TMP/tool.sh")"
  [[ "$code" == "200" ]] && bash -n "$TMP/tool.sh" 2>/dev/null || return 0
  # Troca atômica (0033): grava ao lado e renomeia — o bash que está rodando continua lendo o arquivo antigo (outro
  # inode). Sobrescrever no lugar fazia a execução atual ler o arquivo novo do meio e dar erro de sintaxe.
  # Windows/Git Bash pode recusar renomear por cima de um script aberto: ai copia no lugar (como antes).
  cp "$TMP/tool.sh" "$TOOL_DIR/.prmake-skills.sh.new" && chmod +x "$TOOL_DIR/.prmake-skills.sh.new" \
    && { mv -f "$TOOL_DIR/.prmake-skills.sh.new" "$TOOL_DIR/prmake-skills.sh" 2>/dev/null \
         || { cp "$TMP/tool.sh" "$TOOL_DIR/prmake-skills.sh" && chmod +x "$TOOL_DIR/prmake-skills.sh"; rm -f "$TOOL_DIR/.prmake-skills.sh.new"; }; }
}

lock() {
  mkdir -p "$TOOL_DIR"
  local i
  for i in 1 2 3 4 5 6 7 8 9 10; do mkdir "$TOOL_DIR/.lock" 2>/dev/null && { LOCKED=1; return 0; }; sleep 1; done
  # Trava antiga (execução interrompida): assume.
  LOCKED=1
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

# MCP remoto do PRMake (0039): ferramentas do plano direto no Claude Code (menos tokens que o script).
MCP_URL="${BASE%/api/v*}/mcp"
CLAUDE_BIN="$(type -P claude 2>/dev/null || true)"
[[ -z "$CLAUDE_BIN" && -x "$HOME/.local/bin/claude" ]] && CLAUDE_BIN="$HOME/.local/bin/claude"

ensure_mcp() { # [force] — 0 só com o MCP registrado (a marca de "já registrado" depende disso)
  [[ "${PRMAKE_SKIP_MCP:-0}" == 1 ]] && return 0
  [[ -n "$CLAUDE_BIN" ]] || return 1
  local tk; tk="$(token)" || return 1
  if [[ "${1:-}" != force ]] && "$CLAUDE_BIN" mcp get prmake >/dev/null 2>&1; then return 0; fi
  "$CLAUDE_BIN" mcp remove --scope user prmake >/dev/null 2>&1
  if "$CLAUDE_BIN" mcp add --transport http --scope user prmake "$MCP_URL" --header "x-api-key: $tk" >/dev/null 2>&1 \
     && "$CLAUDE_BIN" mcp get prmake >/dev/null 2>&1; then
    say "   MCP do PRMake registrado no Claude Code ($MCP_URL)"
    agent_doctor_bg
    return 0
  fi
  warn "não consegui registrar o MCP do PRMake (rode depois: prmake-skills.sh mcp)"
  return 1
}

# O executor (se instalado) refaz o diagnóstico em segundo plano: a tela "Meus executores" não fica com o resultado velho.
agent_doctor_bg() {
  local bin; bin="$(agent_bin 2>/dev/null)"
  [[ -x "$bin" ]] && ( "$bin" doctor >/dev/null 2>&1 & )
  return 0
}

# Executor do PRMake (0039): binário único publicado pela API para o sistema desta máquina.
AGENT_HOME="${PRMAKE_AGENT_HOME:-$HOME/.prmake-agent}"
agent_rid() {
  local arch; arch="$(uname -m 2>/dev/null)"; [[ "$arch" == arm64 || "$arch" == aarch64 ]] && arch=arm64 || arch=x64
  case "$(uname -s 2>/dev/null)" in Darwin) echo "osx-$arch" ;; Linux) echo "linux-$arch" ;; *) echo "win-x64" ;; esac
}
agent_bin() { [[ $WINDOWS -eq 1 ]] && echo "$AGENT_HOME/bin/prmake-agent.exe" || echo "$AGENT_HOME/bin/prmake-agent"; }
agent_download() {
  local tk rid bin code; tk="$(token)" || die "sem token (PRMAKE_TOKEN ou $TOKEN_FILE)"
  rid="$(agent_rid)"; bin="$(agent_bin)"; mkdir -p "$(dirname "$bin")"
  code="$(curl -s --max-time 300 -o "$TMP/agent" -w '%{http_code}' -H "x-api-key: $tk" "$BASE/ExecutionWorker/agent/$rid" 2>/dev/null)"
  [[ "$code" == 200 ]] || die "não consegui baixar o executor para $rid (HTTP $code)"
  chmod +x "$TMP/agent"
  [[ "$(uname -s)" == Darwin ]] && { xattr -d com.apple.quarantine "$TMP/agent" 2>/dev/null; codesign --force --sign - "$TMP/agent" >/dev/null 2>&1; }
  "$TMP/agent" version >/dev/null 2>&1 || die "o executor baixado não rodou nesta máquina ($rid)"
  [[ -f "$bin" ]] && mv -f "$bin" "$bin.old" 2>/dev/null
  mv -f "$TMP/agent" "$bin" && say "   executor $("$bin" version) em $bin"
}

# Repositórios desta máquina (0048): mapa nome-do-repositório (pelo remote) → pasta em ~/.prmake/repos.json, lido pelas
# skills (revamp-repos.sh, prmake-plan.sh, arch.sh) e pelo executor. Os repositórios que interessam são os das regras de
# BranchStrategy.repositories (Skills Configurations) — nada fixo aqui. Variáveis (EDV_SOLVACE_DIR, REVAMP_DIR) valem por
# cima do mapa. A busca nunca sobrescreve o que foi fixado à mão (repos set) ou veio de variável.
PRMAKE_HOME="${PRMAKE_HOME:-$HOME/.prmake}"
REPOS_FILE="$PRMAKE_HOME/repos.json"
RULES_CACHE="$PRMAKE_HOME/.skills-config.json"
REPOS_PRUNE=(node_modules bin obj Library .prmake-wt dist packages kb-mirror)
REPOS_CMD="bash ~/.claude/skills/.prmake/prmake-skills.sh repos"

lc() { printf '%s' "$1" | tr '[:upper:]' '[:lower:]'; }
# Caminho como fica gravado: no Windows C:/... (o bash e o executor .NET entendem); sem barra no fim.
norm_path() { local p="${1%/}"; command -v cygpath >/dev/null 2>&1 && p="$(cygpath -m "$p")"; printf '%s' "$p"; }
# Nome do repositório pelo remote origin (último segmento, sem .git); vazio se não houver.
repo_name_of() {
  local url; url="$(git -C "$1" config --get remote.origin.url 2>/dev/null | tr -d '\r')"
  url="${url%/}"; url="${url##*/}"; url="${url##*:}"; printf '%s' "${url%.git}"
}
# Regras (match<TAB>kind) do /Skills/config; sem rede usa a última salva. Sem nenhuma: 1.
repo_rules() {
  mkdir -p "$PRMAKE_HOME"
  if [[ "$(get "/config" "$TMP/skills-config.json")" =~ ^2 ]] && jq -e '.available' "$TMP/skills-config.json" >/dev/null 2>&1; then
    cp "$TMP/skills-config.json" "$RULES_CACHE"
  fi
  [[ -s "$RULES_CACHE" ]] || return 1
  jq -r '.settings.BranchStrategy // empty | (if type == "string" then (fromjson? // {}) else . end)
         | .repositories // [] | .[] | select(.match) | "\(.match)\t\(.kind // "")"' "$RULES_CACHE" 2>/dev/null
}
# kind da primeira regra que casa com o nome (glob, sem diferenciar maiúsculas); 1 se nenhuma.
kind_of() { # <nome> <arquivo de regras>
  local n; n="$(lc "$1")"
  local pat k
  while IFS=$'\t' read -r pat k; do
    [[ -z "$pat" ]] && continue
    # shellcheck disable=SC2053
    [[ "$n" == $(lc "$pat") ]] && { printf '%s' "$k"; return 0; }
  done < "$2"
  return 1
}
repos_map() { if [[ -s "$REPOS_FILE" ]] && jq -e '.version == 1' "$REPOS_FILE" >/dev/null 2>&1; then cat "$REPOS_FILE"; else echo '{"version":1,"repos":{},"ambiguous":{},"missing":[]}'; fi; }
repos_write() { # stdin = mapa novo
  mkdir -p "$PRMAKE_HOME"
  cat > "$REPOS_FILE.tmp.$$" && jq -e . "$REPOS_FILE.tmp.$$" >/dev/null && mv -f "$REPOS_FILE.tmp.$$" "$REPOS_FILE" || { rm -f "$REPOS_FILE.tmp.$$"; return 1; }
}
# Padrões das regras sem nenhum repositório no mapa (ex.: revamp-* sem módulo clonado).
repos_missing_json() { # <arquivo de regras> (mapa no stdin)
  local pats; pats="$(cut -f1 "$1" | jq -R . | jq -s -c .)"
  local names; names="$(jq -c '[(.repos // {} | keys[]), (.ambiguous // {} | keys[]) | ascii_downcase]')"
  local out=() p n hit
  while IFS= read -r p; do
    [[ -z "$p" ]] && continue
    hit=0
    while IFS= read -r n; do
      # shellcheck disable=SC2053
      [[ "$n" == $(lc "$p") ]] && { hit=1; break; }
    done < <(jq -r '.[]' <<<"$names")
    [[ $hit -eq 1 ]] || out+=("$p")
  done < <(jq -r '.[]' <<<"$pats")
  printf '%s\n' ${out[@]+"${out[@]}"} | jq -R 'select(length > 0)' | jq -s -c .
}

# Raízes da busca: PRMAKE_REPOS_ROOTS, ou as pastas comuns que existirem + pais do que já está no mapa + workspace do
# executor + pais das variáveis. macOS: Documents/Desktop/Downloads ficam de fora (pedem permissão do sistema — TCC —,
# inclusive para o serviço do executor); informe em PRMAKE_REPOS_ROOTS se os repositórios estiverem lá.
repos_roots() {
  local r list=()
  if [[ -n "${PRMAKE_REPOS_ROOTS:-}" ]]; then
    IFS=':;' read -r -a list <<< "$PRMAKE_REPOS_ROOTS"
  else
    for r in repos source src dev projects code git workspace work; do list+=("$HOME/$r"); done
    [[ "$(uname -s)" != Darwin ]] && list+=("$HOME/Documents")
    [[ $WINDOWS -eq 1 ]] && list+=(/c/repos /c/dev /c/projects /c/src /c/git /d/repos /d/dev /d/projects)
    [[ -n "${EDV_SOLVACE_DIR:-}" ]] && list+=("$(dirname "$EDV_SOLVACE_DIR")")
    [[ -n "${REVAMP_DIR:-}" ]] && list+=("$REVAMP_DIR")
    local ws; ws="$(jq -r '.workspace // empty' "$AGENT_HOME/config.json" 2>/dev/null)"; [[ -n "$ws" ]] && list+=("$ws")
    while IFS= read -r r; do [[ -n "$r" ]] && list+=("$(dirname "$r")"); done < <(repos_map | jq -r '.repos[].path // empty')
  fi
  # A home (e o que está acima dela) nunca é raiz: seria varrer tudo — o repositório direto na home tem busca própria.
  local home; home="$(cd "$HOME" && pwd -P)"
  for r in "${list[@]}"; do [[ -d "$r" ]] && (cd "$r" && pwd -P); done | awk '!seen[$0]++' \
    | while IFS= read -r r; do [[ "$home" == "$r" || "$home" == "$r"/* ]] || printf '%s\n' "$r"; done
}

# Busca: pastas com .git DIRETÓRIO (worktree tem .git arquivo — fica de fora) até 4 níveis abaixo de cada raiz (e 1 na
# home), sem entrar em ocultas, dependências/build, worktrees do executor e clones de leitura da Base Solvace. Repositório
# dentro de repositório também conta.
repos_find() { # <arquivo de saída>
  local out="$1" r prune=() n
  for n in "${REPOS_PRUNE[@]}"; do prune+=(-name "$n" -o); done
  prune+=(\( -name '.*' ! -name .git \))
  : > "$out"
  {
    while IFS= read -r r; do
      find "$r" -mindepth 1 -maxdepth 5 \( "${prune[@]}" \) -prune -o -type d -name .git -print -prune 2>/dev/null
    done < <(repos_roots)
    # Repositório direto na home (~/edv-solvace).
    find "$HOME" -mindepth 2 -maxdepth 2 -type d -name .git 2>/dev/null
  } | sed 's#/\.git$##' | awk '!seen[$0]++' > "$out"
}

repos_scan() { # [--quiet]
  local quiet=0; [[ "${1:-}" == --quiet ]] && quiet=1
  command -v git >/dev/null || { warn "git não encontrado — não dá para mapear os repositórios"; return 1; }
  repo_rules > "$TMP/rules.tsv" 2>/dev/null && [[ -s "$TMP/rules.tsv" ]] \
    || { [[ $quiet -eq 1 ]] || warn "sem as regras de repositório do PRMake (BranchStrategy) — sem token/rede? Nada mudou."; return 1; }
  local limit="${PRMAKE_REPOS_SCAN_TIMEOUT:-60}" pid waited=0 timed_out=0
  repos_find "$TMP/found.txt" & pid=$!
  while kill -0 "$pid" 2>/dev/null; do
    sleep 0.2; waited=$((waited + 1))
    if (( waited >= limit * 5 )); then pkill -P "$pid" 2>/dev/null; kill "$pid" 2>/dev/null; timed_out=1; break; fi
  done
  wait "$pid" 2>/dev/null
  # Repositório dentro de outro conta (ex.: revamp_separado é um repositório e tem os módulos dentro).
  sort "$TMP/found.txt" > "$TMP/found.sorted"
  local d name kind found='[]' env='[]' gone='[]' p
  while IFS= read -r d; do
    name="$(repo_name_of "$d")"; [[ -n "$name" ]] || continue
    kind="$(kind_of "$name" "$TMP/rules.tsv")" || continue
    found="$(jq -c --arg n "$name" --arg p "$(norm_path "$d")" --arg k "$kind" '. + [{name:$n, path:$p, kind:$k}]' <<<"$found")"
  done < "$TMP/found.sorted"
  # Variáveis viram entradas "env" (o serviço do executor não herda as variáveis do terminal).
  local envdirs=()
  [[ -n "${EDV_SOLVACE_DIR:-}" && -d "${EDV_SOLVACE_DIR:-}/.git" ]] && envdirs+=("$EDV_SOLVACE_DIR")
  if [[ -n "${REVAMP_DIR:-}" && -d "${REVAMP_DIR:-}" ]]; then
    for d in "$REVAMP_DIR"/*/; do [[ -d "${d}.git" ]] && envdirs+=("${d%/}"); done
  fi
  for d in ${envdirs[@]+"${envdirs[@]}"}; do
    name="$(repo_name_of "$d")"; [[ -n "$name" ]] || continue
    kind="$(kind_of "$name" "$TMP/rules.tsv")" || continue
    env="$(jq -c --arg n "$name" --arg p "$(norm_path "$d")" --arg k "$kind" '. + [{name:$n, path:$p, kind:$k}]' <<<"$env")"
  done
  while IFS= read -r p; do [[ -n "$p" && ! -d "$p" ]] && gone="$(jq -c --arg p "$p" '. + [$p]' <<<"$gone")"; done \
    < <(repos_map | jq -r '.repos[] | select(.source == "scan") | .path')
  # Busca completa: as entradas "scan" antigas saem (são refeitas pelo que foi achado agora). Interrompida pelo tempo:
  # mantém as antigas cuja pasta ainda existe.
  repos_map | jq --argjson found "$found" --argjson env "$env" --argjson gone "$gone" --argjson complete "$([[ $timed_out -eq 0 ]] && echo true || echo false)" \
      --arg now "$(date +%Y-%m-%dT%H:%M:%S%z)" --argjson roots "$(repos_roots | jq -R . | jq -s -c .)" '
    def lc: ascii_downcase;
    def pick($n): map(select((.key | lc) == ($n | lc))) | first;
    def drop($n): map(select((.key | lc) != ($n | lc)));
    ((.repos // {}) | to_entries
      | map(select(.value.source != "scan" or ($complete | not) and (.value.path as $p | $gone | index($p) | not)))) as $kept
    | (reduce $env[] as $e ($kept;
        pick($e.name) as $cur
        | if ($cur.value.source // "") == "manual" then .
          else drop($e.name) + [{key: $e.name, value: {path: $e.path, kind: $e.kind, source: "env", confirmed: true}}] end)) as $entries
    | (reduce ($found | group_by(.name | lc))[] as $g ({entries: $entries, amb: {}};
        ($g[0].name) as $n | (.entries | pick($n)) as $cur
        | if (($cur.value.source // "") | IN("manual", "env")) then .
          elif ($g | length) == 1 then
            .entries = (.entries | drop($n)) + [{key: $n, value: {path: $g[0].path, kind: $g[0].kind, source: "scan",
              confirmed: ((($cur.value.path // "") == $g[0].path) and ($cur.value.confirmed // false))}}]
          else .amb[$n] = ($g | map(.path) | unique) | .entries = (.entries | drop($n)) end)) as $r
    | {version: 1, updatedAt: $now, roots: $roots, repos: ($r.entries | sort_by(.key | lc) | from_entries), ambiguous: $r.amb}' \
    > "$TMP/map.json" || return 1
  jq --argjson m "$(repos_missing_json "$TMP/rules.tsv" < "$TMP/map.json")" '.missing = $m' "$TMP/map.json" | repos_write || return 1
  [[ $timed_out -eq 1 ]] && warn "a busca passou de ${limit}s e parou — gravei o que achei (PRMAKE_REPOS_SCAN_TIMEOUT aumenta; PRMAKE_REPOS_ROOTS limita as pastas)"
  [[ $quiet -eq 1 ]] || { repos_show; [[ -t 0 && -t 1 ]] && repos_interactive; }
  return 0
}

# Uma linha: "N mapeados, A ambíguos, F sem clone (padrões)".
repos_summary() {
  [[ -s "$REPOS_FILE" ]] || { echo "sem mapa — rode: $REPOS_CMD scan"; return 1; }
  jq -r '"\(.repos | length) mapeados"
    + (if (.ambiguous // {} | length) > 0 then ", \(.ambiguous | length) ambíguo(s): \(.ambiguous | keys | join(", "))" else "" end)
    + (if (.missing // [] | length) > 0 then ", sem clone: \(.missing | join(", "))" else "" end)' "$REPOS_FILE"
}
repos_pending() { [[ -s "$REPOS_FILE" ]] && jq -e '((.ambiguous // {}) | length) + ((.missing // []) | length) > 0' "$REPOS_FILE" >/dev/null 2>&1; }

repos_show() {
  [[ -s "$REPOS_FILE" ]] || { echo "Nenhum mapa ainda ($REPOS_FILE). Rode: $REPOS_CMD scan"; return 0; }
  echo "Repositórios desta máquina ($REPOS_FILE):"
  local name path kind src conf br
  while IFS=$'\t' read -r name path kind src conf; do
    br="$(git -C "$path" rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"
    printf '  %-28s %-16s %-22s %s%s\n' "$name" "${kind:--}" "$br" "$path" \
      "$([[ "$src" == scan && "$conf" != true ]] && echo '  (busca)' || { [[ "$src" == env ]] && echo '  (variável)'; } || true)"
  done < <(jq -r '.repos | to_entries[] | [.key, .value.path, (.value.kind // ""), (.value.source // ""), ((.value.confirmed // false) | tostring)] | @tsv' "$REPOS_FILE")
  jq -r '(.ambiguous // {}) | to_entries[] | "  AMBÍGUO \(.key): \(.value | join("  |  "))"' "$REPOS_FILE"
  jq -r '(.missing // []) | if length > 0 then "  sem clone nesta máquina: \(join(", "))" else empty end' "$REPOS_FILE"
  if repos_pending; then
    echo "Resolver: $REPOS_CMD set <repo> <pasta>   (ou PRMAKE_REPOS_ROOTS=<pastas> $REPOS_CMD scan)"
  fi
}

# Terminal interativo: escolhe entre os ambíguos, informa a pasta do que não foi achado e confirma o resto.
repos_interactive() {
  local name i choice cands=() p ans
  while IFS= read -r name; do
    [[ -n "$name" ]] || continue
    cands=(); while IFS= read -r p; do cands+=("$p"); done < <(jq -r --arg n "$name" '.ambiguous[$n][]' "$REPOS_FILE")
    echo "Qual pasta é o seu clone de trabalho de $name?"
    for i in "${!cands[@]}"; do echo "  $((i + 1))) ${cands[$i]}"; done
    read -rp "Número (Enter = decidir depois): " choice < /dev/tty
    [[ "$choice" =~ ^[0-9]+$ ]] && (( choice >= 1 && choice <= ${#cands[@]} )) && repos_set "$name" "${cands[$((choice - 1))]}"
  done < <(jq -r '(.ambiguous // {}) | keys[]' "$REPOS_FILE")
  while IFS= read -r p; do
    [[ -n "$p" ]] || continue
    read -rp "Pasta de um clone de $p (Enter = não tenho/pular): " ans < /dev/tty
    [[ -n "$ans" ]] || continue
    ans="${ans/#\~/$HOME}"
    local n; n="$(repo_name_of "$ans")"
    # shellcheck disable=SC2053
    if [[ -z "$n" ]]; then warn "$ans não é um repositório git com remote origin — pulei"
    elif [[ "$(lc "$n")" != $(lc "$p") ]]; then warn "o remote de $ans é '$n', não casa com '$p' — pulei"
    else repos_set "$n" "$ans"; fi
  done < <(jq -r '(.missing // [])[]' "$REPOS_FILE")
  if jq -e '[.repos[] | select(.source == "scan" and (.confirmed | not))] | length > 0' "$REPOS_FILE" >/dev/null; then
    read -rp "Confirmar os demais repositórios achados pela busca? [S/n] " ans < /dev/tty
    [[ "$ans" =~ ^[nN] ]] || jq '.repos |= map_values(if .source == "scan" then .confirmed = true else . end)' "$REPOS_FILE" | repos_write
  fi
  repos_summary
}

repos_set() { # <repo> <pasta>
  local want="${1:?uso: repos set <repo> <pasta>}" dir="${2:?uso: repos set <repo> <pasta>}" name kind=""
  dir="${dir/#\~/$HOME}"
  [[ -d "$dir/.git" || -f "$dir/.git" ]] || die "não é um repositório git: $dir"
  dir="$(cd "$dir" && pwd -P)"
  name="$(repo_name_of "$dir")"
  [[ -n "$name" ]] || die "$dir não tem remote origin"
  [[ "$(lc "$name")" == "$(lc "$want")" || "$(lc "$name")" == "revamp-$(lc "$want")" ]] || die "o remote de $dir é '$name', não '$want'"
  repo_rules > "$TMP/rules.tsv" 2>/dev/null && kind="$(kind_of "$name" "$TMP/rules.tsv")"
  [[ -n "$kind" ]] || kind="$(repos_map | jq -r --arg n "$(lc "$name")" '[.repos | to_entries[] | select((.key | ascii_downcase) == $n) | .value.kind][0] // ""')"
  repos_map | jq --arg n "$name" --arg p "$(norm_path "$dir")" --arg k "$kind" '
      .repos = ((.repos // {}) | with_entries(select((.key | ascii_downcase) != ($n | ascii_downcase))) + {($n): {path: $p, kind: $k, source: "manual", confirmed: true}})
      | .ambiguous = ((.ambiguous // {}) | with_entries(select((.key | ascii_downcase) != ($n | ascii_downcase))))' > "$TMP/map.json"
  if [[ -s "$TMP/rules.tsv" ]]; then
    jq --argjson m "$(repos_missing_json "$TMP/rules.tsv" < "$TMP/map.json")" '.missing = $m' "$TMP/map.json" | repos_write
  else repos_write < "$TMP/map.json"; fi
  say "✔ $name → $(norm_path "$dir")"
}

repos_unset() { # <repo>
  local want; want="$(lc "${1:?uso: repos unset <repo>}")"
  repos_map | jq --arg n "$want" '.repos |= with_entries(select((.key | ascii_downcase) != $n)) | .ambiguous |= with_entries(select((.key | ascii_downcase) != $n))' | repos_write
  say "removido do mapa: $1 (a próxima busca pode achá-lo de novo)"
}

# Pasta de um repositório (contrato 0048): stdout = pasta, exit 0 · 2 = não está no mapa · 3 = ambíguo (candidatos no
# stderr). Ordem: variável → mapa (nome do remote, forma curta revamp-, nome da pasta, trecho único) → caminho padrão.
repos_path() { # <repo>
  local q; q="$(lc "${1:?uso: repos path <repo>}")"; q="${q%/}"
  local d n
  if [[ -n "${EDV_SOLVACE_DIR:-}" && -d "${EDV_SOLVACE_DIR:-}" ]]; then
    n="$(lc "$(repo_name_of "$EDV_SOLVACE_DIR")")"; [[ -n "$n" ]] || n=edv-solvace
    [[ "$q" == "$n" ]] && { norm_path "$EDV_SOLVACE_DIR"; echo; return 0; }
  fi
  if [[ -n "${REVAMP_DIR:-}" && -d "${REVAMP_DIR:-}" ]]; then
    for d in "$REVAMP_DIR"/*/; do
      [[ -d "${d}.git" ]] || continue
      n="$(lc "$(repo_name_of "$d")")"
      [[ "$q" == "$n" || "revamp-$q" == "$n" || "$q" == "$(lc "$(basename "$d")")" ]] && { norm_path "$d"; echo; return 0; }
    done
  fi
  if [[ -s "$REPOS_FILE" ]]; then
    local hits
    # 1) nome exato  2) revamp-<q>  3) nome da pasta  4) trecho do nome (só se único)
    hits="$(jq -r --arg q "$q" '
      (.repos | to_entries) as $e
      | ([$e[] | select((.key | ascii_downcase) == $q)]) as $a
      | ([$e[] | select((.key | ascii_downcase) == ("revamp-" + $q))]) as $b
      | ([$e[] | select((.value.path | split("/") | last | ascii_downcase) == $q)]) as $c
      | ([$e[] | select(.key | ascii_downcase | contains($q))]) as $d
      | (if ($a | length) > 0 then $a elif ($b | length) > 0 then $b elif ($c | length) > 0 then $c else $d end)
      | .[] | .value.path' "$REPOS_FILE")"
    if [[ -n "$hits" && "$(wc -l <<<"$hits" | tr -d ' ')" == 1 ]]; then printf '%s\n' "$hits"; return 0; fi
    if [[ -n "$hits" ]]; then echo "ERRO: '$1' casa com mais de um repositório:" >&2; sed 's/^/  /' <<<"$hits" >&2; return 3; fi
    local amb
    amb="$(jq -r --arg q "$q" '(.ambiguous // {}) | to_entries[] | select((.key | ascii_downcase) == $q or (.key | ascii_downcase) == ("revamp-" + $q)) | .value[]' "$REPOS_FILE")"
    if [[ -n "$amb" ]]; then
      echo "ERRO: '$1' tem mais de um clone nesta máquina — escolha com: $REPOS_CMD set $1 <pasta>" >&2
      sed 's/^/  /' <<<"$amb" >&2; return 3
    fi
  fi
  # Caminhos padrão de antes da 0048.
  for d in "$HOME/repos/solvace/$q" "$HOME/repos/solvace/revamp_separado"/*/; do
    [[ -d "${d%/}/.git" ]] || continue
    n="$(lc "$(repo_name_of "$d")")"
    [[ "$q" == "$n" || "revamp-$q" == "$n" ]] && { norm_path "$d"; echo; return 0; }
  done
  echo "ERRO: repositório '$1' não está no mapa desta máquina — informe a pasta: $REPOS_CMD set $1 <pasta>" >&2
  return 2
}

# Migração de quem já instalou (.repos-v1): monta o mapa em segundo plano no update do hook; avisa na sessão seguinte se
# houver pendência (só quando o resumo muda, para não repetir a cada sessão).
ensure_repos_map() {
  [[ "${PRMAKE_SKIP_REPOS:-0}" == 1 ]] && return 0
  if [[ ! -f "$TOOL_DIR/.repos-v1" ]]; then
    (nohup bash "$TOOL_DIR/prmake-skills.sh" repos scan --quiet --mark >/dev/null 2>&1 &)
    return 0
  fi
  repos_pending || return 0
  local s; s="$(repos_summary)"
  [[ "$(cat "$TOOL_DIR/.repos-notified" 2>/dev/null)" == "$s" ]] && return 0
  printf '%s' "$s" > "$TOOL_DIR/.repos-notified"
  echo "PRMake: repositórios desta máquina — $s. Confira com: $REPOS_CMD"
}

case "$cmd" in
  install)
    windows_prepare
    need; lock
    token >/dev/null || die "sem token: rode com PRMAKE_TOKEN=<sua api-key do PRMake> ou crie $TOKEN_FILE"
    if [[ -n "${PRMAKE_TOKEN:-}" ]]; then
      if [[ ! -f "$TOKEN_FILE" ]] || [[ "$(tr -d '\r\n' < "$TOKEN_FILE")" != "$PRMAKE_TOKEN" ]]; then
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
    ensure_permissions
    ensure_mcp force
    sync_kb
    self_update
    # 0048: onde estão os repositórios nesta máquina (com terminal, confirma/corrige).
    say "Procurando os repositórios desta máquina…"
    repos_scan $([[ $QUIET -eq 1 ]] && echo --quiet) && { : > "$TOOL_DIR/.repos-v1"; } || warn "não mapeei os repositórios agora — depois: $REPOS_CMD scan"
    say "Pronto. As skills estão em $SKILLS_DIR e se atualizam sozinhas a cada sessão do Claude Code."
    if [[ "${PRMAKE_AGENT:-0}" == 1 ]]; then
      bash "$TOOL_DIR/prmake-skills.sh" agent install
    elif [[ ! -x "$(agent_bin)" ]]; then
      say "Dica: para o PRMake rodar a análise sozinho (botão \"Analisar com Claude\", sem abrir o terminal):"
      say "   bash $TOOL_DIR/prmake-skills.sh agent install"
    fi
    ;;

  update)
    command -v jq >/dev/null && command -v curl >/dev/null || exit 0
    [[ $QUIET -eq 1 ]] && windows_prepare >/dev/null 2>&1 || windows_prepare
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
    # Skill já instalada e em dia pode ter ganhado dependência nova.
    for name in $(selected ${ARGS[@]+"${ARGS[@]}"}); do
      [[ -d "$SKILLS_DIR/$name" ]] && install_depends "$name"
    done
    ensure_hook
    ensure_permissions
    # MCP do PRMake: uma vez por máquina (quem removeu de propósito não ganha de volta).
    [[ -f "$TOOL_DIR/.mcp-registered" ]] || { ensure_mcp && touch "$TOOL_DIR/.mcp-registered"; }
    sync_kb
    self_update
    ensure_repos_map
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
    echo "repositórios: $(repos_summary)"
    ;;

  doctor)
    # Diagnóstico (0035): o que as skills enxergam nesta máquina — cole a saída para o suporte.
    ok() { printf '  %-28s %s\n' "$1" "$2"; }
    echo "sistema:   $(uname -s 2>/dev/null) (windows=$WINDOWS)  bash $BASH_VERSION"
    echo "HOME:      $HOME"
    echo "skills:    $SKILLS_DIR"
    for c in curl jq python3 py python unzip rsync git; do ok "$c" "$(type -P "$c" 2>/dev/null || echo '— não encontrado')"; done
    ok "jq sem CRLF" "$([[ "$(jq -rn '"x"' 2>/dev/null)" == x ]] && echo ok || echo 'NÃO (rode: prmake-skills.sh update)')"
    ok "python3 funciona" "$(python3 -c 'import sys; print(sys.version.split()[0])' 2>/dev/null | tr -d '\r' || echo NÃO)"
    ok "token" "$(token >/dev/null && echo "ok ($TOKEN_FILE)" || echo 'NÃO — rode o instalador da tela Skills')"
    ok "API ($BASE)" "HTTP $(get "" "$TMP/catalog.json")"
    ok "hook SessionStart" "$(jq -r '[.hooks.SessionStart[]?.hooks[]? | select((.command // "") | contains("prmake-skills.sh")) | .command + (if .shell then " [shell=" + .shell + "]" else "" end)] | if length == 0 then "NÃO instalado" else join(" | ") end' "$SETTINGS" 2>/dev/null || echo "settings.json inválido")"
    ok "hook esperado" "$HOOK_CMD"
    permissions_report
    creds_report
 ok "MCP do PRMake" "$([[ -n "$CLAUDE_BIN" ]] && "$CLAUDE_BIN" mcp get prmake >/dev/null 2>&1 && echo "ok ($MCP_URL)" || echo 'NÃO — rode: prmake-skills.sh mcp')"
    ok "executor" "$([[ -x "$(agent_bin)" ]] && "$(agent_bin)" status 2>/dev/null | head -1 || echo 'não instalado (opcional: prmake-skills.sh agent install)')"
    ok "repositórios" "$(repos_summary) ($REPOS_FILE)"
    for d in "$SKILLS_DIR"/*/; do [[ -f "$d/$MANIFEST" ]] && ok "$(basename "$d")" "$(jq -r '.version' "$d/$MANIFEST")$([[ -x "$d/.venv/bin/python" || -x "$d/.venv/Scripts/python.exe" ]] && echo ' (venv ok)')"; done
    ;;

  permissions)
    # Reaplica as regras mesmo que o usuário as tenha removido (ignora os marcadores).
    PRMAKE_SKIP_PERMISSIONS=0 ensure_permissions 1
    permissions_report
    ;;

  db-credentials)
    db_credentials ${ARGS[@]+"${ARGS[@]}"}
    ;;

  mcp)
    [[ -n "$CLAUDE_BIN" ]] || die "Claude Code (claude) não encontrado no PATH"
    if [[ "${ARGS[0]:-}" == remove ]]; then
      "$CLAUDE_BIN" mcp remove --scope user prmake && touch "$TOOL_DIR/.mcp-registered" && say "MCP do PRMake removido"
    else
      ensure_mcp force && touch "$TOOL_DIR/.mcp-registered"
    fi
    ;;

  agent)
    case "${ARGS[0]:-install}" in
      install)
        agent_download
        # 0048: o executor acha os repositórios pelo mapa (pasta onde abre o Claude e --add-dir de cada um).
        [[ -s "$REPOS_FILE" ]] || { say "Procurando os repositórios desta máquina…"; repos_scan && : > "$TOOL_DIR/.repos-v1"; } \
          || warn "não mapeei os repositórios — depois: $REPOS_CMD scan"
        "$(agent_bin)" register || die "registro da máquina falhou"
        "$(agent_bin)" install || die "não consegui ligar o serviço do executor"
        "$(agent_bin)" doctor || warn "o doctor encontrou problemas — veja acima (também aparecem em \"Meus executores\" no PRMake)"
        ;;
      update) agent_download && "$(agent_bin)" install ;;
      status) [[ -x "$(agent_bin)" ]] && "$(agent_bin)" status || echo "executor não instalado (prmake-skills.sh agent install)" ;;
      uninstall) [[ -x "$(agent_bin)" ]] && "$(agent_bin)" uninstall ;;
      *) die "uso: prmake-skills.sh agent install|update|status|uninstall" ;;
    esac
    ;;

  repos)
    command -v jq >/dev/null || die "precisa do jq"
    case "${ARGS[0]:-show}" in
      show) repos_show ;;
      scan)
        repos_scan $([[ $QUIET -eq 1 ]] && echo --quiet) || exit 1
        if [[ " ${ARGS[*]} " == *" --mark "* ]]; then mkdir -p "$TOOL_DIR"; : > "$TOOL_DIR/.repos-v1"; fi
        ;;
      set) repos_set "${ARGS[1]:-}" "${ARGS[2]:-}" ;;
      unset) repos_unset "${ARGS[1]:-}" ;;
      path) repos_path "${ARGS[1]:-}" ;;
      summary) repos_summary ;;
      *) die "uso: prmake-skills.sh repos [show | scan [--quiet] | set <repo> <pasta> | unset <repo> | path <repo> | summary]" ;;
    esac
    ;;

  *) die "uso: prmake-skills.sh install|update|status|doctor|permissions|db-credentials|mcp|agent|repos [--quiet] [--force] [skill...]" ;;
esac
