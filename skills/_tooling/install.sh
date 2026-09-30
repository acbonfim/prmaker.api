#!/usr/bin/env bash
# Instalador das skills do PRMake para o Claude Code (features 0024/0035). Uso (copiado da tela "Skills" do PRMake):
#   macOS/Linux (ou Git Bash no Windows):
#     curl -fsSL -H "x-api-key: <sua api-key>" __PRMAKE_API_BASE__/Skills/install.sh | PRMAKE_TOKEN=<sua api-key> bash
#   Windows (PowerShell): a tela mostra o comando do install.ps1, que prepara Git/jq/Python e chama este script.
# Instala o que faltar (jq, unzip, python3 + venv) pelo gerenciador de pacotes do sistema — PRMAKE_NO_DEPS=1 não
# instala nada, só avisa. Baixa a ferramenta prmake-skills.sh para ~/.claude/skills/.prmake/ e instala todas as
# skills (ou só as informadas: ... | PRMAKE_TOKEN=... bash -s -- analisar-bug), com o hook de atualização automática.
set -euo pipefail

# Tudo dentro de main: com "curl | bash" o script vem pelo stdin — o bash lê o arquivo inteiro antes de executar e
# nenhum comando (apt, brew, pip…) consome o resto do script. Os gerenciadores também rodam com </dev/null.
main() {
BASE="${PRMAKE_API_BASE:-__PRMAKE_API_BASE__}"
TOKEN="${PRMAKE_TOKEN:-}"
[[ -n "$TOKEN" ]] || TOKEN="$(tr -d '\r\n' < "$HOME/.claude/prmake-token.txt" 2>/dev/null || true)"
[[ -n "$TOKEN" ]] || { echo "ERRO: informe a api-key: ... | PRMAKE_TOKEN=<sua api-key do PRMake> bash" >&2; exit 1; }
command -v curl >/dev/null || { echo "ERRO: instale o curl antes (Debian/Ubuntu: sudo apt-get install -y curl)" >&2; exit 1; }

case "$(uname -s 2>/dev/null)" in
  Darwin) OS=macos ;;
  MINGW*|MSYS*|CYGWIN*) OS=windows ;;
  *) OS=linux ;;
esac
# Onde vão os binários baixados (sem sudo). No Windows ~/bin também guarda os atalhos python3/jq da ferramenta.
if [[ $OS == windows ]]; then BIN="$HOME/bin"; export PATH="$HOME/bin:$PATH"; else BIN="$HOME/.local/bin"; export PATH="$PATH:$BIN"; fi

info() { echo "   $*"; }
warn() { echo "AVISO: $*" >&2; }
has()  { command -v "$1" >/dev/null 2>&1; }
# No Windows o python.org instala python.exe/py.exe e o "python3" da Microsoft Store é um atalho que não roda;
# a ferramenta cria o atalho python3 em ~/bin a partir de "py -3" ou "python".
PY=(python3)
if [[ $OS == windows ]] && ! python3 -c 'import sys' >/dev/null 2>&1; then
  if py -3 -c 'import sys' >/dev/null 2>&1; then PY=(py -3); else PY=(python); fi
fi
py_ok()   { "${PY[@]}" -c 'import sys; sys.exit(0 if sys.version_info[0] == 3 else 1)' >/dev/null 2>&1; }
venv_ok() { "${PY[@]}" -c 'import venv, ensurepip' >/dev/null 2>&1; }
jq_ok()   { has jq && jq -n 1 >/dev/null 2>&1; }

# Linha no arquivo de inicialização do shell (idempotente), para o Claude Code achar o que foi instalado em $BIN.
add_to_shell_rc() { # <linha>
  local rc="$HOME/.bashrc"
  [[ "${SHELL:-}" == */zsh ]] && rc="$HOME/.zshrc"
  grep -qsF "$1" "$rc" && return 0
  printf '\n# Skills do PRMake\n%s\n' "$1" >> "$rc"
  info "adicionado a $rc: $1"
}

# Pacotes que faltam, no nome de cada gerenciador.
MISSING=()
jq_ok || MISSING+=(jq)
has unzip || MISSING+=(unzip)
if ! py_ok; then MISSING+=(python3); elif ! venv_ok; then MISSING+=(python3-venv); fi

install_packages() {
  [[ ${#MISSING[@]} -gt 0 ]] || return 0
  if [[ "${PRMAKE_NO_DEPS:-0}" == "1" ]]; then warn "faltando: ${MISSING[*]} (PRMAKE_NO_DEPS=1 — não instalei)"; return 0; fi
  local sudo="" pkgs=() p
  [[ $(id -u 2>/dev/null || echo 1) -ne 0 ]] && has sudo && sudo="sudo"
  case "$OS" in
    macos)
      if ! has brew; then
        warn "faltando: ${MISSING[*]} e o Homebrew não está instalado (https://brew.sh)."
        [[ " ${MISSING[*]} " == *" python3 "* ]] && warn "python3 no macOS sem Homebrew: xcode-select --install"
        return 0
      fi
      for p in "${MISSING[@]}"; do case "$p" in python3|python3-venv) pkgs+=(python) ;; *) pkgs+=("$p") ;; esac; done
      echo "Instalando dependências com o Homebrew: ${pkgs[*]}"
      brew install "${pkgs[@]}" </dev/null || warn "brew install ${pkgs[*]} falhou"
      ;;
    linux)
      local pm="" cmd=()
      for pm in apt-get dnf yum pacman zypper apk; do has "$pm" && break; pm=""; done
      for p in "${MISSING[@]}"; do
        case "$pm:$p" in
          apt-get:python3-venv) pkgs+=(python3-venv) ;;
          apt-get:python3) pkgs+=(python3 python3-venv) ;;
          pacman:python3) pkgs+=(python) ;;
          *:python3-venv) ;;  # nos outros gerenciadores o venv vem com o python3
          *) pkgs+=("$p") ;;
        esac
      done
      [[ ${#pkgs[@]} -gt 0 ]] || return 0
      case "$pm" in
        apt-get) cmd=(env DEBIAN_FRONTEND=noninteractive apt-get install -y -q "${pkgs[@]}") ;;
        dnf|yum) cmd=("$pm" install -y -q "${pkgs[@]}") ;;
        pacman)  cmd=(pacman -S --needed --noconfirm "${pkgs[@]}") ;;
        zypper)  cmd=(zypper --non-interactive install "${pkgs[@]}") ;;
        apk)     cmd=(apk add --no-cache "${pkgs[@]}") ;;
        *) warn "faltando: ${pkgs[*]} — não achei o gerenciador de pacotes; instale manualmente."; return 0 ;;
      esac
      if [[ -z "$sudo" && $(id -u 2>/dev/null || echo 1) -ne 0 ]]; then
        warn "faltando: ${pkgs[*]} — sem sudo; rode como root: ${cmd[*]}"; return 0
      fi
      echo "Instalando dependências (${pkgs[*]}) com $pm${sudo:+ — o sudo pode pedir a sua senha}…"
      [[ "$pm" == apt-get ]] && { $sudo apt-get update -q </dev/null >/dev/null 2>&1 || true; }
      $sudo "${cmd[@]}" </dev/null || warn "falhou: ${sudo:+sudo }${cmd[*]}"
      ;;
    windows)
      # jq vem abaixo (download); Python pelo winget — o install.ps1 já faz isso antes de chamar este script.
      if [[ " ${MISSING[*]} " == *" python3 "* ]]; then
        warn "Python 3 não encontrado — no PowerShell: winget install -e --id Python.Python.3.12 (e abra um terminal novo)"
      fi
      ;;
  esac
}

# jq sem gerenciador (ou sem permissão): binário oficial em $BIN, sem sudo.
download_jq() {
  jq_ok && return 0
  [[ "${PRMAKE_NO_DEPS:-0}" == "1" ]] && return 0
  local arch asset
  arch="$(uname -m 2>/dev/null)"
  case "$arch" in arm64|aarch64) arch=arm64 ;; *) arch=amd64 ;; esac
  case "$OS" in
    macos) asset="jq-macos-$arch" ;;
    windows) asset="jq-windows-amd64.exe" ;;
    *) asset="jq-linux-$arch" ;;
  esac
  mkdir -p "$BIN"
  local target="$BIN/jq"; [[ $OS == windows ]] && target="$BIN/jq.exe"
  echo "Baixando o jq ($asset) para $BIN…"
  curl -fsSL --max-time 120 -o "$target" "https://github.com/jqlang/jq/releases/latest/download/$asset" || { rm -f "$target"; return 0; }
  chmod +x "$target"
  [[ $OS == windows ]] && add_to_shell_rc 'export PATH="$HOME/bin:$PATH"' || add_to_shell_rc 'export PATH="$PATH:$HOME/.local/bin"'
}

install_packages
download_jq
jq_ok || { echo "ERRO: não consegui instalar o jq — instale manualmente (https://jqlang.org/download) e rode de novo." >&2; exit 1; }
if ! py_ok && [[ $OS != windows ]]; then
  warn "python3 não encontrado — algumas skills (analisar-bug, gerar-prmake, base-solvace) precisam dele."
elif py_ok && ! venv_ok; then
  warn "python3 sem o módulo venv (Debian/Ubuntu: sudo apt-get install -y python3-venv) — as consultas SQL e o Knowledge Center não vão funcionar."
fi

TOOL_DIR="${CLAUDE_SKILLS_DIR:-$HOME/.claude/skills}/.prmake"
mkdir -p "$TOOL_DIR"
TMP="$(mktemp)"
CODE="$(curl -s --max-time 60 -o "$TMP" -w '%{http_code}' -H "x-api-key: $TOKEN" "$BASE/Skills/tool")"
[[ "$CODE" == "200" ]] || { rm -f "$TMP"; echo "ERRO: não consegui baixar a ferramenta (HTTP $CODE) — confira a api-key." >&2; exit 1; }
mv "$TMP" "$TOOL_DIR/prmake-skills.sh"
chmod +x "$TOOL_DIR/prmake-skills.sh"

echo "Instalando as skills do PRMake…"
PRMAKE_TOKEN="$TOKEN" PRMAKE_API_BASE="$BASE" bash "$TOOL_DIR/prmake-skills.sh" install "$@" </dev/null
}

main "$@"
