#!/usr/bin/env bash
# Validacao em ambiente (feature 0092): ambientes, plantas, admin da planta, senha local, navegador e evidencias.
#
# Uso:
#   env.sh envs [--refresh]                      ambientes conhecidos (ValidationEnvironments do PRMake; cache 10 min)
#   env.sh resolve <ambiente|url|cliente>        URL, usuario padrao, somente leitura, banco (host/global); fora da lista:
#                                                procura no banco corporativo (TB_WCM_ENVIRONMENT, so colunas sem segredo)
#   env.sh plants <ambiente> [filtro]            plantas do ambiente (TB_WCM_SITE do banco global: sigla, banco local)
#   env.sh admin <ambiente> <sigla-da-planta>    admin da planta (admin<sigla>@solvacelabs.com) e se existe/ativo no banco
#   env.sh user <ambiente> <email>               o usuario existe e esta ativo no banco global?
#   env.sh has-password <ambiente> <email>       exit 0 = ha senha nesta maquina; 4 = falta (mostra o comando para a pessoa)
#   env.sh browser start <ambiente> [--user <email>] [--plant <sigla>] [--card <card>] [--task <n>] [--headed]
#   env.sh browser go|click|fill|select|press|wait|shot|snap|text|stop <sessao> [args]   (env_browser.py)
#   env.sh browser status
#   env.sh evidence <card> <item> <sub-tarefa> <pasta> [--report <relatorio.md>]
#                                                sobe os prints (NN-*.jpg|png, na ordem) como evidencia da sub-tarefa
#   env.sh setup                                 prepara o venv (Playwright; usa o Chrome da maquina)
# Senha: so no arquivo local ~/.claude/solvace-env-credentials.json, gravado pela PESSOA no terminal dela:
#   bash ~/.claude/skills/.prmake/prmake-skills.sh env-credentials set <ambiente> <email>
# Exit: 0 ok · 1 erro · 2 sem navegador/Playwright · 3 login recusado · 4 sem senha · 5 sessao inexistente.
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

DIR="$(cd "$(dirname "$0")" && pwd)"
SKILL_DIR="$(dirname "$DIR")"
BASE="${PRMAKE_API_BASE:-https://api.softhouse.app.br/api/v1}"
SQL="$HOME/.claude/skills/analisar-bug/scripts/sql-query.sh"
CACHE="$HOME/.prmake/cache/validation-environments.json"
CREDS="${PRMAKE_ENV_CREDENTIALS:-$HOME/.claude/solvace-env-credentials.json}"
CREDS_CMD="bash ~/.claude/skills/.prmake/prmake-skills.sh env-credentials set"
CARDS="${CARDS_DIR:-$HOME/.prmake/cards}"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/env-val.XXXXXX")"; trap 'rm -rf "$TMP"' EXIT

die() { echo "ERRO: $*" >&2; exit "${2:-1}"; }
token() {
  if [[ -n "${PRMAKE_TOKEN:-}" ]]; then printf '%s' "$PRMAKE_TOKEN" | tr -d '\r\n'; return; fi
  [[ -s "$HOME/.claude/prmake-token.txt" ]] && tr -d '\r\n' < "$HOME/.claude/prmake-token.txt"
}

venv_python() {
  local p="$SKILL_DIR/.venv/bin/python"
  [[ -x "$p" ]] || p="$SKILL_DIR/.venv/Scripts/python.exe"
  printf '%s' "$p"
}
# Venv com pip que funciona: alguns Pythons criam o venv sem pip (ex.: 3.14 sem ler a versao do macOS — ensurepip
# falha); tenta os interpretadores da maquina ate um dar certo.
setup() {
  local py c
  py="$(venv_python)"
  if [[ ! -x "$py" ]] || ! "$py" -m pip --version >/dev/null 2>&1; then
    for c in python3 python3.13 python3.12 python3.11 python3.10 py python; do
      command -v "$c" >/dev/null 2>&1 || continue
      "$c" -c 'import sys; sys.exit(0 if sys.version_info >= (3, 9) else 1)' >/dev/null 2>&1 || continue
      rm -rf "$SKILL_DIR/.venv"
      "$c" -m venv "$SKILL_DIR/.venv" >/dev/null 2>&1
      py="$(venv_python)"
      [[ -x "$py" ]] && "$py" -m pip --version >/dev/null 2>&1 && break
      py=""
    done
    [[ -n "$py" ]] || die "nao consegui criar um venv com pip em $SKILL_DIR/.venv (instale o Python 3.10+ — python.org ou brew install python@3.13)" 2
  fi
  if ! "$py" -c 'import playwright' >/dev/null 2>&1; then
    echo "Instalando o Playwright no venv da skill (uma vez, ~40 MB; usa o Chrome desta maquina)..." >&2
    "$py" -m pip install -q --disable-pip-version-check playwright >&2 || die "pip install playwright falhou — sem internet/proxy?" 2
  fi
}

# Configuracao (ValidationEnvironments) — PRMake, cache de 10 min; sem rede: o cache ou o padrao abaixo.
DEFAULT_ENVS='{"environments":[
 {"key":"dev","label":"Desenvolvimento","url":"https://devsp.solvacelabs.com/","user":"admin@ewcm.com.br","kind":"dev","database":{"host":"dev","global":"DB_SOLV_DEV_GLOBAL"}},
 {"key":"qa","label":"QA","url":"https://qa.solvacelabs.com/","user":"admin@ewcm.com.br","kind":"qa","database":{"host":"qa","global":"DB_SOLV_QA_GLOBAL"}},
 {"key":"performancetest","label":"Teste e performance","url":"https://performancetest.solvacelabs.com/","kind":"test"},
 {"key":"sandboxefeso","label":"Homologação de release","url":"https://sandboxefeso.solvacelabs.com/","kind":"homolog","database":{"host":"sandboxefeso","global":"DB_EFESO_TST_GLOBAL"}},
 {"key":"sandboxrt","label":"Homologação de release/hotfix","url":"https://sandboxrt.solvacelabs.com/","kind":"homolog"},
 {"key":"demo","label":"Demonstração","url":"https://demo.solvacelabs.com/","kind":"demo","readOnly":true,"database":{"host":"prod","global":"DB_DEMO_PRD_GLOBAL"}},
 {"key":"gerdau","label":"Produção Gerdau","url":"https://gerdau.solvacelabs.com/","kind":"production","client":"Gerdau","readOnly":true,"database":{"host":"prod1","global":"DB_GERDAU_PRD_GLOBAL"}},
 {"key":"mondelez","label":"Produção Mondelez","url":"https://mondelez.solvacelabs.com/","kind":"production","client":"Mondelez","readOnly":true,"database":{"host":"prod","global":"DB_MDLZ_PRD_GLOBAL"}}],
 "plantAdminPattern":"admin{plant}@solvacelabs.com",
 "corporate":{"host":"corporate","database":"CORPORATE_SOLVACE_DB","table":"TB_WCM_ENVIRONMENT"},
 "productionReadOnly":true}'
load_envs() {
  local refresh="${1:-}" tok code
  mkdir -p "$(dirname "$CACHE")"
  if [[ "$refresh" != "--refresh" && -s "$CACHE" && -n "$(find "$CACHE" -mmin -10 2>/dev/null)" ]]; then return 0; fi
  tok="$(token)"
  if [[ -n "$tok" ]]; then
    code="$(curl -s --max-time 30 -o "$TMP/config.json" -w '%{http_code}' -H "x-api-key: $tok" -H 'X-Execution-Client: skill' "$BASE/Skills/config" 2>/dev/null)"
    if [[ "$code" == 200 ]] && jq -e '.settings.ValidationEnvironments | type == "object"' "$TMP/config.json" >/dev/null 2>&1; then
      jq '.settings.ValidationEnvironments' "$TMP/config.json" > "$CACHE"; return 0
    fi
  fi
  [[ -s "$CACHE" ]] && return 0
  printf '%s' "$DEFAULT_ENVS" | jq . > "$CACHE"
}

# Ambiente pela chave, URL/host ou parte do nome; vazio se nao achar.
find_env() { # <termo>
  local term="$1" host
  host="$(printf '%s' "$term" | sed -E 's#^[a-z]+://##; s#/.*$##' | tr 'A-Z' 'a-z')"
  jq -c --arg t "$(printf '%s' "$term" | tr 'A-Z' 'a-z')" --arg h "$host" '
    def h(u): (u // "") | ascii_downcase | sub("^[a-z]+://"; "") | sub("/.*$"; "");
    [.environments[] | select((.key | ascii_downcase) == $t or h(.url) == $h)][0]
    // [.environments[] | select(((.label // "") + " " + (.client // "") | ascii_downcase) | contains($t))][0]
    // empty' "$CACHE"
}

# Banco: host da configuracao; sem host, procura o banco global em cada servidor das credenciais da maquina.
db_host() { # <global>
  local global="$1" alias out
  for alias in $(jq -r '(.servers // [])[].alias | select(test("^[A-Za-z0-9_-]+$"))' "$HOME/.claude/sqlserver-credentials.json" 2>/dev/null); do
    out="$(bash "$SQL" --host "$alias" -d master -q "SELECT name FROM sys.databases WHERE name = '$global'" --json 2>/dev/null)" || continue
    printf '%s' "$out" | grep -q "\"$global\"" && { printf '%s' "$alias"; return 0; }
  done
  return 1
}
env_db() { # <env-json> -> "host global" (descobre o host se faltar)
  local env="$1" host global
  host="$(jq -r '.database.host // empty' <<<"$env")"; global="$(jq -r '.database.global // empty' <<<"$env")"
  if [[ -z "$global" ]]; then
    global="$(corporate_lookup "$(jq -r '.url' <<<"$env")" | jq -r '.[0].CATALOG_GLOBAL // empty' 2>/dev/null)"
  fi
  [[ -n "$global" ]] || return 1
  [[ -n "$host" ]] || host="$(db_host "$global")" || return 1
  printf '%s %s' "$host" "$global"
}
corporate_lookup() { # <termo> -> JSON (so colunas sem segredo)
  local term c_host c_db t
  c_host="$(jq -r '.corporate.host // "corporate"' "$CACHE")"; c_db="$(jq -r '.corporate.database // "CORPORATE_SOLVACE_DB"' "$CACHE")"
  t="$(printf '%s' "$1" | sed -E 's#^[a-z]+://##; s#/.*$##' | tr -cd 'A-Za-z0-9 ._-')"
  [[ -n "$t" ]] || return 1
  bash "$SQL" --host "$c_host" -d "$c_db" --json -q "SELECT TOP 10 ENVIRONMENT_ID, ENVIRONMENT_NAME, ENVIRONMENT_URL, CATALOG_GLOBAL, SSO FROM TB_WCM_ENVIRONMENT WHERE ENVIRONMENT_URL LIKE '%$t%' OR ENVIRONMENT_NAME LIKE '%$t%' ORDER BY ENVIRONMENT_ID" 2>/dev/null \
    | jq -c 'if type == "object" then (.rows // .data // []) else . end' 2>/dev/null
}
resolve_json() { # <termo> -> JSON do ambiente (configurado ou achado no corporativo)
  local term="$1" env row rows host
  load_envs
  env="$(find_env "$term")"
  if [[ -n "$env" ]]; then printf '%s' "$env"; return 0; fi
  rows="$(corporate_lookup "$term")"
  host="$(printf '%s' "$term" | sed -E 's#^[a-z]+://##; s#/.*$##' | tr 'A-Z' 'a-z')"
  row="$(jq -c --arg h "$host" '[.[] | select((.ENVIRONMENT_URL | ascii_downcase | sub("^[a-z]+://"; "") | sub("/.*$"; "")) == $h)][0]
    // (if length == 1 then .[0] else empty end)' <<<"${rows:-[]}" 2>/dev/null)"
  if [[ -z "$row" ]]; then
    if [[ "$(jq 'length' <<<"${rows:-[]}" 2>/dev/null)" -gt 1 ]]; then
      echo "AMBIGUO: '$term' casa mais de um ambiente no banco corporativo — use a URL:" >&2
      jq -r '.[] | "  \(.ENVIRONMENT_URL)\t\(.ENVIRONMENT_NAME)"' <<<"$rows" >&2
    fi
    return 1
  fi
  jq -c '{key: (.ENVIRONMENT_URL | ascii_downcase | sub("^[a-z]+://"; "") | sub("\\..*$"; "")), label: .ENVIRONMENT_NAME,
      url: (.ENVIRONMENT_URL | sub("/?$"; "/")), kind: (if (.ENVIRONMENT_NAME | test("Production"; "i")) then "production" else "test" end),
      readOnly: (.ENVIRONMENT_NAME | test("Production|Demo"; "i")), database: {global: .CATALOG_GLOBAL}, source: "corporativo"}' <<<"$row"
}
need_env() { resolve_json "$1" || die "ambiente '$1' nao encontrado (nem na configuracao nem no banco corporativo) — confira com: env.sh envs" ; }

has_password() { # <env-json> <email>
  [[ -s "$CREDS" ]] || return 1
  jq -e --arg k "$(jq -r .key <<<"$1")" --arg u "$(printf '%s' "$2" | tr 'A-Z' 'a-z')" --arg h "$(jq -r '.url' <<<"$1" | sed -E 's#^[a-z]+://##; s#/.*$##' | tr 'A-Z' 'a-z')" '
    [(.environments // [])[] | select(((.user // "") | ascii_downcase) == $u
      and (((.env // "") | ascii_downcase) == $k or (((.url // .host // "") | ascii_downcase | sub("^[a-z]+://"; "") | sub("/.*$"; "")) == $h)))
      | select((.password // "") != "")] | length > 0' "$CREDS" >/dev/null 2>&1
}

default_user() { # <env-json> [sigla]
  local env="$1" plant="${2:-}" pattern
  if [[ -n "$plant" ]]; then
    pattern="$(jq -r '.plantAdminPattern // "admin{plant}@solvacelabs.com"' "$CACHE")"
    printf '%s' "${pattern//\{plant\}/$(printf '%s' "$plant" | tr 'A-Z' 'a-z')}"
    return
  fi
  jq -r '.user // empty' <<<"$env"
}

# Sub-tarefa esperando a pessoa (POST WorkItems/tasks/{n}/wait): o que falta e o comando exato.
notify_wait() { # <sub-tarefa> <exit> <chave> <rotulo> <url> <usuario> <saida-do-start>
  local task="$1" rc="$2" key="$3" label="$4" url="$5" user="$6" out="$7" tok host msg detail code
  tok="$(token)"; [[ -n "$tok" ]] || return 0
  host="$( (hostname -s 2>/dev/null || hostname) | tr -d '\r')"
  case "$rc" in
    4) msg="**Falta a senha de \`$user\` em $label ($url) na máquina $host**, onde o executor roda — a senha fica só nela, nunca no PRMake." ;;
    3) detail="$(grep -o 'login recusado[^.]*' "$out" | head -1)"
       msg="**O login de \`$user\` em $label ($url) foi recusado** (${detail:-senha errada ou expirada}) na máquina $host. Regrave a senha (ou informe outro usuário na sub-tarefa)." ;;
    2) msg="**A máquina $host não conseguiu abrir o navegador da validação** (falta o Google Chrome ou o Playwright). Instale o Google Chrome (ou defina PRMAKE_CHROME com o caminho do navegador) e rode: \`bash ~/.claude/skills/validar-ambiente/scripts/env.sh setup\`." ;;
  esac
  if [[ "$rc" != 2 ]]; then
    msg+=$'\n\nNo **seu terminal** (fora do chat do Claude), rode e digite a senha quando pedir:\n\n```\n'"$CREDS_CMD $key $user"$'\n```'
  fi
  msg+=$'\n\nDepois clique em **Já resolvi** nesta sub-tarefa — a validação continua sozinha.'
  jq -n --arg r "$msg" '{result: $r}' > "$TMP/wait.json"
  code="$(curl -s --max-time 30 -o "$TMP/wait.resp" -w '%{http_code}' -X POST "$BASE/WorkItems/tasks/$task/wait" \
    -H "x-api-key: $tok" -H 'X-Execution-Client: skill' -H 'content-type: application/json' --data-binary "@$TMP/wait.json" 2>/dev/null)"
  if [[ "$code" =~ ^2 ]]; then
    echo "AVISADO NA FERRAMENTA: a sub-tarefa #$task ficou aguardando a pessoa com a orientação acima — encerre a vez (nao chame wait de novo)."
  else
    echo "nao consegui marcar a espera na sub-tarefa #$task (HTTP $code) — registre voce: prmake_work_task(wait) com a orientacao acima."
  fi
}

CMD="${1:-}"; shift || true
case "$CMD" in
  setup) setup; echo "ok — venv: $(venv_python)" ;;

  envs)
    load_envs "${1:-}"
    jq -r '.environments[] | "\(.key)\t\(.label)\t\(.url)\t\(.user // "-")\(if .readOnly then "\tSOMENTE LEITURA" else "" end)\(if .database.global then "\t" + (.database.host // "?") + "/" + .database.global else "" end)"' "$CACHE"
    echo "admin da planta: $(jq -r '.plantAdminPattern // "admin{plant}@solvacelabs.com"' "$CACHE") · todos os ambientes: banco corporativo ($(jq -r '.corporate.host // "corporate"' "$CACHE")/$(jq -r '.corporate.database // "CORPORATE_SOLVACE_DB"' "$CACHE"))"
    ;;

  resolve)
    [[ -n "${1:-}" ]] || die "uso: env.sh resolve <ambiente|url|cliente>"
    ENV="$(need_env "$1")" || exit 1
    jq -r '"ambiente: \(.key) — \(.label)", "url: \(.url)", "usuario padrao: \(.user // "(nenhum: use o admin da planta — env.sh plants/admin)")",
      "somente leitura: \(if .readOnly then "SIM — so navegar e consultar, nunca gravar" else "nao" end)",
      (if .database.global then "banco global: \(.database.host // "?")/\(.database.global)" else "banco global: (desconhecido — env.sh plants descobre)" end),
      (if .note then "atencao: \(.note)" else empty end), (if .source then "origem: banco corporativo (fora da configuracao)" else empty end)' <<<"$ENV"
    ;;

  plants)
    [[ -n "${1:-}" ]] || die "uso: env.sh plants <ambiente> [filtro]"
    ENV="$(need_env "$1")" || exit 1
    DB="$(env_db "$ENV")" || die "nao achei o banco global de $1 (sem credencial de banco nesta maquina? bash ~/.claude/skills/.prmake/prmake-skills.sh db-credentials)" 4
    F="$(printf '%s' "${2:-}" | tr -cd 'A-Za-z0-9 ._-')"
    bash "$SQL" --host "${DB%% *}" -d "${DB#* }" -q "SELECT SITE_ID, SITE_ACRONYM, SITE_NAME, INSTANCE_NAME, ACTIVE FROM TB_WCM_SITE WHERE ('$F' = '' OR SITE_NAME LIKE '%$F%' OR SITE_ACRONYM LIKE '%$F%') ORDER BY ACTIVE DESC, SITE_NAME"
    echo "admin da planta: $(default_user "$ENV" "<sigla>") (env.sh admin $1 <sigla>)"
    ;;

  admin|user)
    [[ -n "${1:-}" && -n "${2:-}" ]] || die "uso: env.sh admin <ambiente> <sigla> | env.sh user <ambiente> <email>"
    ENV="$(need_env "$1")" || exit 1
    if [[ "$CMD" == admin ]]; then EMAIL="$(default_user "$ENV" "$2")"; else EMAIL="$2"; fi
    EMAIL_SQL="$(printf '%s' "$EMAIL" | tr -cd 'A-Za-z0-9@._+-')"
    echo "usuario: $EMAIL"
    if DB="$(env_db "$ENV")"; then
      bash "$SQL" --host "${DB%% *}" -d "${DB#* }" -q "SELECT u.USER_EMAIL, u.ACTIVE, s.SITE_ACRONYM, s.SITE_NAME, s.INSTANCE_NAME FROM TB_WCM_USER u LEFT JOIN TB_WCM_SITE s ON s.SITE_ID = u.SITE_ID WHERE LOWER(u.USER_EMAIL) = LOWER('$EMAIL_SQL') OR LOWER(u.USER_USERNAME) = LOWER('$EMAIL_SQL')"
    else
      echo "(nao conferi no banco: banco global de $1 desconhecido ou sem credencial)"
    fi
    if has_password "$ENV" "$EMAIL"; then echo "senha nesta maquina: sim"; else echo "senha nesta maquina: NAO — a pessoa grava no PROPRIO terminal: $CREDS_CMD $(jq -r .key <<<"$ENV") $EMAIL"; fi
    ;;

  has-password)
    [[ -n "${1:-}" && -n "${2:-}" ]] || die "uso: env.sh has-password <ambiente> <email>"
    ENV="$(need_env "$1")" || exit 1
    if has_password "$ENV" "$2"; then echo "ok: ha senha de $2 para $(jq -r .key <<<"$ENV") nesta maquina"; exit 0; fi
    echo "SEM SENHA de $2 para $(jq -r .key <<<"$ENV") ($(jq -r .url <<<"$ENV")). A pessoa grava no PROPRIO terminal (fora do chat):"
    echo "  $CREDS_CMD $(jq -r .key <<<"$ENV") $2"
    exit 4
    ;;

  browser)
    SUB="${1:-}"; shift || true
    setup
    PY="$(venv_python)"
    if [[ "$SUB" == start ]]; then
      [[ -n "${1:-}" ]] || die "uso: env.sh browser start <ambiente> [--user <email>] [--plant <sigla>] [--card <card>] [--task <n>] [--headed]"
      ENV="$(need_env "$1")" || exit 1; shift
      USER_ARG=""; PLANT=""; CARD="${PRMAKE_CARD:-}"; TASK=""; HEADED=()
      while [[ $# -gt 0 ]]; do
        case "$1" in
          --user) USER_ARG="${2:?}"; shift 2 ;;
          --plant) PLANT="${2:?}"; shift 2 ;;
          --card) CARD="${2:?}"; shift 2 ;;
          --task) TASK="${2:?}"; TASK="${TASK#\#}"; shift 2 ;;
          --headed) HEADED=(--headed); shift ;;
          *) die "argumento desconhecido: $1" ;;
        esac
      done
      USER_ARG="${USER_ARG:-$(default_user "$ENV" "$PLANT")}"
      [[ -n "$USER_ARG" ]] || die "sem usuario para $(jq -r .key <<<"$ENV"): informe --user <email> ou --plant <sigla> (admin da planta)"
      KEY="$(jq -r .key <<<"$ENV")"
      OUT="$CARDS/${CARD:-sem-card}/validacao/${KEY}${TASK:+-$TASK}"
      RO=(); [[ "$(jq -r '.readOnly // false' <<<"$ENV")" == true ]] && RO=(--read-only)
      "$PY" "$DIR/env_browser.py" start --env "$KEY" --url "$(jq -r .url <<<"$ENV")" --user "$USER_ARG" --out "$OUT" \
        --label "$(jq -r .label <<<"$ENV")" ${RO[@]+"${RO[@]}"} ${HEADED[@]+"${HEADED[@]}"} | tee "$TMP/start.out"
      RC=${PIPESTATUS[0]}
      # A pessoa fica sabendo na ferramenta (Bancada/Execução: "aguardando você" + "Já resolvi"), sem depender do Claude.
      [[ $RC -eq 2 || $RC -eq 3 || $RC -eq 4 ]] && [[ -n "$TASK" ]] && notify_wait "$TASK" "$RC" "$KEY" "$(jq -r .label <<<"$ENV")" "$(jq -r .url <<<"$ENV")" "$USER_ARG" "$TMP/start.out"
      exit "$RC"
    fi
    exec "$PY" "$DIR/env_browser.py" "$SUB" "$@"
    ;;

  evidence)
    CARD="${1:?card}"; ITEM="${2:?numero do item}"; ITEM="${ITEM#\#}"; TASK="${3:?numero da sub-tarefa}"; TASK="${TASK#\#}"; SRC="${4:?pasta dos prints}"; shift 4
    REPORT=""
    while [[ $# -gt 0 ]]; do case "$1" in --report) REPORT="${2:?}"; shift 2 ;; *) die "argumento desconhecido: $1" ;; esac; done
    [[ -d "$SRC" ]] || die "pasta nao encontrada: $SRC"
    TOK="$(token)"; [[ -n "$TOK" ]] || die "sem token do PRMake (PRMAKE_TOKEN ou ~/.claude/prmake-token.txt)"
    N=0; FAIL=0
    while IFS= read -r f; do
      [[ -n "$f" ]] || continue
      name="$(basename "$f")"
      code="$(curl -s --max-time 120 -o "$TMP/up.json" -w '%{http_code}' -X POST "$BASE/WorkItems/$ITEM/files" \
        -H "x-api-key: $TOK" -H 'X-Execution-Client: skill' \
        -F "file=@$f" -F "name=$name" -F "kind=evidence" -F "taskNumber=$TASK" 2>/dev/null)"
      if [[ "$code" =~ ^2 ]]; then N=$((N + 1)); else FAIL=$((FAIL + 1)); echo "falhou $name: HTTP $code $(jq -r '.error // .message // empty' "$TMP/up.json" 2>/dev/null | head -c 300)"; fi
    done < <(find "$SRC" -maxdepth 1 -type f \( -name '[0-9][0-9]-*.jpg' -o -name '[0-9][0-9]-*.png' -o -name '[0-9][0-9]-*.jpeg' \) | sort)
    if [[ -n "$REPORT" ]]; then
      [[ -f "$REPORT" ]] || die "relatorio nao encontrado: $REPORT"
      code="$(curl -s --max-time 60 -o "$TMP/up.json" -w '%{http_code}' -X POST "$BASE/WorkItems/$ITEM/files" \
        -H "x-api-key: $TOK" -H 'X-Execution-Client: skill' \
        -F "file=@$REPORT;type=text/markdown" -F "name=$(basename "$REPORT")" -F "kind=evidence" -F "taskNumber=$TASK" 2>/dev/null)"
      [[ "$code" =~ ^2 ]] && echo "relatorio $(basename "$REPORT") enviado" || { FAIL=$((FAIL + 1)); echo "falhou o relatorio: HTTP $code"; }
    fi
    echo "$N print(s) enviados como evidencia da sub-tarefa #$TASK (item #$ITEM)$([[ $FAIL -gt 0 ]] && echo " — $FAIL falha(s)")"
    [[ $FAIL -eq 0 ]] || exit 1
    ;;

  *) sed -n '2,20p' "$0"; exit 1 ;;
esac
