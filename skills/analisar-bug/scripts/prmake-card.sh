#!/usr/bin/env bash
# Retomar um card sem copiar comando (feature 0033).
#
# Uso:
#   prmake-card.sh <card> [--bg]      volta para a sessao do Claude Code que trabalhou no card (claude --resume) nesta
#                                     maquina, na mesma pasta; sem sessao aqui, abre uma nova: /analisar-bug <card>.
#                                     --bg: continua em segundo plano (acompanhe no PRMake ou com: claude attach <id>)
#   prmake-card.sh agent ...          SUBSTITUIDO pelo executor do PRMake (0039): fila no PRMake, botao "Analisar com
#                                     Claude", macOS/Windows/Linux — bash ~/.claude/skills/.prmake/prmake-skills.sh agent install
#                                     ('agent uninstall' continua desligando o vigia antigo).
#
# Com o executor rodando o card agora, abrir no terminal pergunta: esperar terminar ou assumir (cancela o executor).
#
# Env: PRMAKE_TOKEN (ou ~/.claude/prmake-token.txt), PRMAKE_API_BASE, PRMAKE_AGENT_INTERVAL (segundos, padrao 60).
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
BASE="${PRMAKE_API_BASE:-https://api.softhouse.app.br/api/v1}"
HOST="$( (hostname -s 2>/dev/null || hostname) | tr -d '\r')"
LOG="$HOME/.claude/prmake-agent.log"
PLIST="$HOME/Library/LaunchAgents/br.app.softhouse.prmake-agent.plist"
SELF="$(cd "$(dirname "$0")" && pwd)/$(basename "$0")"
die() { echo "ERRO: $*" >&2; exit 1; }
command -v jq >/dev/null || die "jq nao encontrado"
command -v claude >/dev/null || die "Claude Code (claude) nao encontrado no PATH"
if [[ -n "${PRMAKE_TOKEN:-}" ]]; then TK="$PRMAKE_TOKEN"; elif [[ -f "$HOME/.claude/prmake-token.txt" ]]; then TK="$(tr -d '\r\n' < "$HOME/.claude/prmake-token.txt")"; else die "token do PRMake nao encontrado"; fi

get() { curl -s --max-time 20 -H "x-api-key: $TK" -H 'accept: application/json' "$BASE/ExecutionPlan$1"; }
post() { curl -s --max-time 20 -o /dev/null -w '%{http_code}' -X POST -H "x-api-key: $TK" -H 'X-Execution-Client: skill' "$BASE/ExecutionPlan$1"; }
transcript_exists() { [[ -n "$(find "$HOME/.claude/projects" -maxdepth 2 -name "$1.jsonl" 2>/dev/null | head -1)" ]]; }

resume_prompt() { # <card>
  printf '%s' "Retomando o card $1 pelo PRMake. Antes de continuar, veja o que mudou na tela enquanto voce estava parado (respostas, comentarios, pausa, etapas) com as ferramentas MCP prmake_plan, prmake_notes e prmake_answers (card $1) — sem o MCP na sessao: prmake-plan.sh resume-info/notes/answers $1 — e siga de onde parou, conforme a skill analisar-bug."
}

# 0039: o executor esta rodando este card agora? Nunca dois processos na mesma sessao.
guard_executor() { # <card> [--bg]
  local card="$1" bg="${2:-}" q id st who answer
  q="$(curl -s --max-time 20 -H "x-api-key: $TK" -H 'accept: application/json' "$BASE/ExecutionQueue/card/$card" 2>/dev/null)" || return 0
  id="$(jq -r '.active.id // empty' <<<"$q" 2>/dev/null)"; st="$(jq -r '.active.status // empty' <<<"$q" 2>/dev/null)"
  [[ -n "$id" ]] || return 0
  who="$(jq -r '.active.workerName // "o executor"' <<<"$q")"
  if [[ "$st" == queued ]]; then
    echo "Ha um pedido na fila do executor para o card $card ($(jq -r '.active.waitReason // "aguardando"' <<<"$q"))."
  else
    echo "O executor em $who esta rodando o card $card agora."
  fi
  if [[ "$bg" == "--bg" || ! -t 0 ]]; then
    die "o executor ja cuida deste card — acompanhe no PRMake (ou rode sem --bg para assumir)"
  fi
  read -r -p "[e]sperar terminar, [a]ssumir aqui (cancela o pedido do executor) ou [s]air? " answer
  case "$answer" in
    a|A)
      curl -s --max-time 20 -o /dev/null -X POST -H "x-api-key: $TK" -H 'content-type: application/json' \
        --data '{"reason":"Assumido no terminal"}' "$BASE/ExecutionQueue/$id/cancel"
      echo "Pedido cancelado — aguardando o executor encerrar a sessao..."; sleep 8 ;;
    e|E)
      echo "Esperando o executor terminar (Ctrl+C para sair)..."
      while jq -e '.active != null' <<<"$(curl -s --max-time 20 -H "x-api-key: $TK" "$BASE/ExecutionQueue/card/$card")" >/dev/null 2>&1; do sleep 15; done ;;
    *) exit 0 ;;
  esac
}

open_card() { # <card> [--bg]
  local card="$1" bg="${2:-}" plans sid cwd
  guard_executor "$card" "$bg"
  plans="$(get "/card/$card")"
  # Plano mais recente que ainda nao terminou (senao o mais recente) com sessao registrada NESTA maquina.
  IFS=$'\t' read -r sid cwd < <(jq -r --arg h "$HOST" '
      ([.[] | select(.status != "completed" and .status != "cancelled")] + .)
      | map(select(.executor != null and ((.executor.host // "") | ascii_downcase) == ($h | ascii_downcase)))
      | .[0].executor // empty | "\(.sessionId)\t\(.cwd // "")"' <<<"${plans:-[]}" 2>/dev/null)
  if [[ -n "${sid:-}" ]] && transcript_exists "$sid"; then
    [[ -n "${cwd:-}" && -d "$cwd" ]] && cd "$cwd"
    for id in $(jq -r '.[] | select(.resumePending == true) | .id' <<<"$plans" 2>/dev/null); do post "/$id/resume-ack" >/dev/null; done
    if [[ "$bg" == "--bg" ]]; then
      echo "Retomando o card $card em segundo plano (sessao $sid, pasta $PWD) — acompanhe no PRMake ou: claude attach $sid"
      exec claude --bg --resume "$sid" "$(resume_prompt "$card")"
    fi
    echo "Retomando o card $card na sessao $sid (pasta $PWD)"
    exec claude --resume "$sid" "$(resume_prompt "$card")"
  fi
  [[ -n "${sid:-}" ]] && echo "(a sessao $sid do card nao esta nesta maquina — abrindo uma nova)"
  [[ "$bg" == "--bg" ]] && exec claude --bg -n "$card" "/analisar-bug $card"
  exec claude -n "$card" "/analisar-bug $card"
}

agent_once() {
  local list; list="$(get "/resume-candidates?host=$HOST")" || return 0
  jq -e 'type == "array"' <<<"$list" >/dev/null 2>&1 || return 0
  jq -c '.[]' <<<"$list" | while IFS= read -r c; do
    local card plan sid cwd reason
    card="$(jq -r '.cardNumber' <<<"$c")"; plan="$(jq -r '.planId' <<<"$c")"; reason="$(jq -r '.reason' <<<"$c")"
    sid="$(jq -r '.session.sessionId' <<<"$c")"; cwd="$(jq -r '.session.cwd // empty' <<<"$c")"
    transcript_exists "$sid" || { echo "$(date '+%F %T') card $card: sessao $sid nao esta nesta maquina — ignorado" >> "$LOG"; continue; }
    [[ "$(post "/$plan/resume-ack")" =~ ^2 ]] || continue
    echo "$(date '+%F %T') card $card ($reason): retomando a sessao $sid em segundo plano" >> "$LOG"
    ( [[ -n "$cwd" && -d "$cwd" ]] && cd "$cwd"; claude --bg --resume "$sid" "$(resume_prompt "$card")" >> "$LOG" 2>&1 )
    command -v osascript >/dev/null && osascript -e "display notification \"Retomando o card $card ($([[ $reason == answers ]] && echo 'respostas chegaram' || echo 'pedido de continuar'))\" with title \"PRMake\"" 2>/dev/null
  done
}

case "${1:-}" in
  agent)
    case "${2:-}" in
      install)
        echo "O vigia foi substituido pelo executor do PRMake (0039) — fila no PRMake, botao \"Analisar com Claude\"."
        echo "Instale: bash ~/.claude/skills/.prmake/prmake-skills.sh agent install"
        exit 1 ;;
      run)
        # Vigia antigo ainda instalado: continua (o PRMake nao manda para ele os cards de quem ja tem executor).
        echo "$(date '+%F %T') vigia iniciado em $HOST" >> "$LOG"
        while true; do agent_once; sleep "${PRMAKE_AGENT_INTERVAL:-60}"; done ;;
      uninstall)
        launchctl unload "$PLIST" 2>/dev/null; rm -f "$PLIST"; echo "Vigia desligado." ;;
      status)
        if launchctl list 2>/dev/null | grep -q br.app.softhouse.prmake-agent; then echo "vigia: ligado"; else echo "vigia: desligado"; fi
        tail -5 "$LOG" 2>/dev/null ;;
      *) die "uso: prmake-card.sh agent run|install|uninstall|status" ;;
    esac ;;
  ""|-h|--help) sed -n '2,16p' "$0" ;;
  *) open_card "$1" "${2:-}" ;;
esac
