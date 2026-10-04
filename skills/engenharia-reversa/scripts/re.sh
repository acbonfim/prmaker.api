#!/usr/bin/env bash
# Engenharia reversa por modulo (feature 0052) — cliente da skill: sessao (rascunho) no PRMake, inventario do codigo,
# checagem (estrutura + cobertura) e envio para aprovacao. Quem aprova/publica e o PRMake (tela Engenharia reversa).
#
# Uso: re.sh <comando> [args]
#   modulo [<chave>]                  resolve o modulo: chave informada, ou pelo repositorio da pasta atual (fontes/apelidos)
#   status <modulo>                   documentos (publicado, revisao aberta, nota do revisor), fontes e anexos
#   tipos                             tipos de documento (chave, titulo, itens, exigido)
#   start <modulo> <doc> [new|improve|redo]
#                                     abre/retoma a sessao do documento e grava o pacote em $RE_HOME/<modulo>/<doc>/:
#                                     modelo.md, publicado.md, sugestoes.md, relacionados.md, nota-revisor.md, anexos/,
#                                     documento.md (o rascunho — escreva aqui) e sessao.json
#   fontes <modulo> [--path repo=subpasta ...]
#                                     pastas locais das fontes (mapa da maquina); legado sem subpasta = erro (o
#                                     edv-solvace inteiro nao e um modulo)
#   inventario <modulo> [--path repo=subpasta ...]
#                                     inventario deterministico do codigo -> $RE_HOME/<modulo>/inventario.json
#   banco <modulo> [--prefix TB_X_ ...] [--sigla X]
#                                     CATALOGO DO BANCO DA DEMO (0053, somente leitura): views, procedures, functions,
#                                     triggers, tabelas (colunas, chaves, checks) e jobs do SQL Agent do modulo, global e
#                                     locais -> $RE_HOME/<modulo>/banco/ (+ inventario-banco.json para a cobertura)
#   termos <modulo>                   termos do modulo para o GLOSSARIO (rotulos da tela, menus, siglas, traducoes)
#   trabalho <modulo> <doc>           MELHORAR (0053): o que mudou no codigo (commits) e no banco desde a versao
#                                     publicada e os itens afetados -> trabalho.md
#   check <modulo> <doc> [arquivo]    cobertura do inventario + checagem do PRMake (erros barram o envio)
#   save <modulo> <doc> [arquivo] [--summary "..."]
#                                     grava o rascunho no PRMake (sem enviar)
#   submit <modulo> <doc> [arquivo] --summary "o que mudou" [--sugestoes decisoes.json]
#                                     grava e envia para revisao (aprovacao na tela); decisoes.json = o que foi feito com
#                                     cada sugestao do pacote: [{"suggestionId","decision":"aplicada|recusada","items":[..],"note"}]
#   etapa <modulo> <doc> <chave> <pending|running|completed|failed|skipped> [--title T] [--detail D]
#                                     ANDAMENTO ao vivo na tela (chaves: inventario, leitura, checagem; areas: area:<nome>)
#   atividade <modulo> <doc> "texto"  o que esta fazendo agora (aparece na tela, linha "agora")
#   log <modulo> <doc> "texto" [info|progress|warning|error]
#   ids <modulo>                      IDs ja usados no modulo (publicados + rascunhos locais) — para nao repetir
#   find <termos> [--module m] [--kind RN,UC] [--card N]
#   get <ref> [ref...] [--card N]     itens publicados (modulo#RN-012) com o texto
#   impact <termo>                    quem usa a tabela/item/modulo
#   assets <modulo>                   anexos de UI/UX (baixa os arquivos para a pasta do modulo)
#   link <modulo> <url> <titulo> [--screens TELA-001,TELA-002] [--notes "..."]
#   upload <modulo> <arquivo> [titulo] [--screens ...] [--notes "..."]
# Env: PRMAKE_API_BASE, PRMAKE_TOKEN (ou ~/.claude/prmake-token.txt), RE_HOME (padrao ~/.prmake/reverse)
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
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TOOL_PY="$HERE/re_tool.py"
BANCO_PY="$HERE/re_banco.py"
SQL_SH="${RE_SQL:-$HOME/.claude/skills/analisar-bug/scripts/sql-query.sh}"
RE_HOME="${RE_HOME:-${PRMAKE_HOME:-$HOME/.prmake}/reverse}"
SKILLS_TOOL="${PRMAKE_SKILLS_TOOL:-$HOME/.claude/skills/.prmake/prmake-skills.sh}"
CMD="${1:-}"; shift || true
die() { echo "ERRO: $*" >&2; exit 1; }
command -v jq >/dev/null || die "jq nao encontrado"
if [[ -n "${PRMAKE_TOKEN:-}" ]]; then TK="$PRMAKE_TOKEN"; elif [[ -f "$HOME/.claude/prmake-token.txt" ]]; then TK="$(tr -d '\r\n' < "$HOME/.claude/prmake-token.txt")"; else die "token do PRMake nao encontrado (~/.claude/prmake-token.txt)"; fi
TMP="$(mktemp -d "${TMPDIR:-/tmp}/re.XXXXXX")"; trap 'rm -rf "$TMP"' EXIT

api() { # <METHOD> <path> [body-file] — resposta em $TMP/resp, status em $CODE
  local args=(-s --max-time "${RE_TIMEOUT:-120}" -o "$TMP/resp" -w '%{http_code}' -X "$1" "$BASE/ReverseEngineering$2" -H "x-api-key: $TK" -H 'accept: application/json')
  [[ -n "${3:-}" ]] && args+=(-H 'content-type: application/json' --data-binary "@$3")
  CODE="$(curl "${args[@]}" 2>/dev/null)"; CODE="${CODE:-000}"
}
check() { [[ "$CODE" =~ ^2 ]] || die "HTTP $CODE: $(jq -r '.error // .title // .' "$TMP/resp" 2>/dev/null | head -c 600)"; }
opt() { local name="$1" def="$2"; shift 2; while [[ $# -gt 0 ]]; do [[ "$1" == "$name" ]] && { printf '%s' "${2:-}"; return; }; shift; done; printf '%s' "$def"; }
urlenc() { jq -rn --arg v "$1" '$v|@uri'; }
positional() { local out=(); while [[ $# -gt 0 ]]; do case "$1" in --*) shift 2;; *) out+=("$1"); shift;; esac; done; printf '%s\n' "${out[@]:-}"; }
moddir() { printf '%s/%s' "$RE_HOME" "$1"; }
docdir() { printf '%s/%s/%s' "$RE_HOME" "$1" "$2"; }

# Pasta local de um repositorio pelo mapa da maquina (0048). Exit 2/3 do mapa = fora do mapa/ambiguo.
repo_path() {
  local repo="$1"
  [[ -f "$SKILLS_TOOL" ]] && bash "$SKILLS_TOOL" repos path "$repo" 2>/dev/null && return 0
  local f="${PRMAKE_HOME:-$HOME/.prmake}/repos.json"
  [[ -s "$f" ]] && jq -r --arg r "$repo" '.repos | to_entries[] | select((.key|ascii_downcase) == ($r|ascii_downcase)) | .value.path' "$f" | head -1 | grep . && return 0
  return 2
}

# Fontes do modulo -> linhas "papel<TAB>pasta" (aplica --path repo=subpasta).
resolve_sources() { # <modulo> [--path repo=sub ...]
  local mod="$1"; shift
  api GET "/modules/$(urlenc "$mod")"; check
  cp "$TMP/resp" "$TMP/module.json"
  local overrides=(); while [[ $# -gt 0 ]]; do [[ "$1" == --path ]] && overrides+=("$2"); shift; done
  local kind; kind="$(jq -r '.projectKind' "$TMP/module.json")"
  local n=0 missing=0
  # separador \x1f (nao e espaco): campo vazio (fonte sem subpasta) nao colapsa como o tab no read
  while IFS=$'\x1f' read -r repo sub role; do
    [[ -z "$repo" ]] && continue
    for o in "${overrides[@]:-}"; do [[ "${o%%=*}" == "$repo" ]] && sub="${o#*=}"; done
    local root; root="$(repo_path "$repo")" || { echo "FALTA: repositorio '$repo' nao esta no mapa da maquina — clone/fixe com: prmake-skills.sh repos set $repo <pasta>" >&2; missing=1; continue; }
    if [[ "$kind" == legacy && -z "$sub" && "$(printf '%s' "$repo" | tr '[:upper:]' '[:lower:]')" == edv-solvace ]]; then
      echo "FALTA: legado sem subpasta — o edv-solvace inteiro nao e o modulo. Veja 'Onde esta' em: kb.sh show $mod modulos; rode com --path edv-solvace=<subpasta> (e peca ao aprovador para gravar as fontes na tela)" >&2
      missing=1; continue
    fi
    local dir="$root${sub:+/$sub}"
    [[ -d "$dir" ]] || { echo "FALTA: pasta nao existe: $dir (fonte $repo/${sub})" >&2; missing=1; continue; }
    printf '%s\t%s\n' "${role:-backend}" "$dir"; n=$((n + 1))
  done < <(jq -r '.sources[] | [.repository, (.path // ""), .role] | join("\u001f")' "$TMP/module.json")
  for o in "${overrides[@]:-}"; do
    [[ -z "$o" ]] && continue
    jq -e --arg r "${o%%=*}" '.sources | any(.repository == $r)' "$TMP/module.json" >/dev/null && continue
    local root; root="$(repo_path "${o%%=*}")" || { echo "FALTA: repositorio '${o%%=*}' nao esta no mapa" >&2; missing=1; continue; }
    [[ -d "$root/${o#*=}" ]] && { printf 'backend\t%s\n' "$root/${o#*=}"; n=$((n + 1)); }
  done
  [[ $n -gt 0 ]] || return 3
  return $missing
}

# Inventários a mais (0053): banco da DEMO e termos do glossário, quando já foram gerados.
extra_inv() { local m; m="$(moddir "$1")"; for f in "$m/banco/inventario-banco.json" "$m/inventario-termos.json"; do [[ -s "$f" ]] && printf -- '--extra\n%s\n' "$f"; done; }

doc_file() { # <modulo> <doc> [arquivo]
  local f="${3:-}"; [[ -z "$f" ]] && f="$(docdir "$1" "$2")/documento.md"
  [[ -s "$f" ]] || die "documento nao encontrado ou vazio: $f"
  printf '%s' "$f"
}
revision_id() { # <modulo> <doc>
  local s; s="$(docdir "$1" "$2")/sessao.json"
  [[ -s "$s" ]] || die "sem sessao aberta para $1/$2 — rode: re.sh start $1 $2"
  jq -r '.revision.id' "$s"
}

# Andamento ao vivo (0052): nunca derruba o comando — sem rede, a tela so fica sem a novidade.
progress() { # <modulo> <doc> <json>
  local s; s="$(docdir "$1" "$2")/sessao.json"
  [[ -s "$s" ]] || return 0
  printf '%s' "$3" > "$TMP/progress.json"
  curl -s --max-time 15 -o /dev/null -X POST "$BASE/ReverseEngineering/revisions/$(jq -r '.revision.id' "$s")/progress" \
    -H "x-api-key: $TK" -H 'content-type: application/json' --data-binary "@$TMP/progress.json" 2>/dev/null || true
}
# Documentos com sessao aberta nesta maquina para o modulo (o inventario e do modulo; o andamento vai para cada um).
open_docs() { local d; for d in "$(moddir "$1")"/*/sessao.json; do [[ -s "$d" ]] && basename "$(dirname "$d")"; done; }

case "$CMD" in
  etapa)
    MOD="${1:?modulo}"; DOC="${2:?documento}"; KEY="${3:?etapa}"; ST="${4:?status}"
    jq -n --arg k "$KEY" --arg s "$ST" --arg t "$(opt --title "" "$@")" --arg d "$(opt --detail "" "$@")" \
      '{step: $k, status: $s} + (if $t == "" then {} else {title: $t} end) + (if $d == "" then {} else {detail: $d} end)' > "$TMP/body"
    progress "$MOD" "$DOC" "$(cat "$TMP/body")"; echo "OK $KEY -> $ST"
    ;;
  atividade)
    MOD="${1:?modulo}"; DOC="${2:?documento}"
    progress "$MOD" "$DOC" "$(jq -n --arg a "${3:?texto}" '{activity: $a}')"; echo "OK"
    ;;
  log)
    MOD="${1:?modulo}"; DOC="${2:?documento}"
    progress "$MOD" "$DOC" "$(jq -n --arg l "${3:?texto}" --arg k "${4:-info}" '{log: $l, kind: $k}')"; echo "OK"
    ;;

  modulo)
    if [[ -n "${1:-}" ]]; then api GET "/modules/$(urlenc "$1")"; check; jq -r '.key' "$TMP/resp"; exit 0; fi
    remote="$(git remote get-url origin 2>/dev/null)" || die "fora de um repositorio git — informe o modulo: re.sh modulo <chave>"
    repo="$(basename "${remote%.git}")"
    top="$(git rev-parse --show-toplevel)"; rel="$(python3 -c 'import os,sys;print(os.path.relpath(sys.argv[1],sys.argv[2]))' "$PWD" "$top")"
    api GET "/modules"; check
    # modulos configurados cujas fontes apontam para este repositorio; depois, chave do projeto = nome do repositorio
    jq -r --arg r "$(printf '%s' "$repo" | tr '[:upper:]' '[:lower:]')" '.[] | select((.key == $r) or (.key == ("revamp-" + ($r|sub("^revamp-";""))))) | .key' "$TMP/resp" > "$TMP/cands"
    cands=()
    while IFS= read -r line; do [[ -n "$line" ]] && cands+=("$line"); done < "$TMP/cands"
    if [[ ${#cands[@]} -eq 0 ]]; then
      for k in $(jq -r '.[].key' "$TMP/resp"); do
        api GET "/modules/$k"; [[ "$CODE" =~ ^2 ]] || continue
        jq -e --arg r "$repo" --arg rel "$rel" '.sources | any((.repository|ascii_downcase) == ($r|ascii_downcase) and (((.path // "") == "") or ($rel|startswith(.path)) or ((.path)|startswith($rel))))' "$TMP/resp" >/dev/null && cands+=("$k")
      done
    fi
    case ${#cands[@]} in
      0) echo "Nenhum modulo da engenharia reversa aponta para $repo${rel:+/$rel}. Informe a chave (re.sh modulo <chave>; lista na tela Engenharia reversa)." >&2; exit 2 ;;
      1) echo "${cands[0]}" ;;
      *) echo "Mais de um modulo usa $repo: ${cands[*]} — informe qual (re.sh modulo <chave>)." >&2; printf '%s\n' "${cands[@]}"; exit 3 ;;
    esac
    ;;

  tipos)
    api GET "/doc-types"; check
    jq -r '.[] | "\(.key)\t\(.title)\t\(if .required then "exigido" else "opcional" end)\titens: \(.kinds|join(","))"' "$TMP/resp"
    ;;

  status)
    MOD="${1:?modulo}"; api GET "/modules/$(urlenc "$MOD")"; check
    jq -r '"\(.key) — \(.displayName // .name) (\(.world)) · engenharia reversa \(if .complete then "COMPLETA" else "\(.publishedRequired)/\(.requiredCount)" end) · \(.items) itens",
      "Fontes: " + ([.sources[] | "\(.repository)\(if .path then "/" + .path else "" end) (\(.role))"] | join("; ")) + (if .configured then "" else "  [sugeridas — confirme]" end),
      "Apelidos (campo Module do card): " + (.aliases | join(", ")),
      "Documentos:",
      (.docs[] | "  \(.type)\t\(.title)\t\(if .published then "publicado v\(.published.version) (\(.published.items) itens)" else "—" end)\t\(if .open then "revisao #\(.open.number) \(.open.status)\(if .open.reviewNote then " — revisor: " + .open.reviewNote else "" end)" else "" end)\(if .pendingSuggestions > 0 then " · \(.pendingSuggestions) sugestoes" else "" end)"),
      "Anexos de UI/UX: \(.assets | length)" + (if (.assets|length) > 0 then " (re.sh assets \(.key))" else "" end)' "$TMP/resp"
    ;;

  start)
    MOD="${1:?modulo}"; DOC="${2:?documento (re.sh tipos)}"; MODE="${3:-}"
    D="$(docdir "$MOD" "$DOC")"; mkdir -p "$D/anexos"
    jq -n --arg m "$MODE" 'if $m == "" then {} else {mode: $m} end' > "$TMP/body"
    api POST "/modules/$(urlenc "$MOD")/docs/$(urlenc "$DOC")/sessions" "$TMP/body"; check
    cp "$TMP/resp" "$D/sessao.json"
    jq -r '.docType.template' "$D/sessao.json" > "$D/modelo.md"
    jq -r '.published // ""' "$D/sessao.json" > "$D/publicado.md"
    jq -r '.related' "$D/sessao.json" > "$D/relacionados.md"
    jq -r '.suggestions[] | "## \(.kind) · \(.sectionKey // "-") · card \(.cardNumber // "-") · \(.createdBy) (\(.id))\n\n\(.content)\n"' "$D/sessao.json" > "$D/sugestoes.md"
    jq -r '.reviewNote // ""' "$D/sessao.json" > "$D/nota-revisor.md"
    jq -r '.otherDocIds | to_entries[] | "\(.key)\t\(.value)"' "$D/sessao.json" > "$D/ids-outros-documentos.tsv"
    # rascunho: retomado (conteudo do PRMake) ou o ponto de partida; nunca sobrescreve um documento local mais novo
    if [[ "$(jq -r '.resumed' "$D/sessao.json")" == false || ! -s "$D/documento.md" ]]; then
      [[ -s "$D/documento.md" ]] && mv "$D/documento.md" "$D/documento.anterior.md"
      jq -r '.revision.content' "$D/sessao.json" > "$D/documento.md"
    fi
    for id in $(jq -r '.module.assets[] | select(.download) | .id' "$D/sessao.json"); do
      name="$(jq -r --arg i "$id" '.module.assets[] | select(.id == $i) | .fileName' "$D/sessao.json")"
      [[ -f "$D/anexos/$name" ]] || curl -s --max-time 120 -o "$D/anexos/$name" -H "x-api-key: $TK" "$BASE/ReverseEngineering/assets/$id/file"
    done
    jq -r '.module.assets[] | "- [\(.kind)] \(.title)\(if .url then " — " + .url else " — anexos/" + .fileName end)\(if (.screens|length) > 0 then " · telas: " + (.screens|join(", ")) else "" end)\(if .notes then " · " + .notes else "" end)"' "$D/sessao.json" > "$D/anexos.md"
    jq -r --arg d "$D" '"Sessao \(if .resumed then "RETOMADA" else "aberta" end): \(.revision.moduleKey) / \(.docType.title) — revisao #\(.revision.number) (\(.revision.mode), \(.revision.status))",
      "Pasta: \($d)  (escreva em documento.md)",
      "Publicado: \(if .publishedVersion then "v\(.publishedVersion) -> publicado.md" else "nenhum" end) · sugestoes pendentes: \(.suggestions|length) -> sugestoes.md · anexos: \(.module.assets|length) -> anexos.md",
      "Relacionados (itens de outros modulos): relacionados.md · IDs dos outros documentos: ids-outros-documentos.tsv (\(.otherDocIds|length))",
      "Base antiga do projeto (ponto de partida): " + ([.existingSections[] | .key] | join(", ")) + " — kb.sh show \(.revision.moduleKey) <secao>",
      "Secoes obrigatorias (##): " + (.docType.headings | join(" · ")),
      "Itens deste documento: " + (.docType.kinds | join(", ")) + " (+ GAP) · cobertura minima do inventario: \(.minCoverage * 100 | floor)%",
      (if .reviewNote then "NOTA DO REVISOR (resolva primeiro): \(.reviewNote)" else empty end),
      (if .revision.publishedChangedSinceBase then "AVISO: a versao publicada mudou depois que este rascunho comecou — compare com publicado.md" else empty end),
      (if .referenceDatabase then "Banco de referencia: \(.referenceDatabase.environment) (\(.referenceDatabase.host): \(.referenceDatabase.global) + \(.referenceDatabase.locals | join(", "))) — re.sh banco \(.revision.moduleKey)" else empty end),
      (if .revision.mode == "improve" and .publishedSession then "MELHORAR: rode re.sh trabalho \(.revision.moduleKey) \(.revision.docType) (o que mudou no codigo e no banco desde a versao publicada)" else empty end),
      (if (.suggestions|length) > 0 then "Sugestoes: decida cada uma (aplicada + itens / recusada + motivo) e envie com --sugestoes decisoes.json" else empty end)' "$D/sessao.json"
    progress "$MOD" "$DOC" "$(jq -c --arg h "${USER:-?}@$(hostname -s 2>/dev/null || echo maquina)" '{log: "Claude em \($h): pacote da sessao baixado (\(.suggestions|length) sugestoes, \(.module.assets|length) anexos\(if .publishedVersion then ", publicado v\(.publishedVersion)" else "" end))", kind: "progress"}' "$D/sessao.json")"
    echo "Andamento ao vivo na tela: re.sh etapa|atividade|log (o inventario, a checagem e o rascunho ja reportam sozinhos)."
    ;;

  fontes)
    MOD="${1:?modulo}"; shift
    resolve_sources "$MOD" "$@"; rc=$?
    [[ $rc -eq 3 ]] && die "nenhuma fonte local encontrada para $MOD"
    exit $rc
    ;;

  inventario)
    MOD="${1:?modulo}"; shift
    resolve_sources "$MOD" "$@" > "$TMP/sources"; rc=$?
    [[ -s "$TMP/sources" ]] || die "sem fontes locais para o inventario de $MOD"
    [[ $rc -eq 0 ]] || echo "AVISO: parte das fontes ficou de fora (acima) — o inventario cobre so o que foi encontrado" >&2
    mkdir -p "$(moddir "$MOD")"
    for d in $(open_docs "$MOD"); do progress "$MOD" "$d" '{"step":"inventario","status":"running","activity":"Inventario do codigo (sem LLM)"}'; done
    args=(); while IFS=$'\t' read -r role dir; do args+=("$role=$dir"); done < "$TMP/sources"
    python3 "$TOOL_PY" inventario "$(moddir "$MOD")/inventario.json" "${args[@]}" || {
      for d in $(open_docs "$MOD"); do progress "$MOD" "$d" '{"step":"inventario","status":"failed"}'; done
      die "falha no inventario"; }
    DETAIL="$(jq -r '"\(.counts | to_entries | map(.value) | add // 0) itens: " + ([.counts | to_entries | sort_by(-.value)[] | "\(.value) \(.key)"] | join(", "))' "$(moddir "$MOD")/inventario.json")"
    for d in $(open_docs "$MOD"); do progress "$MOD" "$d" "$(jq -n --arg dt "$DETAIL" '{step: "inventario", status: "completed", detail: $dt, log: ("Inventario: " + $dt), kind: "progress"}')"; done
    # commits das fontes (vao com o envio, para saber de que versao do codigo o documento veio)
    while IFS=$'\t' read -r role dir; do
      printf '%s\t%s\t%s\n' "$role" "$dir" "$(git -C "$dir" rev-parse --short HEAD 2>/dev/null)@$(git -C "$dir" rev-parse --abbrev-ref HEAD 2>/dev/null)"
    done < "$TMP/sources" > "$(moddir "$MOD")/fontes.tsv"
    ;;

  banco)
    MOD="${1:?modulo}"; shift
    [[ -f "$SQL_SH" ]] || die "sql-query.sh nao encontrado ($SQL_SH) — instale a skill analisar-bug (prmake-skills.sh update analisar-bug)"
    api GET "/settings"; check; cp "$TMP/resp" "$TMP/settings.json"
    jq -e '.referenceDatabase.global' "$TMP/settings.json" >/dev/null || die "banco de referencia nao configurado (Skills Configurations -> ReverseEngineeringReferenceDatabase)"
    INV="$(moddir "$MOD")/inventario.json"
    [[ -s "$INV" ]] || die "rode antes: re.sh inventario $MOD (o banco usa as tabelas e procedures citadas no codigo)"
    PREFIXES=(); SIGLA="$(opt --sigla "" "$@")"
    while [[ $# -gt 0 ]]; do [[ "$1" == --prefix ]] && PREFIXES+=("$2"); shift; done
    if [[ ${#PREFIXES[@]} -eq 0 ]]; then
      # prefixos TB_<SIGLA>_ mais citados pelo código do módulo (fora os compartilhados: WCM, SYS, GLB, EMP, MLG…)
      while IFS= read -r p; do [[ -n "$p" ]] && PREFIXES+=("$p"); done < <(jq -r '[.items[] | select(.cat == "tabela") | .name | ascii_upcase
        | (capture("^(?<p>TB_[A-Z0-9]+_)") | .p)] | map(select(test("^TB_(WCM|SYS|GLB|EMP|MLG|CMN|AUD|LOG)_") | not))
        | group_by(.) | map({p: .[0], n: length}) | sort_by(-.n) | (.[0].n // 0) as $top | map(select(.n * 2 >= $top)) | .[:3][] | .p' "$INV")
    fi
    [[ ${#PREFIXES[@]} -gt 0 ]] || die "nao achei o prefixo das tabelas do modulo — informe: re.sh banco $MOD --prefix TB_<SIGLA>_ --sigla <SIGLA>"
    [[ -n "$SIGLA" ]] || SIGLA="$(printf '%s' "${PREFIXES[0]}" | sed -E 's/^TB_([A-Z0-9]+)_$/\1/')"
    ENVN="$(jq -r '.referenceDatabase.environment' "$TMP/settings.json")"; HOSTA="$(jq -r '.referenceDatabase.host' "$TMP/settings.json")"
    ARGS=(catalogo --sql "$SQL_SH" --host "$HOSTA" --environment "$ENVN" --global "$(jq -r '.referenceDatabase.global' "$TMP/settings.json")"
          --sigla "$SIGLA" --inventario "$INV" --out "$(moddir "$MOD")/banco")
    while IFS= read -r l; do ARGS+=(--local "$l"); done < <(jq -r '.referenceDatabase.locals[]?' "$TMP/settings.json")
    for p in "${PREFIXES[@]}"; do ARGS+=(--prefix "$p"); done
    echo "Banco de referencia $ENVN ($HOSTA): prefixos ${PREFIXES[*]} · sigla $SIGLA"
    for d in $(open_docs "$MOD"); do progress "$MOD" "$d" "$(jq -n --arg a "Lendo o banco $ENVN (global e locais): ${PREFIXES[*]}" '{step: "banco", status: "running", activity: $a}')"; done
    if python3 "$BANCO_PY" "${ARGS[@]}" > "$TMP/banco.out"; then
      cat "$TMP/banco.out"
      DETAIL="$(grep -m1 '^Banco' "$TMP/banco.out" | cut -c1-380)"
      for d in $(open_docs "$MOD"); do progress "$MOD" "$d" "$(jq -n --arg dt "$DETAIL" '{step: "banco", status: "completed", detail: $dt, log: $dt, kind: "progress"}')"; done
    else
      RC=$?; cat "$TMP/banco.out"
      MSG="Sem acesso ao banco $ENVN ($HOSTA) — ligue a VPN e confira a credencial (prmake-skills.sh db-credentials); o documento registra GAP se seguir sem o banco"
      for d in $(open_docs "$MOD"); do progress "$MOD" "$d" "$(jq -n --arg m "$MSG" '{step: "banco", status: "failed", detail: $m, log: $m, kind: "error"}')"; done
      die "$MSG" 
    fi
    ;;

  termos)
    MOD="${1:?modulo}"
    INV="$(moddir "$MOD")/inventario.json"; [[ -s "$INV" ]] || die "rode antes: re.sh inventario $MOD"
    api GET "/settings"; check; EXCL="$(jq -c '.glossaryExclusions // []' "$TMP/resp")"
    api GET "/modules"; check
    # nomes de outros módulos (e as siglas de 2–4 letras das palavras-chave deles) não são termos deste módulo
    OUTROS="$(jq -c --arg m "$MOD" '[.[] | select(.key != $m) | (.displayName // .name), .name, .aliases[]] | map(select(. != null)) | unique' "$TMP/resp")"
    python3 "$TOOL_PY" termos "$INV" --banco "$(moddir "$MOD")/banco" --exclusoes "$EXCL" --outros-modulos "$OUTROS" --out "$(moddir "$MOD")/inventario-termos.json"
    ;;

  trabalho)
    MOD="${1:?modulo}"; DOC="${2:?documento}"; D="$(docdir "$MOD" "$DOC")"
    [[ -s "$D/sessao.json" ]] || die "abra a sessao antes: re.sh start $MOD $DOC improve"
    [[ -s "$D/publicado.md" ]] || { echo "Sem versao publicada — nao ha o que comparar (documento novo)."; exit 0; }
    : > "$TMP/arquivos"; : > "$TMP/objetos"; OUT="$D/trabalho.md"
    { echo "# Lista de trabalho — $MOD / $DOC (o que mudou desde a versao publicada)"; echo; } > "$OUT"
    echo "## Codigo" >> "$OUT"
    while IFS=$'\x1f' read -r role path commit; do
      [[ -z "$path" ]] && continue
      sha="${commit%%@*}"
      if [[ -z "$sha" || ! -d "$path" ]]; then echo "- $role $path: sem commit gravado/pasta ausente — revise pelo inventario completo" >> "$OUT"; continue; fi
      if ! git -C "$path" cat-file -e "$sha^{commit}" 2>/dev/null; then echo "- $role $path: commit $sha nao existe mais neste clone — revise pelo inventario completo" >> "$OUT"; continue; fi
      CH="$(git -C "$path" diff --name-status "$sha"..HEAD -- . 2>/dev/null)"
      echo "- $role \`$path\` desde $sha: $(printf '%s' "$CH" | grep -c . | tr -d ' ') arquivo(s)" >> "$OUT"
      printf '%s\n' "$CH" | grep . | sed 's/^/  - /' | head -200 >> "$OUT"
      # linhas alteradas (lado antigo = o que o documento publicado cita) por arquivo: "arquivo<TAB>ini-fim,ini-fim"
      git -C "$path" diff -U0 "$sha"..HEAD -- . 2>/dev/null | awk '
        /^--- a\// { f = substr($0, 7); next }
        /^--- \/dev\/null/ { f = ""; next }
        /^\+\+\+ b\// { if (f == "") f = substr($0, 7); next }
        /^@@/ { split($2, o, ","); a = substr(o[1], 2) + 0; n = (o[2] == "" ? 1 : o[2] + 0); if (n == 0) n = 1;
                r[f] = r[f] (r[f] == "" ? "" : ",") a "-" (a + n - 1) }
        END { for (k in r) print k "\t" r[k] }' >> "$TMP/arquivos"
    done < <(jq -r '.publishedSession.sources[]? | [.role, .path, .commit] | join("\u001f")' "$D/sessao.json")
    echo >> "$OUT"; echo "## Banco ($(jq -r '.referenceDatabase.environment // "DEMO"' "$D/sessao.json"))" >> "$OUT"
    CAT="$(moddir "$MOD")/banco/catalogo.json"
    if jq -e '.publishedSession.catalog' "$D/sessao.json" >/dev/null 2>&1 && [[ -s "$CAT" ]]; then
      jq '.publishedSession.catalog' "$D/sessao.json" > "$TMP/antigo.json"
      python3 "$BANCO_PY" diff "$TMP/antigo.json" "$CAT" > "$TMP/diff.json"
      jq -r '"- novos: \(.novos | join(", "))", "- alterados: \(.alterados | join(", "))", "- removidos: \(.removidos | join(", "))"' "$TMP/diff.json" >> "$OUT"
      jq -r '(.novos + .alterados + .removidos)[] | sub("^job "; "")' "$TMP/diff.json" >> "$TMP/objetos"
    else
      echo "- sem retrato do banco na versao publicada (ou rode re.sh banco $MOD) — compare pelo catalogo atual" >> "$OUT"
    fi
    echo >> "$OUT"; echo "## Itens publicados afetados (citam o que mudou)" >> "$OUT"
    python3 "$TOOL_PY" afetados "$D/publicado.md" --arquivos "$TMP/arquivos" --objetos "$TMP/objetos" | sed 's/^/- /; s/\t/ — por /' >> "$OUT"
    NA="$(grep -c . "$TMP/arquivos" | tr -d ' ')"; NO="$(grep -c . "$TMP/objetos" | tr -d ' ')"; NI="$(grep -c '^- [A-Z]' <(sed -n '/Itens publicados afetados/,$p' "$OUT") | tr -d ' ')"
    MSG="$NA arquivo(s) e $NO objeto(s) do banco mudaram desde a versao publicada → $NI item(ns) a revisar"
    echo; cat "$OUT"; echo; echo "$MSG"
    progress "$MOD" "$DOC" "$(jq -n --arg m "$MSG" '{log: ("Lista de trabalho do melhorar: " + $m), kind: "progress", activity: $m}')"
    ;;

  check)
    MOD="${1:?modulo}"; DOC="${2:?documento}"; F="$(doc_file "$MOD" "$DOC" "${3:-}")"
    INV="$(moddir "$MOD")/inventario.json"; RATIO=null
    progress "$MOD" "$DOC" '{"step":"checagem","status":"running","activity":"Checando estrutura e cobertura do inventario"}'
    if [[ -s "$INV" ]]; then
      EXTRA=(); while IFS= read -r x; do EXTRA+=("$x"); done < <(extra_inv "$MOD")
      python3 "$TOOL_PY" cobertura "$INV" "$F" "$DOC" --json "$(docdir "$MOD" "$DOC")/cobertura.json" --max "${RE_MAX_MISSING:-60}" "${EXTRA[@]}"
      RATIO="$(jq -r '.ratio // "null"' "$(docdir "$MOD" "$DOC")/cobertura.json")"
    else
      echo "(sem inventario — rode: re.sh inventario $MOD)"
    fi
    jq -n --arg d "$DOC" --rawfile c "$F" --argjson r "$RATIO" '{docType: $d, content: $c, coverageRatio: $r}' > "$TMP/body"
    api POST "/modules/$(urlenc "$MOD")/lint" "$TMP/body"; check
    jq -r '"Checagem do PRMake: \(.items) itens (" + ([.byKind | to_entries[] | "\(.key) \(.value)"] | join(", ")) + ")",
      (if (.errors|length) > 0 then "ERROS (barram o envio):", (.errors[] | "  - " + .) else "Sem erros." end),
      (if (.warnings|length) > 0 then "Avisos:", (.warnings[] | "  - " + .) else empty end)' "$TMP/resp"
    progress "$MOD" "$DOC" "$(jq -c --argjson r "$RATIO" '{step: "checagem", status: (if (.errors|length) == 0 then "completed" else "running" end),
      detail: ("\(.items) itens · cobertura \(if $r == null then "—" else (($r * 100 | floor | tostring) + "%") end) · \(.errors|length) erro(s), \(.warnings|length) aviso(s)"),
      log: ("Checagem: \(.items) itens, cobertura \(if $r == null then "—" else (($r * 100 | floor | tostring) + "%") end), \(.errors|length) erro(s)"),
      kind: (if (.errors|length) == 0 then "progress" else "warning" end)}' "$TMP/resp")"
    [[ "$(jq '.errors|length' "$TMP/resp")" == 0 ]]
    ;;

  save|submit)
    MOD="${1:?modulo}"; DOC="${2:?documento}"
    POS=(); while IFS= read -r p; do [[ -n "$p" ]] && POS+=("$p"); done < <(positional "${@:3}")
    F="$(doc_file "$MOD" "$DOC" "${POS[0]:-}")"; ID="$(revision_id "$MOD" "$DOC")"
    SUMMARY="$(opt --summary "" "$@")"
    [[ "$CMD" == submit && -z "$SUMMARY" ]] && die "informe --summary \"o que este documento cobre / o que mudou\" (o revisor le)"
    COV="$(docdir "$MOD" "$DOC")/cobertura.json"; INV="$(moddir "$MOD")/inventario.json"
    if [[ -s "$INV" ]]; then
      EXTRA=(); while IFS= read -r x; do EXTRA+=("$x"); done < <(extra_inv "$MOD")
      python3 "$TOOL_PY" cobertura "$INV" "$F" "$DOC" --json "$COV" --max 0 "${EXTRA[@]}" >/dev/null
    fi
    [[ -s "$COV" ]] || echo '{}' > "$COV"
    FONTES="$(moddir "$MOD")/fontes.tsv"; [[ -s "$FONTES" ]] || : > "$FONTES"
    # 0053: retrato do banco (objetos/jobs com hash e data) guardado na revisão — o próximo "melhorar" compara
    CAT="$(moddir "$MOD")/banco/catalogo.json"
    if [[ -s "$CAT" ]]; then python3 "$BANCO_PY" snapshot "$CAT" > "$TMP/snapshot.json"; else echo 'null' > "$TMP/snapshot.json"; fi
    DEC="$(opt --sugestoes "" "$@")"
    if [[ -n "$DEC" ]]; then [[ -s "$DEC" ]] || die "arquivo de decisoes nao encontrado: $DEC"; jq -e 'type == "array"' "$DEC" >/dev/null || die "$DEC precisa ser uma lista JSON"
      jq -e 'all(.[]; ((.suggestionId // .id // "") | test("^[0-9a-fA-F-]{36}$")))' "$DEC" >/dev/null || die "$DEC: toda decisao precisa do suggestionId (o id da sugestao em sugestoes.md)"
      cp "$DEC" "$TMP/decisoes.json"; else echo 'null' > "$TMP/decisoes.json"; fi
    jq -n --rawfile c "$F" --arg s "$SUMMARY" --slurpfile cov "$COV" --rawfile fontes "$FONTES" --slurpfile snap "$TMP/snapshot.json" \
      --slurpfile dec "$TMP/decisoes.json" --argjson counts "$( [[ -s "$INV" ]] && jq '.counts' "$INV" || echo '{}')" '
      {content: $c,
       coverage: ($cov[0] | {total, covered, ratio, byCategory, missingCount, missing: ((.missing // [])[:200])}),
       coverageRatio: ($cov[0].ratio),
       session: ({sources: ($fontes | split("\n") | map(select(length > 0) | split("\t") | {role: .[0], path: .[1], commit: .[2]})), inventory: $counts, tool: "re.sh"}
                 + (if $snap[0] == null then {} else {catalog: $snap[0]} end))}
      + (if $s == "" then {} else {summary: $s} end)
      + (if $dec[0] == null then {} else {suggestionDecisions: ($dec[0] | map({suggestionId: (.suggestionId // .id), decision: (.decision // .decisao), items: (.items // .itens // []), note: (.note // .motivo // .nota)}))} end)' > "$TMP/body"
    api PUT "/revisions/$ID" "$TMP/body"; check
    if [[ "$CMD" == submit ]]; then
      api POST "/revisions/$ID/submit"; check
      jq -r '"Enviado para revisao: \(.moduleKey) / \(.docType) — revisao #\(.number) (\(.status)) · cobertura \(if .coverageRatio then (.coverageRatio * 100 | floor | tostring) + "%" else "—" end)",
        (if .lint and (.lint.warnings|length) > 0 then "Avisos para o revisor:", (.lint.warnings[] | "  - " + .) else empty end),
        "Aprovacao: PRMake -> Engenharia reversa -> \(.moduleName // .moduleKey) -> \(.docType) (aprovar e publicar)."' "$TMP/resp"
    else
      jq -r '"Rascunho gravado: revisao #\(.number) (\(.status)), \(.length) caracteres" + (if .lint and (.lint.errors|length) > 0 then " — \(.lint.errors|length) erro(s) de checagem: re.sh check" else "" end)' "$TMP/resp"
      progress "$MOD" "$DOC" "$(jq -c '{log: ("Rascunho gravado (visivel na tela): \(.lint.items // 0) itens" + (if .coverageRatio then ", cobertura \(.coverageRatio * 100 | floor)%" else "" end)), kind: "progress"}' "$TMP/resp")"
    fi
    ;;

  ids)
    MOD="${1:?modulo}"
    api GET "/index/search?module=$(urlenc "$MOD")&limit=2000&removed=true"; check
    jq -r '.[] | "\(.itemId)\tpublicado (\(.docType))\(if .removed then " (removido)" else "" end)"' "$TMP/resp" > "$TMP/pub"
    [[ -d "$(moddir "$MOD")" ]] && python3 "$TOOL_PY" ids "$(moddir "$MOD")" | sed 's/$/  (local)/' > "$TMP/loc" || : > "$TMP/loc"
    sort -t- -k1,1 -k2,2n "$TMP/pub" "$TMP/loc"
    ;;

  find)
    Q="$(positional "$@" | tr '\n' ' ')"; MODQ="$(opt --module "" "$@")"; KQ="$(opt --kind "" "$@")"; CARD="$(opt --card "" "$@")"
    api GET "/index/search?q=$(urlenc "$Q")&module=$(urlenc "$MODQ")&kind=$(urlenc "$KQ")&card=$(urlenc "$CARD")&limit=${RE_LIMIT:-15}"; check
    jq -r 'if length == 0 then "Nada no indice da engenharia reversa (tente outros termos; kb.sh find para a base antiga)." else .[] | "\(.ref) [\(.kindLabel)] \(.title)\(if (.tables|length) > 0 then " · " + (.tables[:3]|join(", ")) else "" end)\n    \(.snippet[:160])" end' "$TMP/resp"
    ;;

  get)
    CARD="$(opt --card "" "$@")"; REFS="$(positional "$@" | paste -sd, -)"
    api GET "/items?refs=$(urlenc "$REFS")&card=$(urlenc "$CARD")"; check
    jq -r '.[] | "--- \(.ref) (\(.docType), v\(.sectionVersion))\(if (.referencedBy|length) > 0 then " · citado por: " + (.referencedBy[:8]|join(", ")) else "" end)\n\(.body)\n"' "$TMP/resp"
    ;;

  impact)
    api GET "/impact?term=$(urlenc "${1:?termo}")"; check
    jq -r 'if length == 0 then "Nenhum item publicado cita isso." else group_by(.moduleKey)[] | "\(.[0].moduleKey):", (.[] | "  - \(.itemId) [\(.kind)] \(.title)") end' "$TMP/resp"
    ;;

  assets)
    MOD="${1:?modulo}"; D="$(moddir "$MOD")/anexos"; mkdir -p "$D"
    api GET "/modules/$(urlenc "$MOD")"; check; cp "$TMP/resp" "$TMP/m.json"
    for id in $(jq -r '.assets[] | select(.download) | .id' "$TMP/m.json"); do
      name="$(jq -r --arg i "$id" '.assets[] | select(.id == $i) | .fileName' "$TMP/m.json")"
      [[ -f "$D/$name" ]] || curl -s --max-time 120 -o "$D/$name" -H "x-api-key: $TK" "$BASE/ReverseEngineering/assets/$id/file"
    done
    jq -r --arg d "$D" '.assets[] | "- [\(.kind)] \(.title)\(if .url then " — " + .url else " — " + $d + "/" + .fileName end)\(if (.screens|length) > 0 then " · telas: " + (.screens|join(", ")) else "" end)\(if .notes then " · " + .notes else "" end)"' "$TMP/m.json"
    ;;

  link)
    MOD="${1:?modulo}"; URL="${2:?url}"; TITLE="${3:?titulo}"
    jq -n --arg u "$URL" --arg t "$TITLE" --arg n "$(opt --notes "" "$@")" --arg s "$(opt --screens "" "$@")" \
      '{url: $u, title: $t, notes: (if $n == "" then null else $n end), screens: ($s | split(",") | map(select(length > 0)))}' > "$TMP/body"
    api POST "/modules/$(urlenc "$MOD")/assets/link" "$TMP/body"; check
    jq -r '"Anexado: [\(.kind)] \(.title) — \(.url)"' "$TMP/resp"
    ;;

  upload)
    MOD="${1:?modulo}"; FILE="${2:?arquivo}"; [[ -f "$FILE" ]] || die "arquivo nao encontrado: $FILE"
    TITLE="${3:-$(basename "$FILE")}"; [[ "$TITLE" == --* ]] && TITLE="$(basename "$FILE")"
    CODE="$(curl -s --max-time 300 -o "$TMP/resp" -w '%{http_code}' -X POST "$BASE/ReverseEngineering/modules/$(urlenc "$MOD")/assets/file" \
      -H "x-api-key: $TK" -F "file=@$FILE" -F "title=$TITLE" -F "notes=$(opt --notes "" "$@")" -F "screens=$(opt --screens "" "$@")")"
    check
    jq -r '"Anexado: [\(.kind)] \(.title) (\(.size) bytes)"' "$TMP/resp"
    ;;

  ""|-h|--help|help) sed -n '2,32p' "$0" | sed 's/^# \{0,1\}//' ;;
  *) die "comando desconhecido: $CMD (re.sh help)" ;;
esac
