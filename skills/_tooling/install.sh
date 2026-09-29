#!/usr/bin/env bash
# Instalador das skills do PRMake para o Claude Code (feature 0024). Uso (copiado da tela "Skills" do PRMake):
#   curl -fsSL -H "x-api-key: <sua api-key>" __PRMAKE_API_BASE__/Skills/install.sh | PRMAKE_TOKEN=<sua api-key> bash
# Baixa a ferramenta prmake-skills.sh para ~/.claude/skills/.prmake/ e instala todas as skills
# (ou só as informadas: ... | PRMAKE_TOKEN=... bash -s -- analisar-bug), com o hook de atualização automática.
set -euo pipefail

BASE="${PRMAKE_API_BASE:-__PRMAKE_API_BASE__}"
TOKEN="${PRMAKE_TOKEN:-}"
[[ -n "$TOKEN" ]] || TOKEN="$(tr -d '\n' < "$HOME/.claude/prmake-token.txt" 2>/dev/null || true)"
[[ -n "$TOKEN" ]] || { echo "ERRO: informe a api-key: ... | PRMAKE_TOKEN=<sua api-key do PRMake> bash" >&2; exit 1; }

for c in curl jq unzip rsync; do
  command -v "$c" >/dev/null || { echo "ERRO: instale '$c' antes (macOS: brew install $c)" >&2; exit 1; }
done
command -v python3 >/dev/null || echo "AVISO: python3 não encontrado — algumas skills (analisar-bug, gerar-prmake) precisam dele." >&2

TOOL_DIR="${CLAUDE_SKILLS_DIR:-$HOME/.claude/skills}/.prmake"
mkdir -p "$TOOL_DIR"
TMP="$(mktemp)"
CODE="$(curl -s --max-time 60 -o "$TMP" -w '%{http_code}' -H "x-api-key: $TOKEN" "$BASE/Skills/tool")"
[[ "$CODE" == "200" ]] || { rm -f "$TMP"; echo "ERRO: não consegui baixar a ferramenta (HTTP $CODE) — confira a api-key." >&2; exit 1; }
mv "$TMP" "$TOOL_DIR/prmake-skills.sh"
chmod +x "$TOOL_DIR/prmake-skills.sh"

echo "Instalando as skills do PRMake…"
PRMAKE_TOKEN="$TOKEN" PRMAKE_API_BASE="$BASE" bash "$TOOL_DIR/prmake-skills.sh" install "$@"
