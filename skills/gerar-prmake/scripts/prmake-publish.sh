#!/usr/bin/env bash
# Publica o PR no PRMake, (para Bug) grava o Root Cause no DevOps e, se houver
# arquivo de comentario, posta o resumo nao-tecnico (PT/EN) na discussion do card.
# Uso: prmake-publish.sh <cardNumber> <branchPrefix> <descFile> [rcaFile] [repository]
#   <branchPrefix> = hotfix/  ou  bugfix/   (com a barra)
#   <descFile>     = arquivo com a descricao do PR (sem o bloco RCA)
#   [rcaFile]      = arquivo com o root cause (obrigatorio para Bug; omita/"" para US)
#   [repository]   = default: derivado do git remote origin; sem remote, o DefaultRepository do PRMake
# Env opcional:
#   COMMENT_FILE   = arquivo Markdown com o resumo nao-tecnico (PT/EN): publicado na discussion e gravado no PRMake
#   OPEN_GITHUB_PR = 1 para tambem abrir o PR no GitHub (exige TARGET_BRANCH; PR_TITLE e PR_DRAFT opcionais)
#   PRMAKE_BASE    = URL base da API (default https://api.softhouse.app.br/api/v1)
set -euo pipefail
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

# --- resolve repo slug: arg $5 > git remote origin (nome do repo) > DefaultRepository do PRMake (Skills Configurations)
resolve_repo() {
  if [[ -n "${1:-}" ]]; then printf '%s' "$1"; return; fi
  local url slug
  url="$(git remote get-url origin 2>/dev/null || true)"
  if [[ -n "$url" ]]; then
    slug="${url##*/}"; slug="${slug%.git}"
    if [[ -n "$slug" ]]; then printf '%s' "$slug"; return; fi
  fi
}

CARD="${1:?informe o numero do card}"
PREFIX="${2:?informe o branchPrefix (hotfix/ ou bugfix/)}"
DESC_FILE="${3:?informe o arquivo de descricao}"
RCA_FILE="${4:-}"
REPO="$(resolve_repo "${5:-}")"
FORM_ID="${FORM_ID:-1}"
BASE="${PRMAKE_BASE:-https://api.softhouse.app.br/api/v1}"

resolve_token() {
  if [[ -n "${PRMAKE_TOKEN:-}" ]]; then printf '%s' "$PRMAKE_TOKEN"; return; fi
  local candidates=(
    "$HOME/.claude/prmake-token.txt"
    "${CLAUDE_PROJECT_DIR:-}/.claude/prmake-token.txt"
    "$(git rev-parse --show-toplevel 2>/dev/null)/solvace-core/.claude/prmake-token.txt"
    "$(git rev-parse --show-toplevel 2>/dev/null)/.claude/prmake-token.txt"
    "$(cd "$(dirname "$0")/../.." && pwd)/prmake-token.txt"
  )
  for f in "${candidates[@]}"; do [[ -f "$f" ]] && { tr -d '\r\n' < "$f"; return; }; done
  echo "ERRO: token nao encontrado (defina PRMAKE_TOKEN ou crie ~/.claude/prmake-token.txt)" >&2; exit 1
}
TOKEN="$(resolve_token)"
if [[ -z "$REPO" ]]; then
  REPO="$(curl -s --max-time 30 -H "x-api-key: $TOKEN" "$BASE/Skills/config" | jq -r '.settings.DefaultRepository // empty' 2>/dev/null || true)"
  [[ -n "$REPO" ]] || { echo "ERRO: sem git remote e sem DefaultRepository no PRMake — informe o repositorio" >&2; exit 1; }
fi
USER_ID="$(printf '%s' "$TOKEN" | cut -d. -f2 | tr '_-' '/+' | base64 -d 2>/dev/null | sed -n 's/.*"ExternalId":"\([^"]*\)".*/\1/p')"
[[ -n "$USER_ID" ]] || { echo "ERRO: nao consegui extrair ExternalId do token" >&2; exit 1; }

RCA_CONTENT=""
[[ -n "$RCA_FILE" && -f "$RCA_FILE" ]] && RCA_CONTENT="$(cat "$RCA_FILE")"

# --- registro do card (um por card): so dados do card; branch/repo pertencem ao PR do GitHub ---
jq -n --rawfile desc "$DESC_FILE" --arg rca "$RCA_CONTENT" --arg uid "$USER_ID" \
   --arg card "$CARD" --argjson form "$FORM_ID" \
  '{description:$desc, cardNumber:$card, userId:$uid, formId:$form, rootCause:$rca}' > /tmp/prmake_body.json

echo ">> POST $BASE/PullRequest"
PR_CODE=$(curl -s --max-time 60 -o /tmp/prmake_pr_resp.json -w "%{http_code}" -X POST "$BASE/PullRequest" \
  -H 'accept: application/json' -H 'content-type: application/json' -H "x-api-key: $TOKEN" \
  --data @/tmp/prmake_body.json)
echo "   HTTP $PR_CODE (id: $(jq -r '.id // "?"' /tmp/prmake_pr_resp.json 2>/dev/null))"
[[ "$PR_CODE" =~ ^2 ]] || { echo "ERRO ao salvar o card no PRMake:"; cat /tmp/prmake_pr_resp.json; echo; exit 1; }

# --- PR no GitHub (opcional): OPEN_GITHUB_PR=1 + TARGET_BRANCH ---
# Abre o PR de <prefix><card> -> TARGET_BRANCH e registra no card (e na timeline).
# Se ja existir PR aberto para head->base, a API devolve o existente (alreadyExisted=true).
if [[ "${OPEN_GITHUB_PR:-0}" == "1" ]]; then
  TARGET="${TARGET_BRANCH:?defina TARGET_BRANCH (ex.: qa, master) para abrir o PR no GitHub}"
  if [[ -n "${PR_TITLE:-}" ]]; then GH_TITLE="$PR_TITLE"; else
    # Titulo pelo padrao do PRMake (Skills Configurations → PrTitlePattern: {card}, {TARGET}, {target}).
    PATTERN="$(curl -s --max-time 30 -H "x-api-key: $TOKEN" "$BASE/Skills/config" | jq -r '.settings.PrTitlePattern // empty' 2>/dev/null || true)"
    UPPER="$(printf '%s' "$TARGET" | tr '[:lower:]' '[:upper:]')"
    GH_TITLE="$(printf '%s' "${PATTERN:-{card\} {TARGET\}}" | sed -e "s|{card}|$CARD|g" -e "s|{TARGET}|$UPPER|g" -e "s|{target}|$TARGET|g")"
  fi
  jq -n --rawfile desc "$DESC_FILE" --arg uid "$USER_ID" --arg repo "$REPO" --arg prefix "$PREFIX" \
     --arg name "$CARD" --arg target "$TARGET" --arg title "$GH_TITLE" --argjson draft "${PR_DRAFT:-false}" \
    '{repositoryId:$repo, branchPrefix:$prefix, branchName:$name, targetBranch:$target,
      title:$title, description:$desc, draft:$draft, userId:$uid}' > /tmp/prmake_gh_body.json
  echo ">> POST $BASE/PullRequest/$CARD/github ($REPO: $PREFIX$CARD -> $TARGET)"
  GH_CODE=$(curl -s --max-time 90 -o /tmp/prmake_gh_resp.json -w "%{http_code}" -X POST "$BASE/PullRequest/$CARD/github" \
    -H 'accept: application/json' -H 'content-type: application/json' -H "x-api-key: $TOKEN" \
    --data @/tmp/prmake_gh_body.json)
  echo "   HTTP $GH_CODE (#$(jq -r '.number // "?"' /tmp/prmake_gh_resp.json 2>/dev/null), alreadyExisted: $(jq -r '.alreadyExisted // false' /tmp/prmake_gh_resp.json 2>/dev/null))"
  echo "   url: $(jq -r '.url // "?"' /tmp/prmake_gh_resp.json 2>/dev/null)"
  [[ "$GH_CODE" =~ ^2 ]] || { echo "ERRO ao abrir o PR no GitHub:"; cat /tmp/prmake_gh_resp.json; echo; exit 1; }
else
  echo ">> OPEN_GITHUB_PR!=1 - PR no GitHub nao aberto (abra pela tela ou rode com OPEN_GITHUB_PR=1 TARGET_BRANCH=<destino>)"
fi

# --- root cause no DevOps (somente se houver RCA) ---
if [[ -n "$RCA_CONTENT" ]]; then
  # o DevOps recebe o root cause convertido de Markdown -> HTML5
  RCA_HTML="$(printf '%s' "$RCA_CONTENT" | python3 "$(dirname "$0")/md2html.py")"
  jq -n --arg rca "$RCA_HTML" '{rootCause:$rca}' > /tmp/prmake_rca_body.json
  echo ">> POST $BASE/Azure/card/$CARD/rootcause"
  RC_CODE=$(curl -s --max-time 60 -o /tmp/prmake_rca_resp.json -w "%{http_code}" -X POST "$BASE/Azure/card/$CARD/rootcause" \
    -H 'accept: application/json' -H 'content-type: application/json' -H "x-api-key: $TOKEN" \
    --data @/tmp/prmake_rca_body.json)
  echo "   HTTP $RC_CODE (rev: $(jq -r '.rev // "?"' /tmp/prmake_rca_resp.json 2>/dev/null))"
  [[ "$RC_CODE" =~ ^2 ]] || { echo "ERRO ao gravar Root Cause" >&2; exit 1; }
else
  echo ">> sem RCA (User Story) - root cause no DevOps pulado"
fi

# --- resumo nao-tecnico (PT/EN) na discussion do card (se fornecido) ---
# Feature 0011 do CIME: POST /PullRequest/<card>/summary publica na discussion (cria o comentario ou
# ATUALIZA o mesmo ja publicado) e grava o resumo no PRMake (fica visivel/editavel na tela do card).
# Toda gravacao passa pelo PRMake (0028) — nada direto no Azure.
if [[ -n "${COMMENT_FILE:-}" && -f "$COMMENT_FILE" ]]; then
  if true; then
    SUMMARY_HTML="$(python3 "$(dirname "$0")/md2html.py" "$COMMENT_FILE")"
    jq -n --rawfile summary "$COMMENT_FILE" --arg html "$SUMMARY_HTML" '{summary:$summary, html:$html}' > /tmp/prmake_summary_body.json
    echo ">> POST $BASE/PullRequest/$CARD/summary (resumo nao-tecnico PT/EN)"
    SM_CODE=$(curl -s --max-time 90 -o /tmp/prmake_summary_resp.json -w "%{http_code}" -X POST "$BASE/PullRequest/$CARD/summary" \
      -H 'accept: application/json' -H 'content-type: application/json' -H "x-api-key: $TOKEN" \
      --data @/tmp/prmake_summary_body.json)
    echo "   HTTP $SM_CODE (commentId: $(jq -r '.summaryCommentId // "?"' /tmp/prmake_summary_resp.json 2>/dev/null))"
    [[ "$SM_CODE" =~ ^2 ]] || { echo "ERRO ao publicar o resumo:"; cat /tmp/prmake_summary_resp.json; echo; exit 1; }
  fi
else
  echo ">> sem COMMENT_FILE - resumo na discussion pulado"
fi

# --- classificacao do card (pelo PRMake: POST /Azure/card/<card>/actions/classify) ---
# Sempre preenchidos (Resolution Type / General Classification / Classification).
# Sobrescreva por env RESOLUTION_TYPE / GENERAL_CLASSIFICATION / CLASSIFICATION,
# ou defina SKIP_FIELDS=1 para pular esta etapa.
if [[ "${SKIP_FIELDS:-0}" != "1" ]]; then
  echo ">> preenchendo campos custom de classificacao no card $CARD"
  bash "$(dirname "$0")/azure-fields.sh" "$CARD"
else
  echo ">> SKIP_FIELDS=1 - campos custom pulados"
fi

echo "CONCLUIDO"
