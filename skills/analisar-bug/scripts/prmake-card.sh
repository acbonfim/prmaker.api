#!/usr/bin/env bash
# Retomar um card sem copiar comando (feature 0033).
#
# Uso:
#   prmake-card.sh <card> [--bg]      volta para a sessao do Claude Code que trabalhou no card (claude --resume) nesta
#                                     maquina, na mesma pasta; sem sessao aqui, abre uma nova: /analisar-bug <card>.
#                                     --bg: continua em segundo plano (acompanhe no PRMake ou com: claude attach <id>)
#   prmake-card.sh agent run          VIGIA (opcional, por pessoa): a cada minuto pergunta ao PRMake o que retomar nesta
#                                     maquina (pedido de "Continuar" na tela ou respostas dadas pela tela) e continua a
#                                     sessao em segundo plano. So segue ate a proxima pergunta/PR — nunca faz merge.
#   prmake-card.sh agent install|uninstall|status
#                                     liga/desliga o vigia no login do macOS (LaunchAgent); desligado por padrao
#
# Env: PRMAKE_TOKEN (ou ~/.claude/prmake-token.txt), PRMAKE_API_BASE, PRMAKE_AGENT_INTERVAL (segundos, padrao 60).
set -uo pipefail
BASE="${PRMAKE_API_BASE:-https://api.softhouse.app.br/api/v1}"
HOST="$(hostname -s 2>/dev/null || hostname)"
LOG="$HOME/.claude/prmake-agent.log"
PLIST="$HOME/Library/LaunchAgents/br.app.softhouse.prmake-agent.plist"
SELF="$(cd "$(dirname "$0")" && pwd)/$(basename "$0")"
die() { echo "ERRO: $*" >&2; exit 1; }
command -v jq >/dev/null || die "jq nao encontrado"
command -v claude >/dev/null || die "Claude Code (claude) nao encontrado no PATH"
if [[ -n "${PRMAKE_TOKEN:-}" ]]; then TK="$PRMAKE_TOKEN"; elif [[ -f "$HOME/.claude/prmake-token.txt" ]]; then TK="$(tr -d '\n' < "$HOME/.claude/prmake-token.txt")"; else die "token do PRMake nao encontrado"; fi

get() { curl -s --max-time 20 -H "x-api-key: $TK" -H 'accept: application/json' "$BASE/ExecutionPlan$1"; }
post() { curl -s --max-time 20 -o /dev/null -w '%{http_code}' -X POST -H "x-api-key: $TK" -H 'X-Execution-Client: skill' "$BASE/ExecutionPlan$1"; }
transcript_exists() { [[ -n "$(find "$HOME/.claude/projects" -maxdepth 2 -name "$1.jsonl" 2>/dev/null | head -1)" ]]; }

resume_prompt() { # <card>
  printf '%s' "Retomando o card $1 pelo PRMake. Antes de continuar: rode prmake-plan.sh resume-info $1 e prmake-plan.sh notes $1 (o que mudou na tela enquanto voce estava parado: respostas, comentarios, pausa, etapas) e siga de onde parou, conforme a skill analisar-bug."
}

open_card() { # <card> [--bg]
  local card="$1" bg="${2:-}" plans sid cwd
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
      run)
        echo "$(date '+%F %T') vigia iniciado em $HOST" >> "$LOG"
        while true; do agent_once; sleep "${PRMAKE_AGENT_INTERVAL:-60}"; done ;;
      install)
        mkdir -p "$(dirname "$PLIST")"
        cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>br.app.softhouse.prmake-agent</string>
  <key>ProgramArguments</key><array><string>/bin/bash</string><string>$SELF</string><string>agent</string><string>run</string></array>
  <key>EnvironmentVariables</key><dict><key>PATH</key><string>$PATH</string></dict>
  <key>RunAtLoad</key><true/><key>KeepAlive</key><true/>
  <key>StandardErrorPath</key><string>$LOG</string>
</dict></plist>
EOF
        launchctl unload "$PLIST" 2>/dev/null; launchctl load "$PLIST" && echo "Vigia ligado (log: $LOG). Desligar: prmake-card.sh agent uninstall" ;;
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
