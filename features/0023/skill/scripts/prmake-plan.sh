#!/usr/bin/env bash
# Plano de execucao no PRMake (feature 0023): a skill registra o plano do card, manda o andamento em
# pedacos, envia os arquivos e respeita pausar/continuar/cancelar feitos na tela do card.
#
# Uso: prmake-plan.sh <comando> <card> [args...]
#   start       <card> [titulo] [etapas.json|-] [--new]  cria o plano ou RETOMA o plano aberto do card
#   steps       <card> <etapas.json|->                   define/refina as etapas (upsert pela key, na ordem)
#   step        <card> <key> <status> [motivo]           status da etapa: running|completed|failed|cancelled|pending
#   activity    <card> <key> <texto>                     o que esta fazendo agora na etapa (uma linha)
#   log         <card> <key|-> [kind] [mensagem|STDIN]   pedaco de andamento (kind: info|progress|finding|decision|warning|error)
#   checkpoint  <card> <key> [texto|STDIN]               onde parou (para retomar a etapa)
#   upload      <card> <arquivo> [kind] [key] [descricao] envia um arquivo (kind: script|analysis|data|image|attachment)
#   sync        <card> [key]                             envia o que mudou em scripts/ analises/ dados/ imagens/ anexos/
#   status      <card> <running|completed|failed|paused|cancelled> [motivo] [resumo.md]
#   control     <card>                                   heartbeat; exit 0 = seguir, 10 = pausado, 11 = parar
#   wait        <card> [segundos=540]                    espera sair da pausa; exit 0 = continuar, 10 = ainda pausado, 11 = parar
#   resume-info <card>                                   etapas, checkpoints e arquivos do plano atual
#   pull        <card>                                   baixa os arquivos do plano para a pasta do card
#   flush       <card>                                   reenvia a fila local (envios que falharam)
#
# Etapas (JSON): [{"key":"investigar-codigo","title":"Investigar o codigo","description":"..."}]
# Estado local: $CARDS_DIR/<card>/.prmake-plan.json (id do plano) e .prmake-outbox.jsonl (fila).
# Nunca derruba a skill por falha de rede: o envio vai para a fila e e reenviado na proxima chamada.
# Env: PRMAKE_TOKEN (token), PRMAKE_API_BASE (default https://api.softhouse.app.br/api/v1), CARDS_DIR.
set -uo pipefail

BASE="${PRMAKE_API_BASE:-https://api.softhouse.app.br/api/v1}"
CARDS_ROOT="${CARDS_DIR:-$HOME/.claude/cards}"
CMD="${1:-}"; shift || true
CARD="${1:-}"; shift || true

die() { echo "ERRO: $*" >&2; exit 1; }
warn() { echo "AVISO: $*" >&2; }

[[ -n "$CMD" && -n "$CARD" ]] || die "uso: prmake-plan.sh <comando> <card> [args] (veja o cabecalho do script)"
command -v jq >/dev/null || die "jq nao encontrado"

CARD_DIR="$CARDS_ROOT/$CARD"
mkdir -p "$CARD_DIR"
STATE="$CARD_DIR/.prmake-plan.json"
OUTBOX="$CARD_DIR/.prmake-outbox.jsonl"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/prmake-plan.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT

resolve_token() {
  if [[ -n "${PRMAKE_TOKEN:-}" ]]; then printf '%s' "$PRMAKE_TOKEN"; return; fi
  local f
  for f in "$HOME/.claude/prmake-token.txt" "${CLAUDE_PROJECT_DIR:-}/.claude/prmake-token.txt" \
           "$(git rev-parse --show-toplevel 2>/dev/null)/.claude/prmake-token.txt"; do
    [[ -f "$f" ]] && { tr -d '\n' < "$f"; return; }
  done
  die "token nao encontrado (defina PRMAKE_TOKEN ou crie ~/.claude/prmake-token.txt)"
}
TOKEN="$(resolve_token)"

new_id() { uuidgen 2>/dev/null | tr 'A-Z' 'a-z' || date +%s%N; }
sha256() { if command -v shasum >/dev/null; then shasum -a 256 "$1" | cut -d' ' -f1; else sha256sum "$1" | cut -d' ' -f1; fi; }

plan_id() { [[ -f "$STATE" ]] && jq -r '.planId // empty' "$STATE"; }
# Plano indisponivel (API sem o recurso ou fora do ar no start): os comandos viram no-op e a skill segue.
require_plan() {
  if [[ -f "$STATE" ]] && jq -e '.offline == true' "$STATE" >/dev/null 2>&1; then
    [[ "$CMD" == "control" ]] && echo "continue"
    echo "(plano de execucao indisponivel neste card — ignorado; rode 'start' de novo para tentar)" >&2
    exit 0
  fi
  PLAN="$(plan_id)"; [[ -n "$PLAN" ]] || die "nenhum plano para o card $CARD (rode: prmake-plan.sh start $CARD)"
}
go_offline() { # <motivo>
  jq -n --arg card "$CARD" --arg why "$1" '{planId:null, card:$card, offline:true, reason:$why}' > "$STATE"
  echo "PLANO INDISPONIVEL ($1) — a analise segue sem o plano de execucao; os comandos do plano serao ignorados."
  exit 0
}

# --- HTTP ------------------------------------------------------------------------------------------
# api <METHOD> <path> [json-body-file] -> corpo em $TMP/resp, retorna o HTTP code em $CODE.
# 3 tentativas com espera (rede/5xx/409). Code 000 = sem conexao.
api() {
  local method="$1" path="$2" body="${3:-}" attempt
  local args=(-s --max-time 60 -o "$TMP/resp" -w '%{http_code}' -X "$method" "$BASE/ExecutionPlan$path"
              -H "x-api-key: $TOKEN" -H 'accept: application/json' -H 'X-Execution-Client: skill')
  [[ -n "$body" ]] && args+=(-H 'content-type: application/json' --data-binary "@$body")
  for attempt in 1 2 3; do
    CODE="$(curl "${args[@]}" 2>/dev/null)"; CODE="${CODE:-000}"
    [[ "$CODE" =~ ^(000|5..|409|429)$ ]] || return 0
    [[ $attempt -lt 3 ]] && sleep $((attempt * 2))
  done
  return 0
}

api_upload() { # <path> <file> <kind> <stepKey> <description>
  local path="$1" file="$2" kind="$3" key="$4" desc="$5" attempt
  local args=(-s --max-time 120 -o "$TMP/resp" -w '%{http_code}' -X POST "$BASE/ExecutionPlan$path"
              -H "x-api-key: $TOKEN" -H 'X-Execution-Client: skill' -F "file=@$file")
  [[ -n "$kind" ]] && args+=(-F "kind=$kind")
  [[ -n "$key" ]] && args+=(-F "stepKey=$key")
  [[ -n "$desc" ]] && args+=(-F "description=$desc")
  for attempt in 1 2 3; do
    CODE="$(curl "${args[@]}" 2>/dev/null)"; CODE="${CODE:-000}"
    [[ "$CODE" =~ ^(000|5..|409|429)$ ]] || return 0
    [[ $attempt -lt 3 ]] && sleep $((attempt * 2))
  done
  return 0
}

is_transient() { [[ "$CODE" =~ ^(000|5..|409|429)$ ]]; }
resp_error() { jq -r '.error // .message // .' "$TMP/resp" 2>/dev/null | head -c 500; }

# Envio que nao pode se perder: se falhar por rede/servidor, vai para a fila local.
send_or_queue() { # <METHOD> <path> <json-body-file>
  local method="$1" path="$2" body="$3"
  flush_quiet
  api "$method" "$path" "$body"
  if [[ "$CODE" =~ ^2 ]]; then return 0; fi
  if is_transient; then
    jq -cn --arg m "$method" --arg p "$path" --slurpfile b "$body" '{m:$m,p:$p,b:$b[0]}' >> "$OUTBOX"
    warn "PRMake indisponivel (HTTP $CODE): envio guardado na fila local ($OUTBOX)"
    return 0
  fi
  die "HTTP $CODE em $method $path: $(resp_error)"
}

upload_or_queue() { # <file> <kind> <key> <desc>
  local file="$1" kind="$2" key="$3" desc="$4"
  flush_quiet
  api_upload "/$PLAN/artifacts" "$file" "$kind" "$key" "$desc"
  if [[ "$CODE" =~ ^2 ]]; then echo "   enviado: $(basename "$file") ($(jq -r '.kind' "$TMP/resp"), $(jq -r '.size' "$TMP/resp") bytes)"; return 0; fi
  if is_transient; then
    jq -cn --arg p "/$PLAN/artifacts" --arg f "$file" --arg k "$kind" --arg s "$key" --arg d "$desc" \
      '{m:"UPLOAD",p:$p,f:$f,kind:$k,stepKey:$s,description:$d}' >> "$OUTBOX"
    warn "PRMake indisponivel (HTTP $CODE): arquivo $(basename "$file") guardado na fila local"
    return 0
  fi
  die "HTTP $CODE ao enviar $(basename "$file"): $(resp_error)"
}

# Reenvia a fila na ordem; para no primeiro que ainda falhar (preserva a ordem dos pedacos).
flush_outbox() {
  [[ -s "$OUTBOX" ]] || return 0
  local total sent=0 line m p
  total=$(wc -l < "$OUTBOX" | tr -d ' ')
  : > "$TMP/rest"
  local failed=0
  while IFS= read -r line; do
    [[ -z "$line" ]] && continue
    if [[ $failed -eq 1 ]]; then echo "$line" >> "$TMP/rest"; continue; fi
    m=$(jq -r '.m' <<<"$line"); p=$(jq -r '.p' <<<"$line")
    if [[ "$m" == "UPLOAD" ]]; then
      local f; f=$(jq -r '.f' <<<"$line")
      [[ -f "$f" ]] || { sent=$((sent + 1)); continue; }
      api_upload "$p" "$f" "$(jq -r '.kind' <<<"$line")" "$(jq -r '.stepKey' <<<"$line")" "$(jq -r '.description' <<<"$line")"
    else
      # Arquivo proprio: o corpo do envio atual ($TMP/body) nao pode ser sobrescrito pela fila.
      jq -c '.b' <<<"$line" > "$TMP/queued-body"
      api "$m" "$p" "$TMP/queued-body"
    fi
    if [[ "$CODE" =~ ^2 ]] || ! is_transient; then
      [[ "$CODE" =~ ^2 ]] || warn "descartado da fila (HTTP $CODE): $m $p — $(resp_error)"
      sent=$((sent + 1))
    else
      failed=1; echo "$line" >> "$TMP/rest"
    fi
  done < "$OUTBOX"
  mv "$TMP/rest" "$OUTBOX"
  [[ -s "$OUTBOX" ]] || rm -f "$OUTBOX"
  [[ $sent -gt 0 ]] && echo "   fila local: $sent de $total reenviado(s)" >&2
  return 0
}
flush_quiet() { [[ -s "$OUTBOX" ]] && flush_outbox; return 0; }

default_steps() {
  cat <<'EOF'
[
 {"key":"identificar-card","title":"Identificar o card","description":"Descobrir o numero do card (branch hotfix/bugfix ou informado) e criar a pasta de artefatos."},
 {"key":"coletar-dados","title":"Ler o card","description":"Buscar titulo, estado, tipo e repro steps do card no PRMake/Azure DevOps."},
 {"key":"investigar-codigo","title":"Investigar o codigo","description":"Localizar telas, endpoints, servicos e queries envolvidos (legado edv-solvace ou revamp) e reconstruir o fluxo ate o erro."},
 {"key":"consultar-ambiente","title":"Consultar dados e ambiente","description":"Quando necessario: Cognito (usuario/ambiente) e SQL Server somente leitura."},
 {"key":"causa-raiz","title":"Levantar a causa raiz","description":"Hipoteses priorizadas e pontos suspeitos (caminho:linha); o que e confirmado vs. hipotese."},
 {"key":"montar-analise","title":"Montar a analise e os scripts","description":"Escrever a analise em markdown e, se houver, os scripts (ex.: SQL de correcao e rollback)."},
 {"key":"publicar","title":"Publicar na timeline","description":"Postar a analise completa na Timeline do card."}
]
EOF
}

read_steps_arg() { # <arg> -> json array em $TMP/steps
  local src="${1:-}"
  if [[ -z "$src" ]]; then default_steps > "$TMP/steps"
  elif [[ "$src" == "-" ]]; then cat > "$TMP/steps"
  else [[ -f "$src" ]] || die "arquivo de etapas nao encontrado: $src"; cp "$src" "$TMP/steps"; fi
  jq -e 'type == "array" and length > 0' "$TMP/steps" >/dev/null || die "etapas devem ser um array JSON nao vazio"
}

print_plan() { # imprime o plano em $TMP/resp
  jq -r '"PLANO \(.id)  [\(.status)]  \(.title)",
         (.steps[] | "  [\(.status)] \(.key) — \(.title)" + (if .checkpoint then "\n      checkpoint: \(.checkpoint | gsub("\n"; " | "))" else "" end)
                                                          + (if .statusReason then "\n      motivo: \(.statusReason)" else "" end)),
         (if (.artifacts | length) > 0 then "  arquivos: " + ([.artifacts[] | "\(.kind)/\(.name)"] | join(", ")) else empty end)' "$TMP/resp"
}

case "$CMD" in
  start)
    TITLE=""; STEPS_ARG=""; FORCE_NEW=0
    for a in "$@"; do
      if [[ "$a" == "--new" ]]; then FORCE_NEW=1
      elif [[ -z "$TITLE" ]]; then TITLE="$a"
      else STEPS_ARG="$a"; fi
    done
    [[ -n "$TITLE" ]] || TITLE="Analise do bug $CARD"
    flush_quiet
    if [[ $FORCE_NEW -eq 0 ]]; then
      api GET "/card/$CARD/current"
      if [[ "$CODE" == "200" ]] && ! jq -e '.status == "completed" or .status == "cancelled"' "$TMP/resp" >/dev/null; then
        PLAN=$(jq -r '.id' "$TMP/resp")
        jq -n --arg id "$PLAN" --arg card "$CARD" '{planId:$id, card:$card}' > "$STATE"
        if ! jq -e '.status == "running"' "$TMP/resp" >/dev/null; then
          jq -n '{status:"running", reason:"Retomado pela skill"}' > "$TMP/body"
          api POST "/$PLAN/status" "$TMP/body"
        fi
        api GET "/$PLAN"
        echo "RETOMADO — continue da primeira etapa nao concluida, usando o checkpoint:"
        print_plan
        exit 0
      fi
      [[ "$CODE" == "000" ]] && go_offline "PRMake inacessivel"
      [[ "$CODE" =~ ^(404|405)$ ]] && go_offline "API sem o recurso de plano de execucao (HTTP $CODE)"
      [[ "$CODE" =~ ^(401|403)$ ]] && die "HTTP $CODE: token PRMake recusado/expirado (~/.claude/prmake-token.txt ou PRMAKE_TOKEN)"
      [[ "$CODE" =~ ^5 ]] && go_offline "PRMake com erro (HTTP $CODE)"
      [[ "$CODE" =~ ^(200|204)$ ]] || die "HTTP $CODE ao buscar o plano do card: $(resp_error)"
    fi
    read_steps_arg "$STEPS_ARG"
    jq -n --arg card "$CARD" --arg title "$TITLE" --slurpfile steps "$TMP/steps" \
      '{cardNumber:$card, kind:"analisar-bug", title:$title, steps:$steps[0]}' > "$TMP/body"
    api POST "" "$TMP/body"
    [[ "$CODE" == "000" ]] && go_offline "PRMake inacessivel"
    [[ "$CODE" =~ ^(404|405)$ ]] && go_offline "API sem o recurso de plano de execucao (HTTP $CODE)"
    [[ "$CODE" =~ ^2 ]] || die "HTTP $CODE ao criar o plano: $(resp_error)"
    PLAN=$(jq -r '.id' "$TMP/resp")
    jq -n --arg id "$PLAN" --arg card "$CARD" '{planId:$id, card:$card}' > "$STATE"
    rm -f "$OUTBOX"
    echo "NOVO PLANO criado:"
    print_plan
    ;;

  steps)
    require_plan; read_steps_arg "${1:--}"
    jq -n --slurpfile steps "$TMP/steps" '{steps:$steps[0]}' > "$TMP/body"
    send_or_queue PUT "/$PLAN/steps" "$TMP/body" && echo "OK etapas"
    ;;

  step)
    require_plan; KEY="${1:?key}"; ST="${2:?status}"; REASON="${3:-}"
    jq -n --arg s "$ST" --arg r "$REASON" '{status:$s} + (if $r != "" then {reason:$r} else {} end)' > "$TMP/body"
    send_or_queue PATCH "/$PLAN/steps/$KEY" "$TMP/body" && echo "OK $KEY -> $ST"
    ;;

  activity)
    require_plan; KEY="${1:?key}"; TEXT="${2:?texto}"
    jq -n --arg a "$TEXT" '{activity:$a}' > "$TMP/body"
    send_or_queue PATCH "/$PLAN/steps/$KEY" "$TMP/body" && echo "OK"
    ;;

  log)
    require_plan; KEY="${1:--}"; KIND="${2:-info}"; MSG="${3:-}"
    [[ -n "$MSG" ]] || MSG="$(cat)"
    [[ -n "$MSG" ]] || die "mensagem vazia"
    [[ "$KEY" == "-" ]] && KEY=""
    # Mensagens longas viram varios pedacos (limite da API: 20 000 caracteres por registro).
    printf '%s' "$MSG" > "$TMP/msg"
    jq -Rs --arg k "$KEY" --arg kind "$KIND" --arg cid "$(new_id)" '
      [ . as $m | range(0; ($m | length); 18000) as $i
        | {clientId: "\($cid)-\($i)", kind: $kind, message: $m[$i:$i+18000]}
        + (if $k != "" then {stepKey: $k} else {} end) ] | {logs: .}' "$TMP/msg" > "$TMP/body"
    send_or_queue POST "/$PLAN/logs" "$TMP/body" && echo "OK log"
    ;;

  checkpoint)
    require_plan; KEY="${1:?key}"; TEXT="${2:-}"
    [[ -n "$TEXT" ]] || TEXT="$(cat)"
    jq -n --arg c "$TEXT" '{checkpoint:$c}' > "$TMP/body"
    send_or_queue PATCH "/$PLAN/steps/$KEY" "$TMP/body" && echo "OK checkpoint $KEY"
    ;;

  upload)
    require_plan; FILE="${1:?arquivo}"; [[ -f "$FILE" ]] || die "arquivo nao encontrado: $FILE"
    FILE="$(cd "$(dirname "$FILE")" && pwd)/$(basename "$FILE")"
    upload_or_queue "$FILE" "${2:-}" "${3:-}" "${4:-}"
    ;;

  sync)
    require_plan; KEY="${1:-}"
    flush_quiet
    api GET "/$PLAN"
    [[ "$CODE" == "200" ]] && jq -r '.artifacts[] | "\(.kind)/\(.name) \(.sha256)"' "$TMP/resp" > "$TMP/remote" || : > "$TMP/remote"
    count=0
    for pair in "scripts:script" "analises:analysis" "dados:data" "imagens:image" "anexos:attachment"; do
      dir="$CARD_DIR/${pair%%:*}"; kind="${pair##*:}"
      [[ -d "$dir" ]] || continue
      while IFS= read -r -d '' f; do
        name="$(basename "$f")"
        # Imagem dentro de outra pasta continua sendo imagem (a tela mostra a previa).
        k="$kind"; case "$(printf '%s' "${name##*.}" | tr 'A-Z' 'a-z')" in png|jpg|jpeg|gif|webp|bmp|svg) k="image";; esac
        remote_sha=$(awk -v n="$k/$name" '$1 == n {print $2}' "$TMP/remote")
        [[ "$remote_sha" == "$(sha256 "$f")" ]] && continue
        upload_or_queue "$f" "$k" "$KEY" ""
        count=$((count + 1))
      done < <(find "$dir" -maxdepth 1 -type f ! -name '.*' -print0 | sort -z)
    done
    echo "OK sync ($count arquivo(s) enviado(s))"
    ;;

  status)
    require_plan; ST="${1:?status}"; REASON="${2:-}"; SUMMARY_FILE="${3:-}"
    if [[ -n "$SUMMARY_FILE" && -f "$SUMMARY_FILE" ]]; then
      jq -n --arg s "$ST" --arg r "$REASON" --rawfile sum "$SUMMARY_FILE" \
        '{status:$s, summary:$sum} + (if $r != "" then {reason:$r} else {} end)' > "$TMP/body"
    else
      jq -n --arg s "$ST" --arg r "$REASON" '{status:$s} + (if $r != "" then {reason:$r} else {} end)' > "$TMP/body"
    fi
    send_or_queue POST "/$PLAN/status" "$TMP/body" && echo "OK plano -> $ST"
    ;;

  control)
    require_plan; flush_quiet
    api POST "/$PLAN/control"
    if [[ ! "$CODE" =~ ^2 ]]; then warn "sem resposta do PRMake (HTTP $CODE) — seguindo"; echo "continue"; exit 0; fi
    ACTION=$(jq -r '.action' "$TMP/resp")
    CANCELLED=$(jq -r '.cancelledSteps | join(",")' "$TMP/resp")
    echo "$ACTION"
    [[ -n "$CANCELLED" ]] && echo "etapas canceladas (pular): $CANCELLED"
    case "$ACTION" in
      wait) echo "PAUSADO por $(jq -r '.statusChangedBy // "?"' "$TMP/resp")$(jq -r 'if .statusReason then ": " + .statusReason else "" end' "$TMP/resp") — rode: prmake-plan.sh wait $CARD"; exit 10;;
      stop) echo "PARAR: plano $(jq -r '.status' "$TMP/resp") por $(jq -r '.statusChangedBy // "?"' "$TMP/resp")"; exit 11;;
    esac
    exit 0
    ;;

  wait)
    require_plan; MAX="${1:-540}"; waited=0; delay=5
    while [[ $waited -lt $MAX ]]; do
      api POST "/$PLAN/control"
      if [[ "$CODE" =~ ^2 ]]; then
        ACTION=$(jq -r '.action' "$TMP/resp")
        case "$ACTION" in
          continue) echo "continue — retomado por $(jq -r '.statusChangedBy // "?"' "$TMP/resp")"
                    C=$(jq -r '.cancelledSteps | join(",")' "$TMP/resp"); [[ -n "$C" ]] && echo "etapas canceladas (pular): $C"
                    exit 0;;
          stop) echo "stop — plano $(jq -r '.status' "$TMP/resp")"; exit 11;;
        esac
      fi
      sleep "$delay"; waited=$((waited + delay)); [[ $delay -lt 15 ]] && delay=$((delay + 5))
    done
    echo "ainda pausado apos ${MAX}s — rode 'prmake-plan.sh wait $CARD' de novo ou encerre e retome depois com /analisar-bug $CARD"
    exit 10
    ;;

  resume-info)
    require_plan; flush_quiet
    api GET "/$PLAN"
    [[ "$CODE" == "200" ]] || die "HTTP $CODE: $(resp_error)"
    print_plan
    ;;

  pull)
    require_plan
    api GET "/$PLAN"
    [[ "$CODE" == "200" ]] || die "HTTP $CODE: $(resp_error)"
    jq -r '.artifacts[] | "\(.id)\t\(.kind)\t\(.name)\t\(.sha256)"' "$TMP/resp" > "$TMP/list"
    while IFS=$'\t' read -r aid kind name sha; do
      case "$kind" in script) d=scripts;; analysis) d=analises;; data) d=dados;; image) d=imagens;; *) d=anexos;; esac
      mkdir -p "$CARD_DIR/$d"; target="$CARD_DIR/$d/$name"
      [[ -f "$target" && "$(sha256 "$target")" == "$sha" ]] && continue
      curl -s --max-time 120 -H "x-api-key: $TOKEN" -o "$target" "$BASE/ExecutionPlan/$PLAN/artifacts/$aid/content?download=true" \
        && echo "   baixado: $d/$name"
    done < "$TMP/list"
    echo "OK pull"
    ;;

  flush)
    require_plan; flush_outbox
    [[ -s "$OUTBOX" ]] && { echo "ainda na fila: $(wc -l < "$OUTBOX" | tr -d ' ')"; exit 1; }
    echo "OK fila vazia"
    ;;

  *) die "comando desconhecido: $CMD" ;;
esac
