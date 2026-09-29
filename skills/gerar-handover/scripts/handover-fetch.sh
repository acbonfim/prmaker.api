#!/usr/bin/env bash
# Busca (somente leitura) tudo que e preciso para gerar a Passagem de Conhecimento (handover) de um card.
# Uso: handover-fetch.sh <cardNumber>
# Saida: escreve arquivos em $OUTDIR (default /tmp/handover) e imprime um manifesto.
#   template.md          - layout do formulario (plugin AI Configurations id=3, TemplatePassagemConhecimento)
#   card.json            - GET Azure/card/<card>/full (campos, comentarios, historico, alertas)
#   pr.json              - GET PullRequest/GetByCardNumber ({} se nao houver)
#   timeline.json        - GET Timeline/card/<card> ([] se nao houver)
#   handover_atual.json  - GET Handover/GetByCardNumber ({} se nao houver)
#   context.json         - dados cruzados no mesmo formato que a tela envia para a IA
#   prompt.txt           - prompt completo (regras + layout + dados), igual ao da tela
set -euo pipefail

CARD="${1:?informe o numero do card}"
BASE="${PRMAKE_BASE:-https://api.softhouse.app.br/api/v1}"
OUTDIR="${OUTDIR:-/tmp/handover}"
PLUGIN_ID="${PLUGIN_ID:-3}"
mkdir -p "$OUTDIR"

resolve_token() {
  if [[ -n "${PRMAKE_TOKEN:-}" ]]; then printf '%s' "$PRMAKE_TOKEN"; return; fi
  local candidates=(
    "$HOME/.claude/prmake-token.txt"
    "${CLAUDE_PROJECT_DIR:-}/.claude/prmake-token.txt"
    "$(git rev-parse --show-toplevel 2>/dev/null)/.claude/prmake-token.txt"
  )
  for f in "${candidates[@]}"; do [[ -f "$f" ]] && { tr -d '\n' < "$f"; return; }; done
  echo "ERRO: token nao encontrado (defina PRMAKE_TOKEN ou crie ~/.claude/prmake-token.txt)" >&2; exit 1
}
TOKEN="$(resolve_token)"
AUTH=(-H "accept: application/json" -H "x-api-key: $TOKEN")
CARD_ENC="$(jq -rn --arg c "$CARD" '$c|@uri')"

get() { curl -s --max-time 90 -w '\n%{http_code}' "${AUTH[@]}" "$1"; }
# fetch <url> <arquivo> <label> <obrigatorio 1|0> <vazio-default>
fetch() {
  local resp code body
  resp="$(get "$1")"; code="${resp##*$'\n'}"; body="${resp%$'\n'*}"
  if [[ "$code" == "200" && -n "$body" && "$body" != "null" ]]; then
    printf '%s' "$body" > "$2"; return 0
  fi
  if [[ "$4" == "1" ]]; then
    echo "ERRO: $3 retornou HTTP $code: ${body:0:300}" >&2
    [[ "$code" == "401" ]] && echo "   -> token PRMake expirado (~/.claude/prmake-token.txt ou PRMAKE_TOKEN)" >&2
    [[ "$body" == *PERSONAL_INTEGRATION_REQUIRED* ]] && echo "   -> salve sua chave do DevOps em 'Minhas integracoes' no app" >&2
    exit 1
  fi
  printf '%s' "$5" > "$2"
}

# --- 1) layout do formulario (sempre o mais atual) ---
fetch "$BASE/PluginConfiguration/get-all-by-id?id=$PLUGIN_ID" "$OUTDIR/plugin.json" "GET plugin $PLUGIN_ID" 1 ''
jq -r '.configurations.TemplatePassagemConhecimento // ""' "$OUTDIR/plugin.json" > "$OUTDIR/template.md"
if [[ ! -s "$OUTDIR/template.md" ]] || ! grep -q '[^[:space:]]' "$OUTDIR/template.md"; then
  echo "ERRO: TemplatePassagemConhecimento vazio no plugin id=$PLUGIN_ID (cadastre o layout em Configuracoes > Plugins > AI Configurations)" >&2
  exit 1
fi

# --- 2) card completo no DevOps ---
fetch "$BASE/Azure/card/$CARD_ENC/full" "$OUTDIR/card.json" "GET Azure/card/$CARD/full" 1 ''
if [[ "$(jq -r '(.id // 0) != 0 and (.error // null) == null' "$OUTDIR/card.json")" != "true" ]]; then
  echo "ERRO: card $CARD nao encontrado no DevOps: $(jq -c '.error // .' "$OUTDIR/card.json" | head -c 300)" >&2
  exit 1
fi

# --- 3) PR salvo, 4) timeline, 5) handover atual (opcionais) ---
fetch "$BASE/PullRequest/GetByCardNumber?cardNumber=$CARD_ENC" "$OUTDIR/pr.json" "GET PullRequest" 0 '{}'
fetch "$BASE/Timeline/card/$CARD_ENC" "$OUTDIR/timeline.json" "GET Timeline" 0 '[]'
fetch "$BASE/Handover/GetByCardNumber?cardNumber=$CARD_ENC" "$OUTDIR/handover_atual.json" "GET Handover" 0 '{}'

# --- contexto no mesmo formato da tela (handover-dialog.component.ts > buildPrompt) ---
jq -n --arg card "$CARD" \
      --slurpfile cf "$OUTDIR/card.json" \
      --slurpfile pr "$OUTDIR/pr.json" \
      --slurpfile tl "$OUTDIR/timeline.json" '
  ($cf[0]) as $c | ($pr[0] // {}) as $p | ($tl[0] // []) as $t |
  {
    card: {
      numero: $card,
      titulo: ($c.fields["System.Title"] // ""),
      url: $c.url,
      campos: $c.fields,
      comentarios: $c.comments,
      historicoDeAlteracoes: $c.history,
      alertas: $c.alerts
    },
    pullRequestSalvo: {
      descricao: $p.description,
      rootCause: $p.rootCause,
      branch: (($p.branchPrefix // "") + ($p.branchName // "")),
      repositorio: $p.repositoryId
    },
    linhaDoTempo: [ $t[] | { quando: .createdAt, quem: .userName, registro: .description } ]
  }' > "$OUTDIR/context.json"

# --- prompt (mesmo texto da tela) ---
{
  cat <<'EOF'
Você é um assistente que preenche um formulário de PASSAGEM DE CONHECIMENTO (handover) de um card de bug, para que o próximo turno dê continuidade ao tratamento sem perder contexto.

Preencha EXATAMENTE o layout abaixo, mantendo todos os títulos e a estrutura, substituindo os espaços em branco pelas informações reais, cruzando TODOS os dados fornecidos (card do DevOps, histórico de alterações, comentários, Pull Request salvo e linha do tempo). Onde não houver informação, escreva "—".

Regras:
- Responda em português.
- Saída em MARKDOWN, contendo APENAS o formulário preenchido, sem nenhum texto antes ou depois e sem blocos de código (```).
- Seja objetivo e técnico; priorize o que ajuda o próximo turno a continuar (o que já foi investigado, onde está o problema, o que foi descartado, causa provável e próximo passo).

LAYOUT A PREENCHER:
EOF
  cat "$OUTDIR/template.md"
  printf '\n\nDADOS DISPONÍVEIS (JSON):\n'
  cat "$OUTDIR/context.json"
  printf '\n'
} > "$OUTDIR/prompt.txt"

# --- manifesto ---
cat <<EOF
OK
card=$CARD
titulo=$(jq -r '.fields["System.Title"] // ""' "$OUTDIR/card.json")
workItemType=$(jq -r '.fields["System.WorkItemType"] // ""' "$OUTDIR/card.json")
estado=$(jq -r '.fields["System.State"] // ""' "$OUTDIR/card.json")
comentarios=$(jq '.comments | length' "$OUTDIR/card.json")
historico=$(jq '.history | length' "$OUTDIR/card.json")
prSalvo=$(jq -r 'if (.id // 0) != 0 then "sim" else "nao" end' "$OUTDIR/pr.json")
repositoryId=$(jq -r '.repositoryId // ""' "$OUTDIR/pr.json")
timeline=$(jq 'length' "$OUTDIR/timeline.json")
handoverExistente=$(jq -r 'if (.content // "") != "" then "sim (isPublic=\(.isPublic), atualizado=\(.updatedAt // .createdAt))" else "nao" end' "$OUTDIR/handover_atual.json")
templateLinhas=$(wc -l < "$OUTDIR/template.md" | tr -d ' ')
arquivos=$OUTDIR/{template.md,context.json,prompt.txt,card.json,pr.json,timeline.json,handover_atual.json}
EOF
