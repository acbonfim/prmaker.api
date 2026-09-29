#!/usr/bin/env bash
# Plano de execucao no PRMake (feature 0023): a skill registra o plano do card, manda o andamento em
# pedacos, envia os arquivos e respeita pausar/continuar/cancelar feitos na tela do card.
#
# Uso: prmake-plan.sh <comando> <card> [args...]
#   start       <card> [titulo] [etapas.json|-] [--new]  cria o plano de ANALISE ou RETOMA o plano aberto do card
#   correction  <card> <titulo> <etapas.json|->           cria o plano de CORRECAO ligado a analise (passa a ser o ativo)
#   use         <card> analysis|correction                escolhe em qual plano os comandos agem
#   steps       <card> <etapas.json|->                   define/refina as etapas (upsert pela key, na ordem)
#   step        <card> <key> <status> [motivo]           status da etapa: running|completed|failed|cancelled|pending
#   activity    <card> <key> <texto>                     o que esta fazendo agora na etapa (uma linha)
#   log         <card> <key|-> [kind] [mensagem|STDIN]   pedaco de andamento (kind: info|progress|finding|decision|warning|error)
#   checkpoint  <card> <key> [texto|STDIN]               onde parou (para retomar a etapa)
#   upload      <card> <arquivo> [kind] [key] [descricao] envia um arquivo (kind: script|analysis|data|image|attachment)
#   sync        <card> [key]                             envia o que mudou em scripts/ analises/ dados/ imagens/ anexos/
#   status      <card> <running|completed|failed|paused|cancelled> [motivo] [resumo.md]
#   ask         <card> <perguntas.json|->                pergunta ao usuario (responde no PRMake ou aqui)
#   answers     <card>                                   perguntas e respostas do plano
#   wait-answers <card> [segundos=540]                   espera as respostas; exit 0 = respondidas, 10 = ainda nao, 11 = parar
#   answer      <card> <n|id> [texto|STDIN]              grava a resposta dada no terminal (via claude)
#   link        <card> <key> <url> [titulo] [--blocks] [--kind ticket|pr|doc|other]
#   pr-text     <card> <repo> <branch>                   prompt configurado (layout padrao do PRMake) + diff da branch
#                                                        em $CARD_DIR/pr/<repo>/ (via gerar-prmake/prmake-fetch.sh)
#   save-pr-text <card> <descricao.md> [rca.md] [key]    salva descricao e root cause no card do PRMake (reuso no
#                                                        "Abrir PR" e na gerar-prmake) e guarda os arquivos no plano
#   devops      <card> <acao> [arquivo]                  fechamento do card no DevOps (via PRMake), qualquer tratamento:
#                 config · rootcause <rca.md> · summary <resumo-pt-en.md> · classifications · classify <opcao> ·
#                 zero-remaining · ready-for-qa · test-in-production · initial-estimate
#                 (tudo gravado pelo PRMake; estados, areas, estimativa, prompt do resumo e opcoes de
#                 classificacao vem da configuracao do usuario no PRMake — `config`/`classifications`)
#   settings    <card>                                   configuracao das skills no PRMake (Skills Configurations +
#                                                        prompts + campos do DevOps): salva em $CARD_DIR/.prmake-settings.json
#   branches    <card> <repo> [--flow producao|release] [--base <branch>]
#                                                        plano de branches/PRs do repositorio conforme a configuracao
#                                                        (fluxo pela area do card, base, cherry-picks, titulos, comandos);
#                                                        exit 3 = precisa perguntar (fluxo ou base) ao usuario
#   open-pr     <card> <repo> <branch> <destino> [titulo] [descricao.md]
#                                                        abre o PR pelo PRMake (registra no card) — NUNCA faz merge
#   control     <card>                                   heartbeat; exit 0 = seguir, 10 = pausado, 11 = parar
#   wait        <card> [segundos=540]                    espera sair da pausa; exit 0 = continuar, 10 = ainda pausado, 11 = parar
#   watch       <card> [segundos=28800]                  VIGIA (rode em segundo plano): termina quando algo muda no PRMake
#                                                        (etapa/chamado/PR/resposta/pausa/conclusao); exit 0 = mudou,
#                                                        11 = plano cancelado/concluido, 10 = nada mudou no prazo
#   resume-info <card>                                   etapas, checkpoints e arquivos do plano atual
#   pull        <card>                                   baixa os arquivos do plano para a pasta do card
#   flush       <card>                                   reenvia a fila local (envios que falharam)
#
# Etapas (JSON): [{"key":"investigar-codigo","title":"Investigar o codigo","description":"...",
#                  "executor":"claude|user","kind":"task|code|pr|ticket|question|validation",
#                  "repository":"edv-solvace","dependsOn":["outra-key"]}]
# Perguntas (JSON): [{"stepKey":"propor-solucoes","text":"...","options":[{"label":"A","description":"...","recommended":true}],"allowFreeText":true}]
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
state_get() { [[ -f "$STATE" ]] && jq -r --arg k "$1" '.[$k] // empty' "$STATE"; }
# Grava o estado local: plano ativo + os dois planos (analise/correcao).
save_state() { # <ativo> <analise> <correcao>
  jq -n --arg id "$1" --arg a "${2:-}" --arg c "${3:-}" --arg card "$CARD" \
    '{planId:$id, card:$card} + (if $a != "" then {analysisPlanId:$a} else {} end) + (if $c != "" then {correctionPlanId:$c} else {} end)' > "$STATE"
}
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

# Configuracao das skills (0030): GET Skills/config — regras de branch, padroes, prompts e campos do DevOps.
# Tudo o que pode mudar fica no PRMake (tela de plugins), nunca fixo na skill.
SETTINGS="$CARD_DIR/.prmake-settings.json"
load_settings() {
  local code
  code="$(curl -s --max-time 30 -o "$TMP/settings" -w '%{http_code}' -H "x-api-key: $TOKEN" "$BASE/Skills/config" 2>/dev/null)"
  if [[ "$code" =~ ^2 ]] && jq -e '.available' "$TMP/settings" >/dev/null 2>&1; then
    cp "$TMP/settings" "$SETTINGS"
  elif [[ -s "$SETTINGS" ]]; then
    warn "PRMake indisponivel (HTTP ${code:-000}): usando a ultima configuracao salva ($SETTINGS)"
  else
    die "configuracao das skills indisponivel (HTTP ${code:-000}; plugin 'Skills Configurations' ausente?) — avise o usuario"
  fi
}
setting() { jq -r --arg k "$1" '.settings[$k] // empty | if type == "string" then . else tojson end' "$SETTINGS"; }
# Preenche {card}, {summary}, {target}, {TARGET} num padrao.
fill_pattern() { # <padrao> [target] [summary]
  local t="${2:-}" up
  up="$(printf '%s' "$t" | tr '[:lower:]' '[:upper:]')"
  printf '%s' "$1" | sed -e "s|{card}|$CARD|g" -e "s|{TARGET}|$up|g" -e "s|{target}|$t|g" -e "s|{summary}|${3:-<resumo>}|g"
}

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
  # Titulos/descricoes aparecem na tela do card no PRMake (por isso com acentuacao).
  cat <<'EOF'
[
 {"key":"identificar-card","title":"Identificar o card","description":"Descobrir o número do card (branch hotfix/bugfix ou informado) e criar a pasta de artefatos."},
 {"key":"coletar-dados","title":"Ler o card","description":"Buscar título, estado, tipo e repro steps do card no PRMake/Azure DevOps."},
 {"key":"investigar-codigo","title":"Investigar o código","description":"Localizar telas, endpoints, serviços e queries envolvidos (legado edv-solvace ou revamp) e reconstruir o fluxo até o erro."},
 {"key":"consultar-ambiente","title":"Consultar dados e ambiente","description":"Quando necessário: Cognito (usuário/ambiente) e SQL Server somente leitura."},
 {"key":"causa-raiz","title":"Levantar a causa raiz","description":"Hipóteses priorizadas e pontos suspeitos (caminho:linha); o que é confirmado e o que é hipótese."},
 {"key":"montar-analise","title":"Montar a análise e os scripts","description":"Escrever a análise em markdown e, se houver, os scripts (ex.: SQL de correção e rollback)."},
 {"key":"publicar","title":"Publicar na timeline","description":"Postar a análise completa na Timeline do card."},
 {"key":"propor-solucoes","title":"Propor soluções e decidir com você","kind":"question","description":"Apresentar as opções de solução (prós, contras, riscos) e perguntar o que for preciso — responda no PRMake ou no Claude. Com as respostas, nasce o plano de correção."}
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
  jq -r '"PLANO \(.id)  [\(.status)]  (\(.phase // "analysis"))  \(.title)",
         (.steps[] | "  [\(.status)] \(.key) — \(.title)"
            + (if (.executor // "claude") == "user" then "  (usuario)" else "" end)
            + (if .repository then "  repo=\(.repository)" else "" end)
            + (if ((.dependsOn // []) | length) > 0 then "  depende de: \(.dependsOn | join(","))" else "" end)
            + (if .checkpoint then "\n      checkpoint: \(.checkpoint | gsub("\n"; " | "))" else "" end)
                                                          + (if .statusReason then "\n      motivo: \(.statusReason)" else "" end)),
         (if (.artifacts | length) > 0 then "  arquivos: " + ([.artifacts[] | "\(.kind)/\(.name)"] | join(", ")) else empty end),
         (if ((.questions // []) | length) > 0 then "  perguntas: " + ([.questions[] | "[\(.status)] \(.text | .[0:60])" + (if .answer then " => \(.answer | .[0:60])" else "" end)] | join(" | ")) else empty end),
         (if ((.links // []) | length) > 0 then "  links: " + ([.links[] | "\(.stepKey): \(.title // .url) [\(.status // "-")]"] | join(" | ")) else empty end)' "$TMP/resp"
}

print_questions() { # perguntas do plano em $TMP/resp, numeradas
  jq -r '.questions // [] | to_entries[] | "\(.key + 1). [\(.value.status)] \(.value.text)"
      + (if (.value.options | length) > 0 then "\n   opcoes: " + ([.value.options[] | .label + (if .recommended then " (recomendada)" else "" end)] | join(" / ")) else "" end)
      + (if .value.answer then "\n   RESPOSTA (\(.value.answeredVia), \(.value.answeredBy)): \(.value.answer)" else "" end)
      + "\n   id: \(.value.id)"' "$TMP/resp"
}

case "$CMD" in
  start)
    TITLE=""; STEPS_ARG=""; FORCE_NEW=0
    for a in "$@"; do
      if [[ "$a" == "--new" ]]; then FORCE_NEW=1
      elif [[ -z "$TITLE" ]]; then TITLE="$a"
      else STEPS_ARG="$a"; fi
    done
    [[ -n "$TITLE" ]] || TITLE="Análise do bug $CARD"
    flush_quiet
    if [[ $FORCE_NEW -eq 0 ]]; then
      api GET "/card/$CARD/current"
      if [[ "$CODE" == "200" ]] && ! jq -e '.status == "completed" or .status == "cancelled"' "$TMP/resp" >/dev/null; then
        PLAN=$(jq -r '.id' "$TMP/resp")
        if jq -e '.phase == "correction"' "$TMP/resp" >/dev/null; then
          save_state "$PLAN" "$(jq -r '.parentPlanId' "$TMP/resp")" "$PLAN"
        else
          save_state "$PLAN" "$PLAN" ""
        fi
        if ! jq -e '.status == "running"' "$TMP/resp" >/dev/null; then
          jq -n '{status:"running", reason:"Retomado pela skill"}' > "$TMP/body"
          api POST "/$PLAN/status" "$TMP/body"
        fi
        api GET "/$PLAN"
        echo "RETOMADO — continue da primeira etapa nao concluida, usando o checkpoint:"
        print_plan
        exit 0
      fi
      # Analise ja concluida e ainda sem plano de correcao (inclusive a feita na versao anterior da skill,
      # que parava em "publicar"): continua dela — nao refaz a analise.
      if [[ "$CODE" == "200" ]] && jq -e '.phase == "analysis" and .status == "completed"' "$TMP/resp" >/dev/null; then
        PLAN=$(jq -r '.id' "$TMP/resp")
        save_state "$PLAN" "$PLAN" ""
        if ! jq -e '[.steps[] | select(.key == "propor-solucoes")] | length > 0' "$TMP/resp" >/dev/null; then
          default_steps | jq '[.[] | select(.key == "propor-solucoes")]' > "$TMP/new-step.json"
          jq --slurpfile extra "$TMP/new-step.json" '{steps: ([.steps | sort_by(.order)[] | {key, title}] + $extra[0])}' "$TMP/resp" > "$TMP/body"
          api PUT "/$PLAN/steps" "$TMP/body"
          [[ "$CODE" =~ ^2 ]] || die "HTTP $CODE ao acrescentar a etapa propor-solucoes: $(resp_error)"
          echo "ANALISE CONCLUIDA NA VERSAO ANTERIOR DA SKILL — acrescentei a etapa propor-solucoes."
          echo "Continue pelo passo 6 (propor solucoes e perguntar) usando a analise ja publicada: $CARD_DIR/analises/analise-inicial.md"
          echo "(pasta do card vazia? rode: prmake-plan.sh pull $CARD). Para refazer a analise do zero: start $CARD --new"
        else
          echo "ANALISE CONCLUIDA — ainda sem plano de correcao. Continue pelo passo 6 (perguntas sem resposta?) ou pelo passo 7"
          echo "(montar o plano de correcao com as respostas: prmake-plan.sh answers $CARD). Para refazer do zero: start $CARD --new"
        fi
        api GET "/$PLAN"
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
    save_state "$PLAN" "$PLAN" ""
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
    READY=$(jq -r '(.readySteps // []) | join(",")' "$TMP/resp"); [[ -n "$READY" ]] && echo "prontas para comecar: $READY"
    WAITING=$(jq -r '(.waitingSteps // []) | join(",")' "$TMP/resp"); [[ -n "$WAITING" ]] && echo "aguardando (nao mexer): $WAITING"
    OPENQ=$(jq -r '.openQuestions // 0' "$TMP/resp"); [[ "$OPENQ" != "0" ]] && echo "perguntas sem resposta: $OPENQ (rode: prmake-plan.sh wait-answers $CARD)"
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

  watch)
    # O PRMake nao consegue chamar esta sessao: quem acorda o Claude e este comando, rodando em segundo plano.
    # Cada consulta ao control tambem sincroniza os PRs com o GitHub. Termina quando o estado muda.
    require_plan; MAX="${1:-28800}"; waited=0
    fingerprint() { jq -c '{s:.status, q:(.openQuestions // 0), steps:[(.steps // [])[] | [.key, .status]]}' "$TMP/resp"; }
    api POST "/$PLAN/control"
    [[ "$CODE" =~ ^2 ]] || { sleep 20; api POST "/$PLAN/control"; }
    [[ "$CODE" =~ ^2 ]] || die "sem resposta do PRMake (HTTP $CODE) — tente de novo"
    fingerprint > "$TMP/fp0"; cp "$TMP/resp" "$TMP/ctl0"
    [[ "$(jq -r '.action' "$TMP/resp")" == "stop" ]] && { echo "PLANO JA TERMINADO ($(jq -r '.status' "$TMP/resp"))"; exit 11; }
    while [[ $waited -lt $MAX ]]; do
      # 20 s nos primeiros 10 min (resposta rapida), depois 60 s (espera longa, ex.: merge).
      if [[ $waited -lt 600 ]]; then delay=20; else delay=60; fi
      sleep "$delay"; waited=$((waited + delay))
      api POST "/$PLAN/control"
      [[ "$CODE" =~ ^2 ]] || continue
      fingerprint > "$TMP/fp1"
      cmp -s "$TMP/fp0" "$TMP/fp1" && continue
      echo "MUDOU NO PRMAKE (plano $(jq -r '.status' "$TMP/resp")):"
      jq -r --slurpfile old "$TMP/ctl0" '
        ($old[0].steps // [] | map({(.key): .status}) | add // {}) as $before
        | (if $old[0].status != .status then "  plano: \($old[0].status) -> \(.status)" + (if .statusReason then " (\(.statusReason))" else "" end) + (if .statusChangedBy then " por \(.statusChangedBy)" else "" end) else empty end),
          ((.steps // [])[] | select($before[.key] != .status) | "  etapa \(.key): \($before[.key] // "nova") -> \(.status)" + (if .executor == "user" then " (etapa do usuario)" else "" end)),
          (if ($old[0].openQuestions // 0) != (.openQuestions // 0) then "  perguntas sem resposta: \($old[0].openQuestions // 0) -> \(.openQuestions // 0)" else empty end),
          (if ((.readySteps // []) | length) > 0 then "  prontas para comecar: \(.readySteps | join(","))" else empty end),
          (if ((.waitingSteps // []) | length) > 0 then "  aguardando: \(.waitingSteps | join(","))" else empty end)' "$TMP/resp"
      [[ "$(jq -r '.action' "$TMP/resp")" == "stop" ]] && exit 11
      exit 0
    done
    echo "nada mudou em ${MAX}s — rode 'prmake-plan.sh watch $CARD' de novo para continuar vigiando"
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

  correction)
    require_plan
    ANALYSIS="$(state_get analysisPlanId)"; [[ -n "$ANALYSIS" ]] || ANALYSIS="$PLAN"
    TITLE="${1:?titulo do plano de correcao}"; read_steps_arg "${2:--}"
    api GET "/$ANALYSIS"
    [[ "$CODE" == "200" ]] || die "HTTP $CODE ao ler o plano de analise: $(resp_error)"
    jq -e '.phase == "analysis"' "$TMP/resp" >/dev/null || die "o plano ativo nao e de analise (use: prmake-plan.sh use $CARD analysis)"
    jq -n --arg card "$CARD" --arg title "$TITLE" --arg parent "$ANALYSIS" --slurpfile steps "$TMP/steps" \
      '{cardNumber:$card, kind:"analisar-bug", title:$title, phase:"correction", parentPlanId:$parent, steps:$steps[0]}' > "$TMP/body"
    api POST "" "$TMP/body"
    [[ "$CODE" =~ ^2 ]] || die "HTTP $CODE ao criar o plano de correcao: $(resp_error)"
    CORR=$(jq -r '.id' "$TMP/resp")
    save_state "$CORR" "$ANALYSIS" "$CORR"
    echo "PLANO DE CORRECAO criado (agora e o plano ativo):"
    print_plan
    ;;

  use)
    WHICH="${1:?analysis ou correction}"
    case "$WHICH" in
      analysis|analise) ID="$(state_get analysisPlanId)" ;;
      correction|correcao) ID="$(state_get correctionPlanId)" ;;
      *) die "use: analysis ou correction" ;;
    esac
    [[ -n "$ID" ]] || die "este card ainda nao tem plano de $WHICH"
    save_state "$ID" "$(state_get analysisPlanId)" "$(state_get correctionPlanId)"
    echo "plano ativo: $WHICH ($ID)"
    ;;

  ask)
    require_plan
    SRC="${1:--}"; if [[ "$SRC" == "-" ]]; then cat > "$TMP/q.json"; else cp "$SRC" "$TMP/q.json"; fi
    jq -e 'type == "array" and length > 0' "$TMP/q.json" >/dev/null || die "perguntas devem ser um array JSON nao vazio"
    jq '{questions: .}' "$TMP/q.json" > "$TMP/body"
    flush_quiet
    api POST "/$PLAN/questions" "$TMP/body"
    [[ "$CODE" =~ ^2 ]] || die "HTTP $CODE ao perguntar: $(resp_error)"
    echo "PERGUNTAS publicadas no PRMake — o usuario pode responder la ou aqui no terminal:"
    jq -r 'to_entries[] | "\(.key + 1). \(.value.text)" + (if (.value.options | length) > 0 then "\n   opcoes: " + ([.value.options[] | .label + (if .recommended then " (recomendada)" else "" end)] | join(" / ")) else "" end)' "$TMP/resp"
    echo "(respostas: prmake-plan.sh wait-answers $CARD — ou, se responderem aqui: prmake-plan.sh answer $CARD <n> \"texto\")"
    ;;

  answers)
    require_plan; api GET "/$PLAN"
    [[ "$CODE" == "200" ]] || die "HTTP $CODE: $(resp_error)"
    print_questions
    ;;

  wait-answers)
    require_plan; MAX="${1:-540}"; waited=0; delay=5
    while [[ $waited -lt $MAX ]]; do
      api POST "/$PLAN/control"
      if [[ "$CODE" =~ ^2 ]] && jq -e '.action == "stop"' "$TMP/resp" >/dev/null; then echo "stop — plano $(jq -r '.status' "$TMP/resp")"; exit 11; fi
      api GET "/$PLAN"
      if [[ "$CODE" == "200" ]] && jq -e '[.questions[]? | select(.status == "open")] | length == 0' "$TMP/resp" >/dev/null; then
        echo "RESPOSTAS:"; print_questions; exit 0
      fi
      sleep "$delay"; waited=$((waited + delay)); [[ $delay -lt 15 ]] && delay=$((delay + 5))
    done
    echo "ainda sem todas as respostas apos ${MAX}s — rode 'prmake-plan.sh wait-answers $CARD' de novo, ou pergunte no terminal e grave com 'answer'"
    exit 10
    ;;

  answer)
    require_plan; WHICH="${1:?numero ou id da pergunta}"; TEXT="${2:-}"
    [[ -n "$TEXT" ]] || TEXT="$(cat)"
    [[ -n "$TEXT" ]] || die "resposta vazia"
    if [[ "$WHICH" =~ ^[0-9]+$ ]]; then
      api GET "/$PLAN"; [[ "$CODE" == "200" ]] || die "HTTP $CODE: $(resp_error)"
      QID=$(jq -r --argjson n "$WHICH" '.questions[$n - 1].id // empty' "$TMP/resp")
      [[ -n "$QID" ]] || die "pergunta $WHICH nao existe"
    else
      QID="$WHICH"
    fi
    jq -n --arg a "$TEXT" '{answer:$a}' > "$TMP/body"
    send_or_queue POST "/$PLAN/questions/$QID/answer" "$TMP/body" && echo "OK resposta gravada (via claude)"
    ;;

  link)
    require_plan; KEY="${1:?key}"; URL="${2:?url}"; shift 2 || true
    TITLE=""; BLOCKS=false; KIND=""
    while [[ $# -gt 0 ]]; do
      case "$1" in
        --blocks) BLOCKS=true ;;
        --kind) KIND="${2:-}"; shift ;;
        *) [[ -z "$TITLE" ]] && TITLE="$1" ;;
      esac
      shift
    done
    jq -n --arg u "$URL" --arg t "$TITLE" --arg k "$KIND" --argjson b "$BLOCKS" \
      '{url:$u, blocksStep:$b} + (if $t != "" then {title:$t} else {} end) + (if $k != "" then {kind:$k} else {} end)' > "$TMP/body"
    send_or_queue POST "/$PLAN/steps/$KEY/links" "$TMP/body" && echo "OK link na etapa $KEY"
    ;;

  pr-text)
    # Mesmo caminho da gerar-prmake: prompt configurado (PromptBug/PromptUS), repro steps, commits e diff da branch.
    REPO="${1:?repositorio}"; BRANCH="${2:?branch com a correcao (ex.: hotfix/$CARD)}"
    FETCH="$HOME/.claude/skills/gerar-prmake/scripts/prmake-fetch.sh"
    [[ -f "$FETCH" ]] || die "skill gerar-prmake nao instalada ($FETCH) — instale pela tela Skills do PRMake"
    OUT="$CARD_DIR/pr/$REPO"; mkdir -p "$OUT"
    OUTDIR="$OUT" PRMAKE_TOKEN="$TOKEN" bash "$FETCH" "$CARD" "$BRANCH" "$REPO" || die "prmake-fetch falhou"
    echo ""
    echo "PROXIMO: siga $OUT/prompt.txt (troque {cardNumber}, {description} = $OUT/description.txt e"
    echo "{githubCommitDiff} = $OUT/diff.txt) exatamente como a gerar-prmake: ingles, markdown, titulos em negrito;"
    echo "Bug com o RCA entre <RCA> e </RCA>. Escreva $OUT/pr_generated.md e separe:"
    echo "  awk '/<RCA>/{f=1;next} /<\/RCA>/{f=0} f' $OUT/pr_generated.md > $OUT/rca.md"
    echo "  awk 'BEGIN{s=1} /<RCA>/{s=0} s==1{print} /<\/RCA>/{s=1}' $OUT/pr_generated.md | sed '/<\/RCA>/d' > $OUT/desc.md"
    echo "Depois: prmake-plan.sh save-pr-text $CARD $OUT/desc.md $OUT/rca.md <key-da-etapa> e open-pr ... $OUT/desc.md"
    ;;

  save-pr-text)
    require_plan; DESC_FILE="${1:?arquivo da descricao}"; RCA_FILE="${2:-}"; KEY="${3:-}"
    [[ -s "$DESC_FILE" ]] || die "descricao vazia: $DESC_FILE"
    USER_ID="$(printf '%s' "$TOKEN" | cut -d. -f2 | tr '_-' '/+' | base64 -d 2>/dev/null | sed -n 's/.*"ExternalId":"\([^"]*\)".*/\1/p')"
    [[ -n "$USER_ID" ]] || die "nao consegui extrair o ExternalId do token"
    RCA=""; [[ -n "$RCA_FILE" && -f "$RCA_FILE" ]] && RCA="$(cat "$RCA_FILE")"
    jq -n --rawfile desc "$DESC_FILE" --arg rca "$RCA" --arg uid "$USER_ID" --arg card "$CARD" --argjson form "${FORM_ID:-1}" \
      '{description:$desc, cardNumber:$card, userId:$uid, formId:$form, rootCause:$rca}' > "$TMP/reg.json"
    CODE="$(curl -s --max-time 60 -o "$TMP/resp" -w '%{http_code}' -X POST "$BASE/PullRequest" \
      -H "x-api-key: $TOKEN" -H 'content-type: application/json' --data-binary "@$TMP/reg.json" 2>/dev/null)"
    [[ "$CODE" =~ ^2 ]] || die "HTTP $CODE ao salvar a descricao no card: $(resp_error)"
    echo "OK descricao$( [[ -n "$RCA" ]] && echo ' e root cause') salvos no card $CARD (aparecem no 'Abrir PR' do PRMake)"
    # Guarda tambem no plano (aba Analises), com o repositorio no nome (pr/<repo>/desc.md -> pr-descricao-<repo>.md).
    SUFFIX="$(basename "$(cd "$(dirname "$DESC_FILE")" && pwd)")"; [[ "$SUFFIX" == "pr" || "$SUFFIX" == "." ]] && SUFFIX="$CARD"
    mkdir -p "$CARD_DIR/analises"
    cp "$DESC_FILE" "$CARD_DIR/analises/pr-descricao-$SUFFIX.md"
    upload_or_queue "$CARD_DIR/analises/pr-descricao-$SUFFIX.md" analysis "$KEY" "Descricao do PR (layout padrao) — $SUFFIX"
    if [[ -n "$RCA" ]]; then
      cp "$RCA_FILE" "$CARD_DIR/analises/pr-rca-$SUFFIX.md"
      upload_or_queue "$CARD_DIR/analises/pr-rca-$SUFFIX.md" analysis "$KEY" "Root cause (RCA) — $SUFFIX"
    fi
    exit 0
    ;;

  devops)
    # Fechamento do card: SEMPRE pelos endpoints do PRMake (0028) — voce gera os textos; quem grava no
    # DevOps e o PRMake (integracao do Azure do usuario, registro na Timeline).
    ACTION="${1:?acao: config|rootcause|summary|classifications|classify|zero-remaining|ready-for-qa|test-in-production|initial-estimate}"
    MD2HTML="$HOME/.claude/skills/gerar-prmake/scripts/md2html.py"
    to_html() { if [[ -f "$MD2HTML" ]]; then python3 "$MD2HTML" "$1"; else sed 's/&/\&amp;/g; s/</\&lt;/g' "$1" | awk 'BEGIN{print "<pre>"} {print} END{print "</pre>"}'; fi; }
    prmake_post() { # <caminho> [corpo.json]
      local args=(-s --max-time 90 -o "$TMP/resp" -w '%{http_code}' -X POST "$BASE/$1" -H "x-api-key: $TOKEN")
      [[ -n "${2:-}" ]] && args+=(-H 'content-type: application/json' --data-binary "@$2")
      CODE="$(curl "${args[@]}" 2>/dev/null)"; CODE="${CODE:-000}"
    }
    # DevOps fora do ar (PRMake devolve 502/503, ex.: TF10216): tenta de novo por ~3 min. So nas acoes idempotentes
    # (campo sobrescrito ou transicao de estado); o summary cria comentario e nao repete.
    prmake_post_retry() { # <caminho> [corpo.json]
      local attempt
      for attempt in 1 2 3 4; do
        prmake_post "$@"
        [[ "$CODE" =~ ^(502|503)$ ]] || return 0
        [[ $attempt -lt 4 ]] && { echo "   DevOps indisponivel (HTTP $CODE) — nova tentativa em $((attempt * 30))s" >&2; sleep $((attempt * 30)); }
      done
      return 0
    }
    case "$ACTION" in
      rootcause)
        F="${2:?arquivo .md do root cause}"; [[ -s "$F" ]] || die "arquivo vazio: $F"
        jq -n --arg rc "$(to_html "$F")" '{rootCause:$rc}' > "$TMP/body"
        prmake_post_retry "Azure/card/$CARD/rootcause" "$TMP/body" ;;
      summary)
        F="${2:?arquivo .md do resumo nao tecnico (**PT** --- ... **EN** --- ...)}"; [[ -s "$F" ]] || die "arquivo vazio: $F"
        jq -n --rawfile s "$F" --arg html "$(to_html "$F")" '{summary:$s, html:$html}' > "$TMP/body"
        prmake_post "PullRequest/$CARD/summary" "$TMP/body" ;;
      classify)
        # Opcoes vem do PRMake (GET Azure/actions/classifications); quem grava no DevOps e o PRMake.
        KIND="${2:?opcao (veja: prmake-plan.sh devops <card> classifications)}"
        jq -n --arg p "$KIND" '{preset:$p}' > "$TMP/body"
        prmake_post_retry "Azure/card/$CARD/actions/classify" "$TMP/body"
        [[ "$CODE" =~ ^2 ]] || die "HTTP $CODE ao classificar: $(resp_error)"
        echo "OK $(jq -r '.message // "classificado"' "$TMP/resp")"
        exit 0 ;;
      config)
        # Configuracao efetiva do usuario (AI Configurations, com os campos pessoais): destinos das acoes,
        # area exigida, estimativa inicial e o prompt do resumo nao tecnico. Nada de nomes presumidos.
        CODE="$(curl -s --max-time 30 -o "$TMP/resp" -w '%{http_code}' -H "x-api-key: $TOKEN" "$BASE/Azure/actions/config" 2>/dev/null)"
        [[ "$CODE" =~ ^2 ]] || die "HTTP $CODE ao ler a configuracao: $(resp_error)"
        jq -e '.available' "$TMP/resp" >/dev/null || die "Acoes DevOps indisponiveis (plugin 'AI Configurations' ausente) — avise o usuario"
        mkdir -p "$CARD_DIR/analises"
        jq -r '.bug.summaryPrompt // ""' "$TMP/resp" > "$CARD_DIR/analises/summary-prompt.txt"
        jq -r --arg sp "$CARD_DIR/analises/summary-prompt.txt" '.bug as $b |
          "test-in-production: " + (if ($b.testInProduction.area // "") != "" and ($b.testInProduction.state // "") != ""
              then "move para \"\($b.testInProduction.state)\" na area \"\($b.testInProduction.area)\""
                + (if ($b.testInProduction.requiredArea // "") != "" then " (so se o card estiver na area \"\($b.testInProduction.requiredArea)\")" else "" end)
                + (if ($b.testInProduction.comment // "") != "" then "; comentario: \"\($b.testInProduction.comment)\"" else "" end)
              else "NAO configurado" end),
          "ready-for-qa: " + (if ($b.readyForQa.state // "") != "" then "move para \"\($b.readyForQa.state)\"" else "NAO configurado" end),
          "initial-estimate: " + (if $b.initialEstimate.configured
              then "Original \($b.initialEstimate.originalEstimate) · Remaining \($b.initialEstimate.remainingWork) · Completed \($b.initialEstimate.completedWork)"
              else "NAO configurado" end),
          "zero-remaining: zera o Remaining Work",
          "summary-prompt: " + (if ($b.summaryPrompt // "") != "" then $sp else "(nao configurado — use o formato padrao PT/EN)" end)' "$TMP/resp"
        exit 0 ;;
      classifications)
        CODE="$(curl -s --max-time 30 -o "$TMP/resp" -w '%{http_code}' -H "x-api-key: $TOKEN" "$BASE/Azure/actions/classifications" 2>/dev/null)"
        [[ "$CODE" =~ ^2 ]] || die "HTTP $CODE ao listar as opcoes: $(resp_error)"
        jq -r '.[] | "\(.key)\t[\(.pattern // "-")] \(.label) — \(.resolutionType) | \(.generalClassification) | \(.classification)"' "$TMP/resp"
        exit 0 ;;
      zero-remaining|ready-for-qa|test-in-production|initial-estimate)
        prmake_post_retry "Azure/card/$CARD/actions/$ACTION" ;;
      *) die "acao desconhecida: $ACTION" ;;
    esac
    [[ "$CODE" =~ ^2 ]] || die "HTTP $CODE em $ACTION: $(resp_error)"
    echo "OK $ACTION no card $CARD"
    ;;

  settings)
    load_settings
    jq -r '"Skills Configurations (" + (if .available then "ok" else "indisponivel" end) + "):",
      (.settings | to_entries[] | "  \(.key) = " + (if (.value | type) == "string" then .value else (.value | tojson) end)),
      "Prompts: bug=" + (if .prompts.bug then "sim" else "nao" end) + " · userStory=" + (if .prompts.userStory then "sim" else "nao" end)
        + " · resumo=" + (if .prompts.summary then "sim" else "nao" end),
      "Campos do DevOps: " + (if .fields then (.fields | to_entries | map("\(.key)=\(.value)") | join(" · ")) else "indisponivel (integracao do Azure?)" end)' "$SETTINGS"
    echo "salvo em $SETTINGS"
    ;;

  branches)
    REPO="${1:?repositorio (ex.: edv-solvace, revamp-BOS)}"; shift
    FLOW=""; BASE_BRANCH=""
    while [[ $# -gt 0 ]]; do
      case "$1" in
        --flow) FLOW="${2:?}"; shift 2 ;;
        --base) BASE_BRANCH="${2:?}"; shift 2 ;;
        *) die "argumento desconhecido: $1" ;;
      esac
    done
    load_settings
    NAME_PATTERN="$(setting BranchNamePattern)"; NAME_PATTERN="${NAME_PATTERN:-hotfix/{card\}}"
    COMMIT_PATTERN="$(setting CommitMessagePattern)"; TITLE_PATTERN="$(setting PrTitlePattern)"
    [[ -n "$(setting BranchStrategy)" ]] || die "BranchStrategy nao configurado no PRMake (Skills Configurations) — avise o usuario"
    # Fluxo: --flow > area do card (BranchFlowByArea) > perguntar.
    if [[ -z "$FLOW" ]]; then
      CODE="$(curl -s --max-time 60 -o "$TMP/card" -w '%{http_code}' -H "x-api-key: $TOKEN" "$BASE/Azure/card/$CARD" 2>/dev/null)"
      AREA="$( [[ "$CODE" =~ ^2 ]] && jq -r '.fields["System.AreaPath"] // ""' "$TMP/card" || true)"
      FLOW="$(jq -r --arg a "$AREA" '[.settings.BranchFlowByArea // [] | .[] | . as $r | select($a != "" and (($a | ascii_downcase) | contains($r.areaContains | ascii_downcase))) | $r.flow][0] // empty' "$SETTINGS")"
    fi
    # Tipo do repositorio: primeira regra de BranchStrategy.repositories que casa (glob, sem diferenciar maiusculas).
    KIND=""; LREPO="$(printf '%s' "$REPO" | tr '[:upper:]' '[:lower:]')"
    while IFS=$'\t' read -r PAT K; do
      [[ -z "$PAT" ]] && continue
      LPAT="$(printf '%s' "$PAT" | tr '[:upper:]' '[:lower:]')"
      # shellcheck disable=SC2053
      if [[ "$LREPO" == $LPAT ]]; then KIND="$K"; break; fi
    done < <(jq -r '.settings.BranchStrategy.repositories // [] | .[] | "\(.match)\t\(.kind)"' "$SETTINGS")
    [[ -n "$KIND" ]] || die "repositorio '$REPO' sem regra em BranchStrategy.repositories — pergunte ao usuario e peca para o admin configurar"
    FIX="$(fill_pattern "$NAME_PATTERN")"
    if [[ -z "$FLOW" ]]; then
      echo "repositorio=$REPO tipo=$KIND fluxo=PERGUNTAR (area '${AREA:-?}' sem regra em BranchFlowByArea)"
      echo "opcoes: $(jq -r '.settings.BranchStrategy.flows // {} | keys | join(", ")' "$SETTINGS") — rode de novo com --flow <fluxo>"
      exit 3
    fi
    jq -e --arg f "$FLOW" --arg k "$KIND" '.settings.BranchStrategy.flows[$f][$k]' "$SETTINGS" > "$TMP/rule" 2>/dev/null \
      || die "sem regra para fluxo '$FLOW' + tipo '$KIND' em BranchStrategy.flows — pergunte ao usuario"
    RULE_BASE="$(jq -r '.base // empty' "$TMP/rule")"; OPTIONS="$(jq -r '.baseOptions // [] | join(", ")' "$TMP/rule")"
    ASK="$(jq -r '.askBase // false' "$TMP/rule")"
    if [[ -z "$BASE_BRANCH" ]]; then
      if [[ "$ASK" == "true" || -z "$RULE_BASE" ]]; then
        echo "repositorio=$REPO tipo=$KIND fluxo=$FLOW"
        echo "base=PERGUNTAR (opcoes: ${OPTIONS:-$RULE_BASE}) — rode de novo com --base <branch>"
        exit 3
      fi
      BASE_BRANCH="$RULE_BASE"
    fi
    echo "repositorio=$REPO tipo=$KIND fluxo=$FLOW base=$BASE_BRANCH"
    echo "branch-correcao=$FIX"
    [[ -n "$COMMIT_PATTERN" ]] && echo "commit=\"$(fill_pattern "$COMMIT_PATTERN")\""
    echo "prs:"
    PUSH="$FIX"; CMDS=()
    while IFS='|' read -r SUFFIX FROM TARGET; do
      TARGET="${TARGET//\{base\}/$BASE_BRANCH}"; FROM="${FROM//\{base\}/$BASE_BRANCH}"
      BR="$FIX$SUFFIX"
      TITLE="$(fill_pattern "${TITLE_PATTERN:-{card\} {TARGET\}}" "$TARGET")"
      if [[ -n "$SUFFIX" && -n "$FROM" ]]; then
        echo "  $BR (de origin/$FROM + cherry-pick da correcao) -> $TARGET  titulo \"$TITLE\""
        CMDS+=("git checkout -b $BR origin/$FROM && git cherry-pick <sha(s)>"); PUSH="$PUSH $BR"
      else
        echo "  $BR -> $TARGET  titulo \"$TITLE\""
      fi
      CMDS+=("#open-pr $BR $TARGET \"$TITLE\"")
    done < <(jq -r '.prs // [] | .[] | "\(.suffix // "")|\(.from // "")|\(.target)"' "$TMP/rule")
    echo "comandos:"
    echo "  git fetch origin"
    echo "  git checkout -b $FIX origin/$BASE_BRANCH    # corrija aqui${COMMIT_PATTERN:+; commit: \"$(fill_pattern "$COMMIT_PATTERN")\"}"
    for C in "${CMDS[@]}"; do [[ "$C" == \#open-pr* ]] || echo "  $C"; done
    echo "  git push -u origin $PUSH"
    for C in "${CMDS[@]}"; do
      [[ "$C" == \#open-pr* ]] || continue
      read -r _ BR TG REST <<< "$C"
      echo "  bash \$PLAN open-pr $CARD $REPO $BR $TG $REST \$CARD_DIR/pr/$REPO/desc.md"
    done
    ;;

  open-pr)
    # Abre o PR pelo PRMake (o mesmo endpoint da tela/gerar-prmake): fica registrado no card, na Timeline e
    # e acompanhado pela etapa de PR do repositorio (conclui quando for mesclado — por outra pessoa).
    require_plan; REPO="${1:?repositorio}"; BRANCH="${2:?branch (ex.: hotfix/74517-dev)}"; TARGET="${3:?branch de destino}"
    if [[ -n "${4:-}" ]]; then TITLE="$4"; else
      load_settings; TITLE="$(fill_pattern "$(setting PrTitlePattern)" "$TARGET")"
      [[ -n "$TITLE" ]] || TITLE="$CARD $(printf '%s' "$TARGET" | tr '[:lower:]' '[:upper:]')"
    fi
    DESC_FILE="${5:-}"
    [[ "$BRANCH" == */* ]] || die "informe a branch completa (ex.: hotfix/$CARD-dev)"
    PREFIX="${BRANCH%%/*}/"; NAME="${BRANCH#*/}"
    USER_ID="$(printf '%s' "$TOKEN" | cut -d. -f2 | tr '_-' '/+' | base64 -d 2>/dev/null | sed -n 's/.*"ExternalId":"\([^"]*\)".*/\1/p')"
    [[ -n "$USER_ID" ]] || die "nao consegui extrair o ExternalId do token"
    if [[ -n "$DESC_FILE" && -f "$DESC_FILE" ]]; then DESC="$(cat "$DESC_FILE")"; else DESC="AB#$CARD"; fi
    jq -n --arg repo "$REPO" --arg prefix "$PREFIX" --arg name "$NAME" --arg target "$TARGET" --arg title "$TITLE" \
      --arg desc "$DESC" --arg uid "$USER_ID" \
      '{repositoryId:$repo, branchPrefix:$prefix, branchName:$name, targetBranch:$target, title:$title, description:$desc, draft:false, userId:$uid}' > "$TMP/pr.json"
    CODE="$(curl -s --max-time 90 -o "$TMP/resp" -w '%{http_code}' -X POST "$BASE/PullRequest/$CARD/github" \
      -H "x-api-key: $TOKEN" -H 'content-type: application/json' --data-binary "@$TMP/pr.json" 2>/dev/null)"
    [[ "$CODE" =~ ^2 ]] || die "HTTP $CODE ao abrir o PR: $(resp_error)"
    NUMBER=$(jq -r '.number // empty' "$TMP/resp"); URL=$(jq -r '.url // empty' "$TMP/resp")
    echo "PR #$NUMBER aberto: $REPO $BRANCH -> $TARGET  $URL$(jq -e '.alreadyExisted' "$TMP/resp" >/dev/null && echo '  (ja existia)')"
    # Anexa ja a etapa de PR desse repositorio (a sincronizacao do PRMake faria em ate 1 min).
    api GET "/$PLAN"
    # Etapa de PR desse repositorio ainda aberta (ex.: pr-edv-solvace-2 de uma nova rodada); senao, a primeira.
    STEP=$(jq -r --arg r "$REPO" '[.steps[] | select(.kind == "pr" and ((.repository // "") | ascii_downcase) == ($r | ascii_downcase))]
      | ((map(select(.status != "completed" and .status != "cancelled")) + .)[0].key) // empty' "$TMP/resp")
    if [[ -n "$STEP" && -n "$NUMBER" ]]; then
      jq -n --arg u "$URL" --arg t "#$NUMBER $REPO -> $TARGET" --argjson n "$NUMBER" --arg r "$REPO" --arg tb "$TARGET" \
        '{url:$u, title:$t, kind:"pr", pullRequestNumber:$n, repository:$r, targetBranch:$tb}' > "$TMP/body"
      api POST "/$PLAN/steps/$STEP/links" "$TMP/body"
      [[ "$CODE" =~ ^2 ]] && echo "   anexado a etapa $STEP"
    fi
    echo "Lembrete: o merge e feito por outra pessoa — nunca mescle."
    ;;

  flush)
    require_plan; flush_outbox
    [[ -s "$OUTBOX" ]] && { echo "ainda na fila: $(wc -l < "$OUTBOX" | tr -d ' ')"; exit 1; }
    echo "OK fila vazia"
    ;;

  *) die "comando desconhecido: $CMD" ;;
esac
