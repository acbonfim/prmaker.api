#!/usr/bin/env bash
# Publicacao da engenharia reversa no PRMake (feature 0033) — escrita so para admin (api-key de admin).
#
# Uso: arch.sh <comando> [args]
#   list                                    projetos publicados (chave, tipo, commit, secoes)
#   project <chave> --name N --kind K [--repo URL] [--summary-file F] [--keywords "a,b"] [--repo-dir D] [--order N]
#           [--relations-file R.json] [--display-name N] [--tagline T] [--area A]
#                                           cria/atualiza o projeto; --repo-dir grava o commit/branch atual (HEAD);
#                                           --relations-file: JSON [{target, kind, detail, evidence}] (substitui as relacoes);
#                                           --display-name/--tagline/--area: nome, frase e area para pessoas (0038)
#   section <chave> <secao> <arquivo.md> [--title T] [--order N] [--note N] [--source skill|admin] [--audience llm|human]
#                                           grava a secao (conteudo igual nao cria versao); human = Guia para pessoas, fica
#                                           fora do espelho das skills (secao guia-* ja nasce human)
#   get <chave> [secao]                     metadados do projeto ou o conteudo de uma secao
#   stale <chave> <repo-dir|repo>           o que mudou no repositorio desde o commit mapeado (para atualizar); aceita o
#                                           nome do repositorio (pasta pelo mapa da maquina — 0048)
#   publicar-pasta <pasta-kb> [projeto...]  publica cada <pasta-kb>/<chave>/ (projeto.json com relations + NNN-secao.md);
#                                           repoDir relativo a SOLVACE_REPOS (padrao ~/repos/solvace) grava o commit
#   suggest <chave> <secao|-> <arquivo.md> [--kind learning|divergence|gap|kc] [--card N] [--item RN-012]
#                                           (0054: --item = o item da engenharia reversa; kc = divergencia Knowledge Center x codigo)
#                                           PROPOE uma melhoria (qualquer usuario): vai para a fila do admin no PRMake,
#                                           nunca grava na secao
#   learn <card> [--instructions T] [--send 1,3|all]
#                                           APRENDER COM UM CARD (0038): o PRMake junta DevOps, PR/RCA, Timeline e planos e a
#                                           IA propoe aprendizados; --send envia as propostas escolhidas para a fila
#   guia <chave> <pasta> [--instructions T] GUIA COM A IA (admin, 0038): grava a proposta em <pasta>/<chave>/5NN-guia-*.md +
#                                           guia.json (nome/frase/area) para revisar e publicar com publicar-pasta
#                                           learn e guia CUSTAM creditos de API do perfil de quem chama (0042: o custo
#                                           aparece no fim); em massa, escreva no Claude Code e publique com section
#   lacunas                                 sugestoes do tipo gap pendentes (admin): perguntas que a base nao cobre — analise o
#                                           codigo, publique a secao e rode: resolver <id> applied
#   resolver <id> applied|dismissed [nota]  resolve uma sugestao da fila (admin)
#   perguntas [open|answered|all] [--todas] PERGUNTAS DO "PERGUNTE" sem resposta (admin, 0040): as mais perguntadas primeiro;
#                                           --todas inclui as que a base ja respondia
#   pergunta-respondida <id> <projeto> <secao> [nota]   marca a pergunta como respondida pela secao publicada
#   pergunta-descartar <id> [nota]          descarta (fora do escopo da base)
# Tipos: ecosystem legacy frontend integration revamp infra third-party auth business-rules other
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
CMD="${1:-}"; shift || true
die() { echo "ERRO: $*" >&2; exit 1; }
SKILLS_TOOL="${PRMAKE_SKILLS_TOOL:-$HOME/.claude/skills/.prmake/prmake-skills.sh}"
command -v jq >/dev/null || die "jq nao encontrado"
if [[ -n "${PRMAKE_TOKEN:-}" ]]; then TK="$PRMAKE_TOKEN"; elif [[ -f "$HOME/.claude/prmake-token.txt" ]]; then TK="$(tr -d '\r\n' < "$HOME/.claude/prmake-token.txt")"; else die "token do PRMake nao encontrado"; fi
TMP="$(mktemp -d "${TMPDIR:-/tmp}/arch.XXXXXX")"; trap 'rm -rf "$TMP"' EXIT

api() { # <METHOD> <path> [body-file]
  local args=(-s --max-time "${ARCH_TIMEOUT:-60}" -o "$TMP/resp" -D "$TMP/headers" -w '%{http_code}' -X "$1" "$BASE/Architecture$2" -H "x-api-key: $TK" -H 'accept: application/json')
  [[ -n "${3:-}" ]] && args+=(-H 'content-type: application/json' --data-binary "@$3")
  CODE="$(curl "${args[@]}" 2>/dev/null)"; CODE="${CODE:-000}"
}
# Consumo de IA da chamada (0042): o PRMake devolve X-AI-Usage (calls;in;out;cost;model) quando a acao usou IA.
ai_cost() {
  local h; h="$(tr -d '\r' < "$TMP/headers" 2>/dev/null | sed -n 's/^[Xx]-[Aa][Ii]-[Uu]sage: *//p' | tail -1)"
  [[ -n "$h" ]] || return 0
  awk -v h="$h" 'BEGIN { n = split(h, kv, ";"); for (i = 1; i <= n; i++) { split(kv[i], p, "="); v[p[1]] = p[2] }
    c = (v["cost"] == "") ? "custo desconhecido (modelo fora da tabela)" : sprintf("~ US$ %.4f", v["cost"])
    printf "IA: %d chamada(s), %d tokens de entrada + %d de saida, %s (%s) - creditos de API de quem chamou\n", v["calls"], v["in"], v["out"], c, v["model"] }' >&2
}
AI_WARN="ATENCAO: usa a IA do PRMake com a chave de API do SEU perfil (creditos pagos, nao a assinatura do Claude Code). Para gerar em massa, escreva o conteudo no Claude Code e publique com 'section'/'publicar-pasta'."
check() { [[ "$CODE" =~ ^2 ]] || die "HTTP $CODE: $(jq -r '.error // .title // .' "$TMP/resp" 2>/dev/null | head -c 300)"; }
opt() { # <nome> <padrao> <args...>
  local name="$1" def="$2"; shift 2
  while [[ $# -gt 0 ]]; do [[ "$1" == "$name" ]] && { printf '%s' "${2:-}"; return; }; shift; done
  printf '%s' "$def"
}

case "$CMD" in
  list)
    api GET /projects; check
    jq -r '.[] | "\(.key)\t\(.kind)\t\(.sourceCommit // "-" | .[0:8])\t\(.sections | length) secoes\t\(.name)"' "$TMP/resp" | column -t -s $'\t' ;;
  project)
    KEY="${1:?chave}"; shift
    NAME="$(opt --name "" "$@")"; [[ -n "$NAME" ]] || die "--name obrigatorio"
    SUMMARY=""; SF="$(opt --summary-file "" "$@")"; [[ -n "$SF" ]] && { [[ -f "$SF" ]] || die "resumo nao encontrado: $SF"; SUMMARY="$(cat "$SF")"; }
    RD="$(opt --repo-dir "" "$@")"; COMMIT=""; BRANCH=""
    if [[ -n "$RD" ]]; then COMMIT="$(git -C "$RD" rev-parse HEAD 2>/dev/null)" || die "$RD nao e um repositorio git"; BRANCH="$(git -C "$RD" rev-parse --abbrev-ref HEAD)"; fi
    ORDER="$(opt --order "" "$@")"
    RF="$(opt --relations-file "" "$@")"; if [[ -n "$RF" ]]; then [[ -f "$RF" ]] || die "relacoes nao encontradas: $RF"; RELS="$(cat "$RF")"; else RELS="null"; fi
    jq -n --argjson rels "$RELS" --arg name "$NAME" --arg kind "$(opt --kind other "$@")" --arg repo "$(opt --repo "" "$@")" --arg summary "$SUMMARY" \
      --arg kw "$(opt --keywords "" "$@")" --arg commit "$COMMIT" --arg branch "$BRANCH" --arg order "$ORDER" \
      --arg dn "$(opt --display-name "" "$@")" --arg tl "$(opt --tagline "" "$@")" --arg area "$(opt --area "" "$@")" \
      'def nn: if . == "" then null else . end;
       {name:$name, kind:$kind, repository:($repo|nn), summary:($summary|nn),
        keywords:($kw|split(",")|map(gsub("^\\s+|\\s+$";""))|map(select(.!=""))),
        sourceCommit:($commit|nn), sourceBranch:($branch|nn), order:($order|nn|if . == null then null else tonumber end), relations:$rels,
        displayName:($dn|nn), tagline:($tl|nn), businessArea:($area|nn)}' > "$TMP/body"
    api PUT "/projects/$KEY" "$TMP/body"; check
    jq -r '"OK projeto \(.key) (\(.kind)) — commit \(.sourceCommit // "-" | .[0:8]) · \(.sections|length) secoes · \(.relations|length) relacoes"' "$TMP/resp" ;;
  section)
    KEY="${1:?chave}"; SEC="${2:?secao}"; FILE="${3:?arquivo.md}"; shift 3
    [[ -f "$FILE" ]] || die "arquivo nao encontrado: $FILE"
    ORDER="$(opt --order "" "$@")"
    jq -n --rawfile content "$FILE" --arg title "$(opt --title "" "$@")" --arg note "$(opt --note "" "$@")" \
      --arg source "$(opt --source skill "$@")" --arg order "$ORDER" --arg aud "$(opt --audience "" "$@")" \
      'def nn: if . == "" then null else . end;
       {content:$content, title:($title|nn), note:($note|nn), source:$source, order:($order|nn|if . == null then null else tonumber end),
        audience:($aud|nn)}' > "$TMP/body"
    api PUT "/projects/$KEY/sections/$SEC" "$TMP/body"; check
    jq -r '"OK \(.key) v\(.version) (\(.length) caracteres, \(if .audience == "human" then "Guia" else "tecnica" end))"' "$TMP/resp" ;;
  get)
    KEY="${1:?chave}"
    if [[ -n "${2:-}" ]]; then api GET "/projects/$KEY/sections/$2"; check; jq -r '.content' "$TMP/resp"
    else api GET "/projects/$KEY"; check; jq -r '"\(.name) (\(.key), \(.kind)) commit \(.sourceCommit // "-") em \(.sourceMappedAt // "-")\n\(.summary // "")\nsecoes: \([.sections[] | "\(.key) v\(.version)"] | join(", "))"' "$TMP/resp"; fi ;;
  stale)
    KEY="${1:?chave}"; RD="${2:?repo-dir (ou o nome do repositorio)}"
    # 0048: aceita o nome do repositorio (pasta pelo mapa da maquina).
    if [[ ! -d "$RD" && -f "$SKILLS_TOOL" ]]; then RD="$(bash "$SKILLS_TOOL" repos path "$RD")" || die "repositorio fora do mapa desta maquina: ${2}"; fi
    api GET "/projects/$KEY"; check
    C="$(jq -r '.sourceCommit // empty' "$TMP/resp")"
    [[ -n "$C" ]] || { echo "projeto sem commit registrado — mapeie do zero"; exit 0; }
    git -C "$RD" cat-file -e "$C^{commit}" 2>/dev/null || { echo "commit $C nao existe no repositorio local (git fetch?) — mapeie do zero"; exit 0; }
    N="$(git -C "$RD" rev-list --count "$C..HEAD")"
    echo "$N commit(s) desde ${C:0:8}"; [[ "$N" == "0" ]] && exit 0
    git -C "$RD" diff --stat "$C..HEAD" | tail -1
    echo "pastas alteradas (as secoes que falam delas precisam de revisao):"
    git -C "$RD" diff --name-only "$C..HEAD" | awk -F/ '{print $1"/"$2}' | sort | uniq -c | sort -rn | head -25 ;;
  suggest)
    KEY="${1:?chave}"; SEC="${2:?secao (ou - para o projeto)}"; FILE="${3:?arquivo.md}"; shift 3
    [[ -f "$FILE" ]] || die "arquivo nao encontrado: $FILE"
    jq -n --arg p "$KEY" --arg s "$SEC" --rawfile c "$FILE" --arg k "$(opt --kind learning "$@")" --arg card "$(opt --card "" "$@")" \
      --arg item "$(opt --item "" "$@")" \
      '{projectKey:$p, sectionKey:(if $s == "-" then null else $s end), kind:$k, content:$c, cardNumber:(if $card == "" then null else $card end)}
       + (if $item == "" then {} else {itemId:$item} end)' > "$TMP/body"
    api POST /suggestions "$TMP/body"; check
    echo "OK sugestao registrada para $KEY${SEC:+/$SEC} — o admin aplica ou descarta na tela Base Solvace" ;;
  publicar-pasta)
    KB="${1:?pasta-kb}"; shift
    REPOS="${SOLVACE_REPOS:-$HOME/repos/solvace}"
    title_of() { case "$1" in
      visao-geral) echo "Visão geral";; modulos) echo "Módulos e fluxos";; dados) echo "Dados";; integracoes) echo "Integrações";;
      infra) echo "Infra e AWS";; autenticacao) echo "Login e permissões";; jobs) echo "Jobs e rotinas";;
      regras-de-negocio) echo "Regras de negócio";; armadilhas) echo "Armadilhas e bugs conhecidos";;
      guia-o-que-e) echo "O que é e para que serve";; guia-como-funciona) echo "Como funciona, passo a passo";;
      guia-regras) echo "Regras de negócio";; guia-conexoes) echo "Com quem conversa";; guia-como-testar) echo "Como testar";;
      guia-perguntas) echo "Perguntas frequentes";; guia-glossario) echo "Glossário";; guia-como-configurar) echo "Como configurar e dar acesso";;
      operacao) echo "Configuração e operação";; catalogo-modulos) echo "Catálogo de módulos e onde configurar";; parametros) echo "Parâmetros globais e de planta";;
      *) echo "$1";; esac; }
    projects=("$@"); [[ ${#projects[@]} -gt 0 ]] || projects=($(cd "$KB" && ls -d */ 2>/dev/null | tr -d / | grep -v '^_'))
    SELF="$0"
    for key in "${projects[@]}"; do
      meta="$KB/$key/projeto.json"; [[ -f "$meta" ]] || { echo "sem $meta" >&2; continue; }
      jq -r '.summary // ""' "$meta" > "$TMP/summary.md"
      jq -c '.relations // null' "$meta" > "$TMP/rels.json"
      args=(--name "$(jq -r .name "$meta")" --kind "$(jq -r .kind "$meta")" --summary-file "$TMP/summary.md"
            --keywords "$(jq -r '(.keywords // []) | join(",")' "$meta")" --order "$(jq -r '.order // 0' "$meta")")
      [[ "$(cat "$TMP/rels.json")" != "null" ]] && args+=(--relations-file "$TMP/rels.json")
      repo="$(jq -r '.repository // empty' "$meta")"; [[ -n "$repo" ]] && args+=(--repo "$repo")
      for f in displayName:--display-name tagline:--tagline businessArea:--area; do
        v="$(jq -r --arg k "${f%%:*}" '.[$k] // empty' "$meta")"; [[ -n "$v" ]] && args+=("${f#*:}" "$v")
      done
      dir="$(jq -r '.repoDir // empty' "$meta")"
      if [[ -n "$dir" && -d "$REPOS/$dir/.git" ]]; then args+=(--repo-dir "$REPOS/$dir")
      elif [[ -n "$dir" && -f "$SKILLS_TOOL" ]] && rd="$(bash "$SKILLS_TOOL" repos path "$(basename "$dir")" 2>/dev/null)"; then
        args+=(--repo-dir "$rd")  # 0048: fora de SOLVACE_REPOS, pelo mapa da maquina
      fi
      bash "$SELF" project "$key" "${args[@]}" || continue
      for f in "$KB/$key"/[0-9][0-9][0-9]-*.md; do
        [[ -f "$f" ]] || continue
        name="$(basename "$f" .md)"; order="${name%%-*}"; section="${name#*-}"
        aud=llm; [[ "$section" == guia-* ]] && aud=human
        bash "$SELF" section "$key" "$section" "$f" --title "$(title_of "$section")" --order "$((10#$order))" --audience "$aud" --note "${KB_NOTE:-engenharia reversa (base-solvace)}" >/dev/null && printf '.'
      done; echo
    done ;;
  learn)
    CARD="${1:?card}"; shift
    jq -n --arg c "$CARD" --arg i "$(opt --instructions "" "$@")" '{cardNumber:$c, instructions:(if $i == "" then null else $i end)}' > "$TMP/body"
    echo "$AI_WARN" >&2
    echo "lendo o card $CARD no PRMake (DevOps, PR/RCA, Timeline, planos) — pode levar alguns minutos..." >&2
    ARCH_TIMEOUT="${ARCH_TIMEOUT:-300}" api POST /learn-from-card "$TMP/body"; ai_cost; check
    cp "$TMP/resp" "$TMP/learn.json"
    jq -r '"Card \(.cardNumber): \(.cardTitle // "-")",
      "Fontes: \([.sources[] | "\(if .ok then "✓" else "✗" end) \(.label)\(if .detail then " (\(.detail))" else "" end)"] | join(" · "))",
      (if (.existing | length) > 0 then "JA SUGERIDO para este card: \(.existing | length) — \([.existing[] | "\(.projectKey)/\(.sectionKey // "-") [\(.status)]"] | join(", "))" else empty end),
      (if .aiUnavailableReason then "IA indisponivel: \(.aiUnavailableReason)" else empty end),
      "", "Resumo: \(.summary // "-")", "",
      (.proposals | to_entries[] | "[\(.key + 1)] \(.value.projectKey // "?")/\(.value.sectionKey // "-") (\(if .value.audience == "human" then "Guia" else "tecnica" end)) — \(.value.title)\n    motivo: \(.value.reason // "-")\n\(.value.content | split("\n") | map("    " + .) | join("\n"))\n")' "$TMP/learn.json"
    SEND="$(opt --send "" "$@")"
    [[ -n "$SEND" ]] || { echo "(para enviar: arch.sh learn $CARD --send 1,2 ou --send all)"; exit 0; }
    N="$(jq '.proposals | length' "$TMP/learn.json")"
    [[ "$SEND" == all ]] && SEND="$(seq -s, 1 "$N")"
    for i in ${SEND//,/ }; do
      jq -e --argjson i "$i" '.proposals[$i - 1] and (.proposals[$i - 1].projectKey != "")' "$TMP/learn.json" >/dev/null || { echo "proposta $i invalida ou sem projeto — pulei" >&2; continue; }
      jq --argjson i "$i" '.proposals[$i - 1] as $p | {projectKey:$p.projectKey, sectionKey:$p.sectionKey, kind:"learning", cardNumber:.cardNumber,
        content:("**" + $p.title + "**\n" + (if $p.audience == "human" then "Público: Guia (linguagem simples)\n" else "" end) + "\n" + $p.content)}' "$TMP/learn.json" > "$TMP/body"
      api POST /suggestions "$TMP/body"; check; echo "OK proposta $i enviada para a fila"
    done ;;
  guia)
    KEY="${1:?chave}"; OUT="${2:?pasta}"; shift 2
    jq -n --arg i "$(opt --instructions "" "$@")" '{instructions:(if $i == "" then null else $i end)}' > "$TMP/body"
    echo "$AI_WARN" >&2
    echo "gerando o Guia de $KEY com a IA do PRMake (projeto grande leva alguns minutos)..." >&2
    ARCH_TIMEOUT="${ARCH_TIMEOUT:-300}" api POST "/projects/$KEY/guide" "$TMP/body"; ai_cost; check
    mkdir -p "$OUT/$KEY"
    jq '{displayName, tagline, businessArea, notes}' "$TMP/resp" > "$OUT/$KEY/guia.json"
    jq -r '.sections[] | @base64' "$TMP/resp" | while read -r row; do
      sec="$(printf '%s' "$row" | base64 --decode)"
      f="$OUT/$KEY/$(printf '%03d' "$(jq -r .order <<<"$sec")")-$(jq -r .key <<<"$sec").md"
      jq -r .content <<<"$sec" > "$f"; echo "  $f"
    done
    echo "dados para pessoas e notas: $OUT/$KEY/guia.json — revise; publique cada secao com: arch.sh section $KEY <guia-...> <arquivo> --audience human" ;;
  lacunas)
    api GET "/suggestions?status=pending"; check
    jq -r '[.[] | select(.kind == "gap")] | if length == 0 then "nenhuma lacuna pendente" else .[] | "\(.id)  \(.projectKey)/\(.sectionKey // "-")  por \(.createdBy) em \(.createdAt[0:10])\n\(.content | split("\n") | map("    " + .) | join("\n"))\n" end' "$TMP/resp" ;;
  perguntas)
    ST="${1:-open}"; [[ "$ST" == --todas ]] && ST=open; GAPS=true; for a in "$@"; do [[ "$a" == --todas ]] && GAPS=false; done
    api GET "/questions?status=$ST&gaps=$GAPS"; check
    jq -r 'if length == 0 then "nenhuma pergunta pendente" else .[] | "\(.id)  \(.times)x  [\(.coverage)/\(.kind)]  \(.text)\n    ultima: \(.lastAskedBy) em \(.lastAskedAt[0:10])\(if .suggestedProject then " · base sugeriu \(.suggestedProject)/\(.suggestedSection // "-")" else "" end)" end' "$TMP/resp" ;;
  pergunta-respondida|pergunta-descartar)
    ID="${1:?id}"
    if [[ "$CMD" == pergunta-respondida ]]; then
      jq -n --arg p "${2:?projeto}" --arg s "${3:?secao}" --arg n "${4:-}" '{status:"answered", projectKey:$p, sectionKey:$s, note:(if $n == "" then null else $n end)}' > "$TMP/body"
    else
      jq -n --arg n "${2:-}" '{status:"dismissed", note:(if $n == "" then null else $n end)}' > "$TMP/body"
    fi
    api POST "/questions/$ID/resolve" "$TMP/body"; check; jq -r '"OK pergunta \(.id) -> \(.status)"' "$TMP/resp" ;;
  resolver)
    ID="${1:?id}"; ST="${2:?applied|dismissed}"
    jq -n --arg s "$ST" --arg n "${3:-}" '{status:$s, note:(if $n == "" then null else $n end)}' > "$TMP/body"
    api POST "/suggestions/$ID/resolve" "$TMP/body"; check; echo "OK sugestao $ID -> $ST" ;;
  *) sed -n '2,33p' "$0"; exit 1 ;;
esac
