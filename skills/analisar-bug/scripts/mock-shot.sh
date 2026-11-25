#!/usr/bin/env bash
# 0075: print de um HTML de simulacao (mock Antes x Depois da analise) com o Chrome/Chromium/Edge headless.
#
# Uso: mock-shot.sh <mock.html> <saida.png> [--width 1440] [--height 900] [--mobile] [--scale 1]
#   --mobile = 390x844 (celular). A pagina e aberta como file:// — CSS/imagens por caminho absoluto funcionam.
# Env: PRMAKE_CHROME = caminho do navegador (quando nao esta no lugar padrao).
# Exit: 0 ok · 1 erro de uso/arquivo · 3 navegador nao encontrado (instale o Chrome ou defina PRMAKE_CHROME).
set -uo pipefail
# Windows/Git Bash (0035): caminhos do navegador e cygpath para file:// e saida.
WIN=0
case "$(uname -s 2>/dev/null)" in MINGW*|MSYS*|CYGWIN*) WIN=1; export PATH="$HOME/bin:$PATH" ;; esac

die() { echo "ERRO: $*" >&2; exit "${2:-1}"; }

HTML="${1:-}"; OUT="${2:-}"
[[ -n "$HTML" && -n "$OUT" ]] || die "uso: mock-shot.sh <mock.html> <saida.png> [--width N] [--height N] [--mobile] [--scale N]"
shift 2
W=1440; H=900; SCALE=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --width) W="${2:?}"; shift 2 ;;
    --height) H="${2:?}"; shift 2 ;;
    --scale) SCALE="${2:?}"; shift 2 ;;
    --mobile) W=390; H=844; SCALE=2; shift ;;
    *) die "opcao desconhecida: $1" ;;
  esac
done
[[ -f "$HTML" ]] || die "HTML nao encontrado: $HTML"
mkdir -p "$(dirname "$OUT")"
HTML_ABS="$(cd "$(dirname "$HTML")" && pwd)/$(basename "$HTML")"
OUT_ABS="$(cd "$(dirname "$OUT")" && pwd)/$(basename "$OUT")"

find_browser() {
  [[ -n "${PRMAKE_CHROME:-}" && -x "$PRMAKE_CHROME" ]] && { echo "$PRMAKE_CHROME"; return; }
  local c
  if [[ $WIN == 1 ]]; then
    for c in "${PROGRAMFILES:-}/Google/Chrome/Application/chrome.exe" "${LOCALAPPDATA:-}/Google/Chrome/Application/chrome.exe" \
             "$(printenv 'ProgramFiles(x86)' 2>/dev/null)/Google/Chrome/Application/chrome.exe" \
             "$(printenv 'ProgramFiles(x86)' 2>/dev/null)/Microsoft/Edge/Application/msedge.exe" "${PROGRAMFILES:-}/Microsoft/Edge/Application/msedge.exe"; do
      [[ -f "$c" ]] && { echo "$c"; return; }
    done
  fi
  for c in "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" "/Applications/Chromium.app/Contents/MacOS/Chromium" \
           "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"; do
    [[ -x "$c" ]] && { echo "$c"; return; }
  done
  for c in google-chrome google-chrome-stable chromium chromium-browser microsoft-edge msedge chrome; do
    command -v "$c" >/dev/null 2>&1 && { command -v "$c"; return; }
  done
}

BROWSER="$(find_browser)"
[[ -n "$BROWSER" ]] || die "Chrome/Chromium/Edge nao encontrado — instale o Chrome ou defina PRMAKE_CHROME=<caminho>" 3

URL="file://$HTML_ABS"; SHOT="$OUT_ABS"
PROFILE="$(mktemp -d "${TMPDIR:-/tmp}/mock-shot.XXXXXX")"
trap 'rm -rf "$PROFILE"' EXIT
if [[ $WIN == 1 ]] && command -v cygpath >/dev/null 2>&1; then
  URL="file:///$(cygpath -m "$HTML_ABS")"; SHOT="$(cygpath -w "$OUT_ABS")"; PROFILE_ARG="$(cygpath -w "$PROFILE")"
else
  PROFILE_ARG="$PROFILE"
fi

rm -f "$OUT_ABS"
"$BROWSER" --headless=new --disable-gpu --hide-scrollbars --no-first-run --no-default-browser-check \
  --allow-file-access-from-files --user-data-dir="$PROFILE_ARG" --force-device-scale-factor="$SCALE" \
  --window-size="$W,$H" --virtual-time-budget=3000 --screenshot="$SHOT" "$URL" >/dev/null 2>&1
[[ -s "$OUT_ABS" ]] || die "o navegador nao gerou o print ($BROWSER) — confira o HTML ($HTML_ABS)"
echo "OK $OUT_ABS (${W}x${H}${SCALE:+ @${SCALE}x}) — abra com Read e confira antes de anexar (upload ... image)"
