#!/usr/bin/env bash
# Espelho local da Base Solvace (feature 0033): engenharia reversa + Knowledge Center filtrado, em
# ~/.claude/solvace-kb. Ler arquivo local nao custa rede nem chamadas — leia o INDEX.md primeiro e abra so o que precisa.
#
# Uso: kb.sh <comando> [args]
#   sync [--quiet]            baixa o pacote do PRMake so quando o hash muda
#   path                      imprime a pasta do espelho
#   index                     imprime o indice (confere o PRMake se o espelho tem mais de 6 h; avisa se > 24 h)
#   show <projeto> [secao]    sem secao: a ficha do projeto (resumo, depende de / usado por, com evidencia) e as secoes;
#                             com secao: imprime a secao (chave ou parte do nome)
#   find <termo> [max=20]     onde o termo aparece no espelho (arquivo:linha, trecho curto)
#   status                    hash local x publicado, quantidades, idade do espelho e agendamento
#   agendar install|uninstall|status|run
#                             sincronizacao em segundo plano (LaunchAgent, a cada KB_SYNC_INTERVAL=7200 s): Knowledge
#                             Center -> PRMake (so com credencial local) e PRMake -> espelho
set -uo pipefail
BASE="${PRMAKE_API_BASE:-https://api.softhouse.app.br/api/v1}"
KB="${SOLVACE_KB_DIR:-$HOME/.claude/solvace-kb}"
CMD="${1:-}"; shift || true
QUIET=0; for a in "$@"; do [[ "$a" == "--quiet" ]] && QUIET=1; done
die() { echo "ERRO: $*" >&2; exit 1; }
say() { [[ $QUIET -eq 1 ]] || echo "$*"; }

SELF="$(cd "$(dirname "$0")" && pwd)/$(basename "$0")"
HERE="$(dirname "$SELF")"
PLIST="$HOME/Library/LaunchAgents/br.app.softhouse.solvace-kb.plist"
LOG="$HOME/.claude/solvace-kb-sync.log"
INTERVAL="${KB_SYNC_INTERVAL:-7200}"

token() {
  if [[ -n "${PRMAKE_TOKEN:-}" ]]; then printf '%s' "$PRMAKE_TOKEN"; return; fi
  [[ -f "$HOME/.claude/prmake-token.txt" ]] && { tr -d '\n' < "$HOME/.claude/prmake-token.txt"; return; }
  return 1
}

local_hash() { cat "$KB/.hash" 2>/dev/null; }

# Descompacta sem depender do unzip (Windows/Git Bash nao tem): bsdtar do sistema ou zipfile do Python.
extract() {
  if command -v unzip >/dev/null; then unzip -q "$1" -d "$2"; return; fi
  local win_tar="${SYSTEMROOT:-/c/Windows}/System32/tar.exe"
  if [[ -x "$win_tar" ]]; then (cd "$2" && "$win_tar" -xf "$1"); return; fi
  if tar --version 2>/dev/null | grep -q bsdtar; then tar -xf "$1" -C "$2"; return; fi
  python3 -m zipfile -e "$1" "$2"
}

# segundos desde a ultima conferencia com o PRMake (999999 = nunca)
age() {
  local f="$KB/.checked"; [[ -f "$f" ]] || { echo 999999; return; }
  echo $(( $(date +%s) - $(stat -f %m "$f" 2>/dev/null || stat -c %Y "$f") ))
}

do_sync() {
  command -v curl >/dev/null && command -v jq >/dev/null || { say "kb: curl/jq ausentes"; return 1; }
  local tk; tk="$(token)" || { say "kb: sem token do PRMake"; return 1; }
  local tmp; tmp="$(mktemp -d "${TMPDIR:-/tmp}/solvace-kb.XXXXXX")"
  local code; code="$(curl -s --max-time 20 -o "$tmp/m.json" -w '%{http_code}' -H "x-api-key: $tk" "$BASE/Architecture/export/manifest")"
  if [[ "$code" != "200" ]]; then rm -rf "$tmp"; say "kb: manifest HTTP $code (espelho mantido)"; return 1; fi
  local remote; remote="$(jq -r '.hash // empty' "$tmp/m.json")"
  if [[ -n "$remote" && "$remote" == "$(local_hash)" && -f "$KB/INDEX.md" ]]; then
    rm -rf "$tmp"; touch "$KB/.checked"; say "kb: espelho em dia ($remote)"; return 0
  fi
  code="$(curl -s --max-time 120 -o "$tmp/kb.zip" -w '%{http_code}' -H "x-api-key: $tk" "$BASE/Architecture/export")"
  if [[ "$code" != "200" ]]; then rm -rf "$tmp"; say "kb: export HTTP $code (espelho mantido)"; return 1; fi
  mkdir -p "$tmp/new" && extract "$tmp/kb.zip" "$tmp/new" || { rm -rf "$tmp"; say "kb: pacote invalido"; return 1; }
  printf '%s' "$remote" > "$tmp/new/.hash"; touch "$tmp/new/.checked"
  mkdir -p "$(dirname "$KB")"
  rm -rf "$KB.old"; [[ -d "$KB" ]] && mv "$KB" "$KB.old"
  mv "$tmp/new" "$KB" && rm -rf "$KB.old" "$tmp"
  echo "Base Solvace atualizada ($remote): $(jq -r '"\(.projects) projetos, \(.sections) secoes, \(.knowledgeArticles) artigos do KC (\(.knowledgeEnvironment))"' "$KB/manifest.json")"
}

scheduled() { launchctl list 2>/dev/null | grep -q br.app.softhouse.solvace-kb; }

case "$CMD" in
  sync) do_sync ;;
  path) echo "$KB" ;;
  index)
    if [[ ! -f "$KB/INDEX.md" ]]; then
      do_sync >/dev/null || die "espelho indisponivel e sem conexao com o PRMake"
    elif (( $(age) > 21600 )); then
      QUIET=1; do_sync >/dev/null 2>&1
    fi
    if (( $(age) > 86400 )); then
      echo "AVISO: Base Solvace sem conferir com o PRMake ha $(( $(age) / 3600 )) h — pode estar desatualizada (kb.sh sync)" >&2
    fi
    cat "$KB/INDEX.md" ;;
  show)
    P="${1:?projeto}"; SEC="${2:-}"
    [[ -d "$KB/projects/$P" ]] || { P="$(ls "$KB/projects" 2>/dev/null | grep -i -- "$P" | head -1)"; [[ -n "$P" ]] || die "projeto '${1}' nao esta no espelho (kb.sh index)"; }
    if [[ -z "$SEC" ]]; then
      [[ -f "$KB/projects/$P/000-projeto.md" ]] && cat "$KB/projects/$P/000-projeto.md"
      echo; echo "Arquivos: $(ls "$KB/projects/$P" | sed 's/\.md$//' | tr '\n' ' ')"
      exit 0
    fi
    F="$(ls "$KB/projects/$P" | grep -i -- "$SEC" | head -1)"; [[ -n "$F" ]] || die "secao '$SEC' nao encontrada em $P"
    cat "$KB/projects/$P/$F" ;;
  find)
    T="${1:?termo}"; MAX="${2:-20}"
    [[ -d "$KB" ]] || die "espelho vazio — rode kb.sh sync"
    grep -rIin --include='*.md' -- "$T" "$KB" 2>/dev/null | sed "s|$KB/||" | cut -c1-220 | head -n "$MAX" ;;
  status)
    echo "local: $(local_hash || echo nenhum) em $KB"
    A="$(age)"; if (( A < 999999 )); then echo "conferido com o PRMake ha $(( A / 60 )) min"; else echo "nunca conferido"; fi
    if scheduled; then echo "agendamento: ligado (log: $LOG)"; else echo "agendamento: desligado (kb.sh agendar install)"; fi
    tk="$(token)" && curl -s --max-time 20 -H "x-api-key: $tk" "$BASE/Architecture/export/manifest" | jq -c . ;;
  agendar)
    case "${1:-}" in
      run)
        # throttle: chamado pelo hook de toda sessao e pelo LaunchAgent; KB_FORCE=1 ignora
        RAN="$KB/../.solvace-kb-ran"
        if [[ -z "${KB_FORCE:-}" && -f "$RAN" ]] && (( $(date +%s) - $(stat -f %m "$RAN" 2>/dev/null || stat -c %Y "$RAN") < 1800 )); then exit 0; fi
        mkdir -p "$(dirname "$RAN")"; touch "$RAN"
        {
          echo "$(date '+%F %T') sincronizando"
          # Knowledge Center -> PRMake so em maquina com a credencial (somente leitura); sem ela vale a base do PRMake.
          if [[ -f "$HOME/.claude/knowledgecenter-credentials.json" ]]; then
            bash "$HERE/kc.sh" sync --quiet || echo "kc: sync falhou (segue com a base do PRMake)"
          fi
          QUIET=1; do_sync || echo "kb: sync falhou (espelho mantido)"
        } >> "$LOG" 2>&1 ;;
      install)
        mkdir -p "$(dirname "$PLIST")"
        cat > "$PLIST" <<PLISTEOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>br.app.softhouse.solvace-kb</string>
  <key>ProgramArguments</key><array><string>/bin/bash</string><string>$SELF</string><string>agendar</string><string>run</string></array>
  <key>EnvironmentVariables</key><dict><key>PATH</key><string>$PATH</string></dict>
  <key>StartInterval</key><integer>$INTERVAL</integer>
  <key>RunAtLoad</key><true/>
  <key>StandardErrorPath</key><string>$LOG</string>
</dict></plist>
PLISTEOF
        launchctl unload "$PLIST" 2>/dev/null
        launchctl load "$PLIST" && echo "Sincronizacao agendada a cada $(( INTERVAL / 60 )) min (log: $LOG). Desligar: kb.sh agendar uninstall" ;;
      uninstall) launchctl unload "$PLIST" 2>/dev/null; rm -f "$PLIST"; echo "Agendamento desligado." ;;
      status)
        if scheduled; then echo "agendamento: ligado"; else echo "agendamento: desligado"; fi
        tail -5 "$LOG" 2>/dev/null ;;
      *) die "uso: kb.sh agendar install|uninstall|status|run" ;;
    esac ;;
  *) sed -n '2,15p' "$0"; exit 1 ;;
esac
