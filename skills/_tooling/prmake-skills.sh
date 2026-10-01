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
trap 'rm -rf "$TMP"; rmdir "$TOOL_DIR/.lock" 2>/dev/null' EXIT

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
    sync_kb
    self_update
    say "Pronto. As skills estão em $SKILLS_DIR e se atualizam sozinhas a cada sessão do Claude Code."
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
    sync_kb
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

  *) die "uso: prmake-skills.sh install|update|status|doctor|permissions|db-credentials [--quiet] [--force] [skill...]" ;;
esac
