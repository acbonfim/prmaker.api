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
#   fontes <modulo> [--path repo=subpasta|arquivo|glob ...]
#                                     caminhos locais das fontes (mapa da maquina); legado sem subpasta = erro (o
#                                     edv-solvace inteiro nao e um modulo). --path com PASTA substitui a do repo; com
#                                     ARQUIVO, GLOB ou outra pasta do mesmo repo soma (0056): --path edv-solvace='solvace-core/helpers/**/Sa3*.cs'
#   inventario <modulo> [--path repo=subpasta ...] [--local]
#                                     0067: le a COPIA DA MASTER atualizada (git fetch; worktree <clone>/../.prmake-wt/master/<repo>),
#                                     nunca a branch em que o clone estiver; --local (ou RE_LOCAL=1) le o clone como esta
#                                     inventario deterministico do codigo -> $RE_HOME/<modulo>/inventario.json
#   banco <modulo> [--prefix TB_X_ ...] [--sigla X]
#                                     CATALOGO DO BANCO DA DEMO (0053, somente leitura): views, procedures, functions,
#                                     triggers, tabelas (colunas, chaves, checks) e jobs do SQL Agent do modulo, global e
#                                     locais -> $RE_HOME/<modulo>/banco/ (+ inventario-banco.json para a cobertura)
#   traducoes <modulo>                TRADUCOES (0056) dos rotulos do modulo no Multilingual do revamp (PostgreSQL, somente
#                                     leitura; credencial em ~/.claude/multilingual-credentials.json) -> banco/traducoes.json
#                                     (roda sozinho no fim do `banco`)
#   infra <modulo> [--account ID] [--profile P] [--region R ...] [--termo T ...] [--so-conta]
#                                     OPCIONAL (0058): mapeia pelo AWS CLI (SO LEITURA) tudo o que a conta tem — Lambdas, S3, esteiras,
#                                     segredos (so nomes), logs do CloudWatch, filas, topicos, regras, bancos... — e liga ao modulo;
#                                     le tambem as esteiras dos repositorios -> $RE_HOME/<modulo>/infra/ + inventario-infra.json
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
#   perguntas <modulo> [arquivo]      VISAO PRATICA (0054): quais perguntas reais do Pergunte o documento responde
#   armadilhas <modulo>               armadilhas do modulo (o que ja deu errado, ligado aos itens)
#   armadilha <modulo> --titulo T --texto-file F [--itens RN-001,TELA-002] [--cards 75067] [--origem learning|manual]
#                                     registra uma armadilha (de quem nao aprova: "a conferir")
#   migrar <modulo> <migracao.json>   liga as armadilhas antigas e as sugestoes sem item aos itens (0054):
#                                     {"traps":[{title,text,items,cards}], "suggestions":[{id,itemId,sectionKey}]}
#   ids <modulo>                      IDs ja usados no modulo (publicados + rascunhos locais) — para nao repetir
#   config                            0066: como gerar (modelo dos subagentes, paralelismo, orcamento da area, checkpoint, idade do
#                                     retrato) — Skills Configurations -> ReverseEngineeringGeneration
#   areas <modulo> <doc> [--orcamento KB]
#                                     0066: divide o codigo do documento em AREAS que cabem no orcamento, com faixa de IDs -> areas.json
#   pacote <modulo> <doc> [--area A]  0066: pacote de leitura por area (codigo inteiro em blocos, com linha) + cartao do subagente
#   faltando <modulo> <doc> <area>    0066: o que a parte da area ainda nao cobre (checkpoint/retomada)
#   juntar <modulo> <doc>             0066: junta parte-*.md em documento.md e compacta os IDs das faixas (referencias juntas)
#   evidencia <modulo> <doc> [arq]    0066: confere arquivo:linha e literais citados contra as fontes (tambem roda no check)
#   retrato banco|infra|status [--forcar]
#                                     0066: RETRATO do banco de referencia inteiro e da conta AWS, uma vez; banco/infra dos modulos
#                                     usam o retrato enquanto ele estiver dentro da idade maxima (--ao-vivo forca a leitura direta)
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
INFRA_PY="$HERE/re_infra.py"
TRAD_PY="$HERE/re_traducoes.py"
PAC_PY="$HERE/re_pacote.py"
# 0056: as traducoes vem do Multilingual (PostgreSQL) — psycopg no venv da skill base-solvace
MLG_PY="${RE_MLG_PYTHON:-$HOME/.claude/skills/base-solvace/.venv/bin/python}"
[[ -x "$MLG_PY" ]] || { [[ -x "$HOME/.claude/skills/base-solvace/.venv/Scripts/python.exe" ]] && MLG_PY="$HOME/.claude/skills/base-solvace/.venv/Scripts/python.exe"; }
SQL_SH="${RE_SQL:-$HOME/.claude/skills/analisar-bug/scripts/sql-query.sh}"
RE_HOME="${RE_HOME:-${PRMAKE_HOME:-$HOME/.prmake}/reverse}"
SKILLS_TOOL="${PRMAKE_SKILLS_TOOL:-$HOME/.claude/skills/.prmake/prmake-skills.sh}"
# 0067: a engenharia reversa documenta a MASTER atualizada (copia so de leitura, sem mexer no clone de trabalho)
MASTER_WT="${RE_MASTER_WT:-$HOME/.claude/skills/analisar-bug/scripts/master-wt.sh}"
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
RETRATO="$RE_HOME/_retrato"
docdir() { printf '%s/%s/%s' "$RE_HOME" "$1" "$2"; }

# Pasta local de um repositorio pelo mapa da maquina (0048). Exit 2/3 do mapa = fora do mapa/ambiguo.
repo_path() {
  local repo="$1"
  [[ -f "$SKILLS_TOOL" ]] && bash "$SKILLS_TOOL" repos path "$repo" 2>/dev/null && return 0
  local f="${PRMAKE_HOME:-$HOME/.prmake}/repos.json"
  [[ -s "$f" ]] && jq -r --arg r "$repo" '.repos | to_entries[] | select((.key|ascii_downcase) == ($r|ascii_downcase)) | .value.path' "$f" | head -1 | grep . && return 0
  return 2
}

# 0067: raiz do repositorio na copia da origin/master (git fetch + worktree em <clone>/../.prmake-wt/master/<repo>).
# RE_LOCAL=1 (ou --local nos comandos) le o clone como esta — so para documentar uma branch que ainda nao entrou na master.
master_root() { # <pasta do clone>
  if [[ "${RE_LOCAL:-0}" == 1 || ! -f "$MASTER_WT" ]]; then printf '%s\n' "$1"; return; fi
  bash "$MASTER_WT" "$1" 2>>"$TMP/master.log" || printf '%s\n' "$1"
}

# 0056: caminho da fonte -> o que existe: pasta, arquivo ou glob ("helpers/**/Sa3*.cs") expandido em arquivos.
expand_path() { # <raiz> <sub>
  python3 - "$1" "$2" <<'PY'
import glob, os, sys
root, sub = sys.argv[1], sys.argv[2]
p = os.path.join(root, sub) if sub else root
if any(c in sub for c in "*?["):
    for f in sorted(glob.glob(p, recursive=True)):
        if os.path.isfile(f):
            print(f)
elif os.path.exists(p):
    print(p)
PY
}
is_pattern() { [[ "$1" == *[\*\?\[]* ]]; }

# Fontes do modulo -> linhas "papel<TAB>caminho" (pasta ou arquivo). --path repo=pasta SUBSTITUI a pasta do repo (como
# antes); --path repo=arquivo ou repo=glob SOMA (0056: ex. --path edv-solvace=helpers/**/Sa3*.cs mantem systems/sa3).
resolve_sources() { # <modulo> [--path repo=sub ...]
  local mod="$1"; shift
  api GET "/modules/$(urlenc "$mod")"; check
  cp "$TMP/resp" "$TMP/module.json"
  local overrides=(); while [[ $# -gt 0 ]]; do [[ "$1" == --path ]] && overrides+=("$2"); [[ "$1" == --local ]] && RE_LOCAL=1; shift; done
  local kind; kind="$(jq -r '.projectKind' "$TMP/module.json")"
  local n=0 missing=0 used="|"
  # separador \x1f (nao e espaco): campo vazio (fonte sem subpasta) nao colapsa como o tab no read
  while IFS=$'\x1f' read -r repo sub role; do
    [[ -z "$repo" ]] && continue
    local root; root="$(repo_path "$repo")" || { echo "FALTA: repositorio '$repo' nao esta no mapa da maquina — clone/fixe com: prmake-skills.sh repos set $repo <pasta>" >&2; missing=1; continue; }
    root="$(master_root "$root")"
    # a PRIMEIRA --path com pasta deste repo substitui a da tela; as demais (pastas, arquivos, globs) somam abaixo
    for o in "${overrides[@]:-}"; do
      if [[ "${o%%=*}" == "$repo" ]] && ! is_pattern "${o#*=}" && [[ -d "$root/${o#*=}" ]]; then sub="${o#*=}"; used="$used$o|"; break; fi
    done
    if [[ "$kind" == legacy && -z "$sub" && "$(printf '%s' "$repo" | tr '[:upper:]' '[:lower:]')" == edv-solvace ]]; then
      echo "FALTA: legado sem subpasta — o edv-solvace inteiro nao e o modulo. Veja 'Onde esta' em: kb.sh show $mod modulos; rode com --path edv-solvace=<subpasta> (e peca ao aprovador para gravar as fontes na tela)" >&2
      missing=1; continue
    fi
    local found=0 p
    while IFS= read -r p; do [[ -n "$p" ]] && { printf '%s\t%s\n' "${role:-backend}" "$p"; n=$((n + 1)); found=1; }; done < <(expand_path "$root" "$sub")
    [[ $found -eq 1 ]] || { echo "FALTA: nao existe: $root${sub:+/$sub} (fonte $repo/${sub})" >&2; missing=1; }
  done < <(jq -r '.sources[] | [.repository, (.path // ""), .role] | join("\u001f")' "$TMP/module.json")
  for o in "${overrides[@]:-}"; do
    [[ -z "$o" ]] && continue
    local orepo="${o%%=*}" osub="${o#*=}" oroot orole
    oroot="$(repo_path "$orepo")" || { echo "FALTA: repositorio '$orepo' nao esta no mapa" >&2; missing=1; continue; }
    oroot="$(master_root "$oroot")"
    # pasta de um repo que ja e fonte: ja substituiu acima; arquivo/glob soma (mesmo papel da fonte do repo)
    [[ "$used" == *"|$o|"* ]] && continue
    if jq -e --arg r "$orepo" '.sources | any(.repository == $r)' "$TMP/module.json" >/dev/null; then
      orole="$(jq -r --arg r "$orepo" '[.sources[] | select(.repository == $r) | .role][0] // "backend"' "$TMP/module.json")"
    else orole=backend; fi
    local found=0 p
    while IFS= read -r p; do [[ -n "$p" ]] && { printf '%s\t%s\n' "$orole" "$p"; n=$((n + 1)); found=1; }; done < <(expand_path "$oroot" "$osub")
    [[ $found -eq 1 ]] || { echo "FALTA: --path $o nao encontrou nada" >&2; missing=1; }
  done
  [[ $n -gt 0 ]] || return 3
  return $missing
}

# Inventários a mais (0053): banco da DEMO e termos do glossário, quando já foram gerados.
extra_inv() { local m; m="$(moddir "$1")"; for f in "$m/banco/inventario-banco.json" "$m/inventario-termos.json" "$m/inventario-infra.json"; do [[ -s "$f" ]] && printf -- '--extra\n%s\n' "$f"; done; }

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

# 0066: configuracao da geracao (ReverseEngineeringGeneration) — guardada em $RE_HOME/_config.json (vale sem rede).
load_config() {
  mkdir -p "$RE_HOME"
  api GET "/settings"
  if [[ "$CODE" =~ ^2 ]]; then cp "$TMP/resp" "$RE_HOME/_config.json"; fi
  [[ -s "$RE_HOME/_config.json" ]] || echo '{}' > "$RE_HOME/_config.json"
}
gen() { # <chave> <padrao> — valor da configuracao de geracao
  [[ -s "$RE_HOME/_config.json" ]] || load_config
  local v; v="$(jq -r --arg k "$1" '(.generation // {})[$k] // empty | if type == "object" then tojson else tostring end' "$RE_HOME/_config.json" 2>/dev/null)"
  printf '%s' "${v:-$2}"
}
# idade (dias, com fracao) de um arquivo do retrato; 9999 se nao existe
age_days() { python3 -c 'import os,sys,time;p=sys.argv[1];print(round((time.time()-os.path.getmtime(p))/86400,2) if os.path.exists(p) else 9999)' "$1"; }
fresh() { python3 -c 'import sys;sys.exit(0 if float(sys.argv[1]) <= float(sys.argv[2]) else 1)' "$(age_days "$1")" "$2"; }

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
    # 0054: visão prática = só o publicado; perguntas reais; armadilhas (e as antigas, para migrar)
    if [[ "$(jq '.publishedDocs | length' "$D/sessao.json")" -gt 0 ]]; then
      mkdir -p "$D/publicados"
      for k in $(jq -r '.publishedDocs | keys[]' "$D/sessao.json"); do jq -r --arg k "$k" '.publishedDocs[$k]' "$D/sessao.json" > "$D/publicados/$k.md"; done
    fi
    jq '.questions' "$D/sessao.json" > "$D/perguntas.json"
    jq -r '.questions[] | "- (\(.times)×, \(.coverage)) \(.text)"' "$D/sessao.json" > "$D/perguntas.md"
    jq -r '.traps[] | "## \(.title)\(if .needsReview then " (a conferir)" else "" end)\n- itens: \(.items | join(", ")) · cards: \(.cards | join(", "))\n\n\(.text)\n"' "$D/sessao.json" > "$D/armadilhas.md"
    jq -r '.legacyTraps // ""' "$D/sessao.json" > "$D/armadilhas-antigas.md"
    # 0066: chaves validas para **Modulos:** dos INT (a checagem avisa chave desconhecida; o mapa so usa chave valida)
    api GET "/modules"; if [[ "$CODE" =~ ^2 ]]; then
      jq -r '.[] | [.key, (.displayName // .name // ""), ((.aliases // []) | join(", "))] | @tsv' "$TMP/resp" > "$D/modulos.tsv"; fi
    load_config >/dev/null 2>&1 || true
    # rascunho: retomado (conteudo do PRMake) ou o ponto de partida; nunca sobrescreve um documento local mais novo
    if [[ "$(jq -r '.resumed' "$D/sessao.json")" == false || ! -s "$D/documento.md" ]]; then
      [[ -s "$D/documento.md" ]] && mv "$D/documento.md" "$D/documento.anterior.md"
      rm -f "$D/documento.base.md" "$D/areas.json"; rm -rf "$D/pacotes"   # 0066: sessao nova = areas/pacotes/base novos
      if ls "$D"/parte-*.md >/dev/null 2>&1; then mkdir -p "$D/partes-anteriores"; mv "$D"/parte-*.md "$D/partes-anteriores/"; fi
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
      (if (.suggestions|length) > 0 then "Sugestoes: decida cada uma (aplicada + itens / recusada + motivo) e envie com --sugestoes decisoes.json" else empty end),
      (if (.publishedDocs|length) > 0 then "VISAO PRATICA: escreva so a partir de publicados/*.md (\(.publishedDocs | keys | join(", "))); perguntas reais: perguntas.md (\(.questions|length)) — confira com re.sh perguntas" else empty end),
      (if (.traps|length) > 0 then "Armadilhas do modulo: armadilhas.md (\(.traps|length))" else empty end),
      (if (.legacyTraps // "") != "" then "MIGRACAO PENDENTE: ligue as armadilhas antigas (armadilhas-antigas.md) e as sugestoes sem item aos itens — re.sh migrar \(.revision.moduleKey) migracao.json" else empty end)' "$D/sessao.json"
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
    [[ -s "$TMP/master.log" ]] && sort -u "$TMP/master.log" | sed 's/^/Codigo: /'
    [[ "${RE_LOCAL:-0}" == 1 ]] && echo "AVISO: --local — lendo os clones como estao (branch atual), nao a master"
    python3 "$TOOL_PY" inventario "$(moddir "$MOD")/inventario.json" "${args[@]}" || {
      for d in $(open_docs "$MOD"); do progress "$MOD" "$d" '{"step":"inventario","status":"failed"}'; done
      die "falha no inventario"; }
    DETAIL="$(jq -r '"\(.counts | to_entries | map(.value) | add // 0) itens: " + ([.counts | to_entries | sort_by(-.value)[] | "\(.value) \(.key)"] | join(", "))' "$(moddir "$MOD")/inventario.json")"
    for d in $(open_docs "$MOD"); do progress "$MOD" "$d" "$(jq -n --arg dt "$DETAIL" '{step: "inventario", status: "completed", detail: $dt, log: ("Inventario: " + $dt), kind: "progress"}')"; done
    # commits das fontes (vao com o envio, para saber de que versao do codigo o documento veio)
    while IFS=$'\t' read -r role dir; do
      g="$dir"; [[ -f "$g" ]] && g="$(dirname "$g")"  # 0056: fonte pode ser um arquivo
      br="$(git -C "$g" rev-parse --abbrev-ref HEAD 2>/dev/null)"; [[ "$dir" == */.prmake-wt/master/* ]] && br="origin/master"
      printf '%s\t%s\t%s\n' "$role" "$dir" "$(git -C "$g" rev-parse --short HEAD 2>/dev/null)@$br"
    done < "$TMP/sources" > "$(moddir "$MOD")/fontes.tsv"
    ;;

  banco)
    MOD="${1:?modulo}"; shift
    [[ -f "$SQL_SH" ]] || die "sql-query.sh nao encontrado ($SQL_SH) — instale a skill analisar-bug (prmake-skills.sh update analisar-bug)"
    api GET "/settings"; check; cp "$TMP/resp" "$TMP/settings.json"
    jq -e '.referenceDatabase.global' "$TMP/settings.json" >/dev/null || die "banco de referencia nao configurado (Skills Configurations -> ReverseEngineeringReferenceDatabase)"
    INV="$(moddir "$MOD")/inventario.json"
    [[ -s "$INV" ]] || die "rode antes: re.sh inventario $MOD (o banco usa as tabelas e procedures citadas no codigo)"
    PREFIXES=(); SIGLA="$(opt --sigla "" "$@")"; LIVE=0; ALLARGS=" $* "
    while [[ $# -gt 0 ]]; do [[ "$1" == --prefix ]] && PREFIXES+=("$2"); [[ "$1" == --ao-vivo ]] && LIVE=1; shift; done
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
    # 0066: retrato do banco (re.sh retrato banco) dentro da idade maxima = sem VPN e sem esperar; --ao-vivo le direto
    RDIR="$RETRATO/banco/$ENVN-$HOSTA"; MAXD="$(gen snapshotMaxAgeDays 7)"
    if [[ $LIVE -eq 0 ]] && fresh "$RDIR/meta.json" "$MAXD" \
       && jq -e --arg g "$(jq -r '.referenceDatabase.global' "$TMP/settings.json")" '.global == $g' "$RDIR/meta.json" >/dev/null 2>&1; then
      ARGS+=(--retrato "$RDIR"); echo "Usando o RETRATO do banco de $(jq -r '.collectedAt' "$RDIR/meta.json") (atualize com: re.sh retrato banco; leitura direta: --ao-vivo)"
    elif [[ $LIVE -eq 0 ]]; then
      echo "Sem retrato do banco (ou mais velho que $MAXD dias) — lendo ao vivo. Dica: re.sh retrato banco (uma vez para todos os modulos)"
    fi
    for d in $(open_docs "$MOD"); do progress "$MOD" "$d" "$(jq -n --arg a "Lendo o banco $ENVN (global e locais): ${PREFIXES[*]}" '{step: "banco", status: "running", activity: $a}')"; done
    if python3 "$BANCO_PY" "${ARGS[@]}" > "$TMP/banco.out"; then
      cat "$TMP/banco.out"
      # 0056: traducoes do Multilingual (sem acesso nao barra: o glossario segue com os rotulos do codigo)
      bash "$0" traducoes "$MOD" 2>&1 | tail -3 | tee -a "$TMP/banco.out"
      DETAIL="$(grep -m1 '^Banco' "$TMP/banco.out" | cut -c1-300)$(grep -m1 '^Traducoes' "$TMP/banco.out" | sed 's/^/ · /' | cut -c1-120)"
      for d in $(open_docs "$MOD"); do progress "$MOD" "$d" "$(jq -n --arg dt "$DETAIL" '{step: "banco", status: "completed", detail: $dt, log: $dt, kind: "progress"}')"; done
    else
      RC=$?; cat "$TMP/banco.out"
      MSG="Sem acesso ao banco $ENVN ($HOSTA) — ligue a VPN e confira a credencial (prmake-skills.sh db-credentials); o documento registra GAP se seguir sem o banco"
      for d in $(open_docs "$MOD"); do progress "$MOD" "$d" "$(jq -n --arg m "$MSG" '{step: "banco", status: "failed", detail: $m, log: $m, kind: "error"}')"; done
      die "$MSG" 
    fi
    ;;

  traducoes)
    MOD="${1:?modulo}"; D="$(moddir "$MOD")"
    [[ -s "$D/inventario.json" ]] || die "rode antes: re.sh inventario $MOD"
    mkdir -p "$D/banco"
    api GET "/settings"; check; cp "$TMP/resp" "$TMP/settings.json"
    [[ -x "$MLG_PY" ]] || die "venv da skill base-solvace nao encontrado ($MLG_PY) — rode: prmake-skills.sh update --force base-solvace"
    "$MLG_PY" "$TRAD_PY" --settings "$TMP/settings.json" --inventario "$D/inventario.json" --catalogo "$D/banco/catalogo.json" --out "$D/banco/traducoes.json"
    ;;

  infra)
    # 0058 — etapa OPCIONAL: so roda quando o usuario pede. Somente leitura (list/describe/get); nunca le valor de segredo.
    MOD="${1:?modulo}"; shift
    command -v aws >/dev/null || die "AWS CLI nao encontrado (aws) — instale e configure um perfil (aws configure list-profiles)"
    D="$(moddir "$MOD")"; [[ -s "$D/inventario.json" ]] || die "rode antes: re.sh inventario $MOD (a infra liga os recursos ao que o codigo cita)"
    api GET "/settings"; check; cp "$TMP/resp" "$TMP/settings.json"
    ARGS=(--module "$MOD" --out "$D/infra" --inventario "$D/inventario.json" --settings "$TMP/settings.json")
    # 0066: retrato da conta (re.sh retrato infra) dentro da idade maxima = sem os ~15 min de leitura; --ao-vivo le a AWS
    if [[ " $* " == *" --ao-vivo "* ]]; then ARGS+=(--retrato-dir "$RETRATO/infra" --retrato-max-dias 0 --gravar-retrato)
    else ARGS+=(--retrato-dir "$RETRATO/infra" --retrato-max-dias "$(gen snapshotMaxAgeDays 7)" --gravar-retrato); fi
    # sigla do banco (se ja rodou `banco`) ajuda a ligar recursos pelo nome
    SG="$(jq -r '.sigla // empty' "$D/banco/catalogo.json" 2>/dev/null)"; [[ -n "$SG" ]] && ARGS+=(--sigla "$SG")
    for a in "$@"; do [[ "$a" == --ao-vivo ]] || ARGS+=("$a"); done
    for d in $(open_docs "$MOD"); do progress "$MOD" "$d" '{"step":"infra","title":"Infra na AWS (opcional)","status":"running","activity":"Mapeando a infra na AWS (somente leitura): Lambdas, S3, esteiras, segredos, logs..."}'; done
    if python3 "$INFRA_PY" "${ARGS[@]}" > "$TMP/infra.out" 2>&1; then
      cat "$TMP/infra.out"
      # 0059: envia o mapa do modulo para a aba Infra da tela (substitui o anterior); falha aqui nao perde o local
      jq -c '{account: ([.accounts[].account] | join(",")), data: .}' "$D/infra/payload.json" > "$TMP/infra-body.json"
      api PUT "/modules/$(urlenc "$MOD")/infra" "$TMP/infra-body.json"
      if [[ "$CODE" =~ ^2 ]]; then echo "Infra enviada para a aba Infra do modulo na tela."; else echo "AVISO: infra nao enviada para a tela (HTTP $CODE: $(jq -r '.error // .title // .' "$TMP/resp" 2>/dev/null | head -c 200)) — fica so em $D/infra" >&2; fi
      DETAIL="$(grep -m1 '^Infra:' "$TMP/infra.out" | cut -c1-300)"
      for d in $(open_docs "$MOD"); do progress "$MOD" "$d" "$(jq -n --arg dt "$DETAIL" '{step: "infra", status: "completed", detail: $dt, log: $dt, kind: "progress"}')"; done
      echo "Leia: $D/infra/modulo.md (recursos do modulo, logs, esteiras) e resumo.md (a conta toda). Guia: references/infra.md"
    else
      cat "$TMP/infra.out"
      MSG="Infra nao mapeada — confira o perfil do AWS CLI (aws sts get-caller-identity) e as permissoes de leitura; a etapa e opcional: o documento segue com GAP 'infra nao lida'"
      for d in $(open_docs "$MOD"); do progress "$MOD" "$d" "$(jq -n --arg m "$MSG" '{step: "infra", status: "failed", detail: $m, log: $m, kind: "warning"}')"; done
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
    python3 "$TOOL_PY" termos "$INV" --banco "$(moddir "$MOD")/banco" --exclusoes "$EXCL" --outros-modulos "$OUTROS" --out "$(moddir "$MOD")/inventario-termos.json" | tee "$TMP/termos.out"
    # 0060: sem traducoes, a tela mostra o aviso (os cards dos clientes vem em ingles)
    if grep -q '^AVISO: sem traducoes' "$TMP/termos.out"; then
      for d in $(open_docs "$MOD"); do progress "$MOD" "$d" '{"log":"Sem traducoes EN/ES do Multilingual: o glossario nao tera os nomes em ingles das telas (cards em ingles nao acham). Configure a credencial e rode re.sh traducoes","kind":"warning"}'; done
    fi
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
      # 0056: fonte arquivo -> git na pasta dele, diff so do arquivo
      if [[ -f "$path" ]]; then gdir="$(dirname "$path")"; gspec="$(basename "$path")"; else gdir="$path"; gspec="."; fi
      if [[ -z "$sha" || ! -e "$path" ]]; then echo "- $role $path: sem commit gravado/caminho ausente — revise pelo inventario completo" >> "$OUT"; continue; fi
      if ! git -C "$gdir" cat-file -e "$sha^{commit}" 2>/dev/null; then echo "- $role $path: commit $sha nao existe mais neste clone — revise pelo inventario completo" >> "$OUT"; continue; fi
      CH="$(git -C "$gdir" diff --name-status "$sha"..HEAD -- "$gspec" 2>/dev/null)"
      echo "- $role \`$path\` desde $sha: $(printf '%s' "$CH" | grep -c . | tr -d ' ') arquivo(s)" >> "$OUT"
      printf '%s\n' "$CH" | grep . | sed 's/^/  - /' | head -200 >> "$OUT"
      # linhas alteradas (lado antigo = o que o documento publicado cita) por arquivo: "arquivo<TAB>ini-fim,ini-fim"
      git -C "$gdir" diff -U0 "$sha"..HEAD -- "$gspec" 2>/dev/null | awk '
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

  perguntas)
    MOD="${1:?modulo}"; DOC=pratica; F="$(doc_file "$MOD" "$DOC" "${2:-}")"; D="$(docdir "$MOD" "$DOC")"
    [[ -s "$D/perguntas.json" ]] || die "sem perguntas no pacote — rode re.sh start $MOD pratica"
    python3 "$TOOL_PY" perguntas "$F" "$D/perguntas.json" --json "$D/cobertura.json"
    ;;

  check)
    MOD="${1:?modulo}"; DOC="${2:?documento}"; F="$(doc_file "$MOD" "$DOC" "${3:-}")"
    INV="$(moddir "$MOD")/inventario.json"; RATIO=null
    if [[ "$DOC" == pratica ]]; then INV=/dev/null; fi
    progress "$MOD" "$DOC" '{"step":"checagem","status":"running","activity":"Checando estrutura e cobertura do inventario"}'
    if [[ "$DOC" == pratica && -s "$(docdir "$MOD" "$DOC")/perguntas.json" ]]; then
      # visão prática: a cobertura é das perguntas reais do Pergunte sobre o módulo
      python3 "$TOOL_PY" perguntas "$F" "$(docdir "$MOD" "$DOC")/perguntas.json" --json "$(docdir "$MOD" "$DOC")/cobertura.json"
      RATIO="$(jq -r '.ratio // "null"' "$(docdir "$MOD" "$DOC")/cobertura.json")"
    elif [[ -s "$INV" ]]; then
      EXTRA=(); while IFS= read -r x; do EXTRA+=("$x"); done < <(extra_inv "$MOD")
      python3 "$TOOL_PY" cobertura "$INV" "$F" "$DOC" --json "$(docdir "$MOD" "$DOC")/cobertura.json" --max "${RE_MAX_MISSING:-60}" "${EXTRA[@]+"${EXTRA[@]}"}"
      RATIO="$(jq -r '.ratio // "null"' "$(docdir "$MOD" "$DOC")/cobertura.json")"
    else
      echo "(sem inventario — rode: re.sh inventario $MOD)"
    fi
    # 0066: evidencia (arquivo:linha e literais) conferida nas fontes — aviso para o revisor, nao barra
    if [[ "$DOC" != pratica && -s "$(moddir "$MOD")/inventario.json" ]]; then
      python3 "$PAC_PY" evidencia "$F" "$(moddir "$MOD")/inventario.json" --json "$(docdir "$MOD" "$DOC")/evidencia.json" --max "${RE_MAX_EVIDENCE:-25}" || true
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
    if [[ "$DOC" == pratica && -s "$(docdir "$MOD" "$DOC")/perguntas.json" ]]; then
      python3 "$TOOL_PY" perguntas "$F" "$(docdir "$MOD" "$DOC")/perguntas.json" --json "$COV" >/dev/null
    elif [[ -s "$INV" ]]; then
      EXTRA=(); while IFS= read -r x; do EXTRA+=("$x"); done < <(extra_inv "$MOD")
      python3 "$TOOL_PY" cobertura "$INV" "$F" "$DOC" --json "$COV" --max 0 "${EXTRA[@]+"${EXTRA[@]}"}" >/dev/null
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
       coverage: ($cov[0] | {total, covered, ratio, byCategory, missingCount, missing: ((.missing // [])[:200]), outsideGlossary: ((.outsideGlossary // [])[:300])}),
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

  config)
    load_config
    jq -r '"Geracao (ReverseEngineeringGeneration): " + ((.generation // {}) | tojson)' "$RE_HOME/_config.json"
    echo "Modelo dos subagentes: $(gen subagentModel sonnet) · por documento: $(gen modelByDoc '{}') · simultaneos: $(gen maxParallel 5) · area: $(gen areaBudgetKb 90) KB · checkpoint: $(gen checkpointEvery 10) itens · retrato: $(gen snapshotMaxAgeDays 7) dias"
    ;;

  areas)
    MOD="${1:?modulo}"; DOC="${2:?documento}"; D="$(docdir "$MOD" "$DOC")"; mkdir -p "$D"
    INV="$(moddir "$MOD")/inventario.json"; [[ -s "$INV" ]] || die "rode antes: re.sh inventario $MOD"
    load_config >/dev/null 2>&1 || true
    BUDGET="$(opt --orcamento "$(gen areaBudgetKb 90)" "$@")"
    # IDs usados = publicados + os documento.md desta maquina (partes, pacotes e modelos tem IDs de faixa/exemplo: fora)
    { bash "$0" ids "$MOD" 2>/dev/null | grep 'publicado' | cut -f1
      for o in "$(moddir "$MOD")"/*/documento.md; do [[ -s "$o" ]] && python3 "$TOOL_PY" ids "$o" | cut -f1; done
    } | sort -u > "$TMP/ids.txt"
    cp "$TMP/ids.txt" "$D/ids-usados.txt"
    EXTRA=(); for f in "$(moddir "$MOD")/banco/inventario-banco.json" "$(moddir "$MOD")/inventario-infra.json"; do [[ -s "$f" ]] && EXTRA+=(--extra "$f"); done
    python3 "$PAC_PY" areas "$INV" "$DOC" "${EXTRA[@]+"${EXTRA[@]}"}" --ids "$TMP/ids.txt" --modulo "$MOD" --orcamento-kb "$BUDGET" \
      --checkpoint "$(gen checkpointEvery 10)" --out "$D/areas.json" | tee "$TMP/areas.out"
    N="$(jq '.areas | length' "$D/areas.json")"
    progress "$MOD" "$DOC" "$(jq -n --arg d "$(head -1 "$TMP/areas.out")" --argjson n "$N" '{step: "leitura", status: "running", detail: "\($n) areas (pacotes de leitura)", log: $d, kind: "progress"}')"
    ;;

  pacote)
    MOD="${1:?modulo}"; DOC="${2:?documento}"; D="$(docdir "$MOD" "$DOC")"
    [[ -s "$D/areas.json" ]] || die "rode antes: re.sh areas $MOD $DOC"
    mkdir -p "$D/pacotes"
    AREA="$(opt --area "" "$@")"
    TIPOS="$(jq -r '(.docType.kinds // []) | join(",")' "$D/sessao.json" 2>/dev/null)"
    if [[ "$DOC" == visao || "$DOC" == spec-arquitetura ]]; then
      # 0066-ajustes2: sintese — itens dos levantamentos (rascunho local desta maquina, senao o publicado), sem codigo
      mkdir -p "$D/fontes"; FONTES=()
      for src in funcional arquitetura uiux; do
        L="$(moddir "$MOD")/$src/documento.md"
        if [[ -s "$L" ]]; then FONTES+=(--fonte "$src=$L"); continue; fi
        api GET "/modules/$(urlenc "$MOD")/docs/$src"
        if [[ "$CODE" =~ ^2 ]] && jq -e '.content // empty' "$TMP/resp" >/dev/null; then jq -r '.content' "$TMP/resp" > "$D/fontes/$src.md"; FONTES+=(--fonte "$src=$D/fontes/$src.md"); fi
      done
      [[ -n "$TIPOS" ]] && jq --arg t "$TIPOS" '.kinds = ($t | split(","))' "$D/areas.json" > "$TMP/a.json" && cp "$TMP/a.json" "$D/areas.json"
      python3 "$PAC_PY" sintese "$D/areas.json" "$D/pacotes" "${FONTES[@]+"${FONTES[@]}"}" --parte-dir "$D" || die "sem levantamentos para a sintese"
      python3 "$PAC_PY" cartao "$HERE/../references/subagente.md" "$D/modelo.md" "$D/modulos.tsv" --out "$D/pacotes/cartao.md" >/dev/null
      echo "Sintese: UM subagente (area 'sintese', pacote-sintese.md) escreve o documento — ou voce mesmo, sem ler codigo."
      exit 0
    fi
    python3 "$PAC_PY" pacote "$D/areas.json" "$D/pacotes" --banco "$(moddir "$MOD")/banco" --parte-dir "$D" ${AREA:+--area "$AREA"} ${TIPOS:+--tipos "$TIPOS"}
    # 0066-ajustes: o que e do modulo inteiro tem subagente proprio, despachado junto com as areas (nao a cauda da sessao
    # principal): funcional → glossario; arquitetura → banco (catalogo/retrato) e modulo (tecnologias, configuracao, seguranca...)
    if [[ -z "$AREA" ]]; then
      python3 "$PAC_PY" especiais "$D/areas.json" "$D/pacotes" --modulo-dir "$(moddir "$MOD")" --parte-dir "$D" | tee "$TMP/especiais.out"
      if grep -q 'especial' "$TMP/especiais.out"; then
        echo "Especiais: despache um subagente para cada um ($(grep -o 'especial [a-z0-9-]*' "$TMP/especiais.out" | cut -d' ' -f2 | paste -sd, -)) JUNTO com as areas — mesmo prompt, trocando a area."
      fi
    fi
    python3 "$PAC_PY" cartao "$HERE/../references/subagente.md" "$D/modelo.md" "$D/modulos.tsv" --out "$D/pacotes/cartao.md" >/dev/null
    MODEL="$(jq -r --arg d "$DOC" --arg m "$(gen subagentModel sonnet)" '(.generation.modelByDoc // {})[$d] // $m' "$RE_HOME/_config.json" 2>/dev/null)"
    echo "Cartao do subagente: $D/pacotes/cartao.md · modelo dos subagentes: ${MODEL:-sonnet} · ate $(gen maxParallel 5) ao mesmo tempo"
    ;;

  faltando)
    MOD="${1:?modulo}"; DOC="${2:?documento}"; AREA="${3:?area}"; D="$(docdir "$MOD" "$DOC")"
    [[ -s "$D/areas.json" ]] || die "rode antes: re.sh areas $MOD $DOC"
    python3 "$PAC_PY" faltando "$D/areas.json" "$AREA" "$D/parte-$AREA.md"
    ;;

  juntar)
    MOD="${1:?modulo}"; DOC="${2:?documento}"; D="$(docdir "$MOD" "$DOC")"
    PARTS=(); for f in "$D"/parte-*.md; do [[ -s "$f" ]] && PARTS+=("$f"); done
    [[ ${#PARTS[@]} -gt 0 ]] || die "nenhuma parte-*.md em $D"
    # o publicado (modo melhorar) entra primeiro: as partes completam/atualizam as secoes dele
    # a base e o documento de ANTES da primeira juntada (publicado no melhorar, ou o que a sessao escreveu): juntar de novo
    # parte sempre dela — senao os IDs ja compactados duplicariam os das partes
    [[ -s "$D/documento.base.md" ]] || { [[ -s "$D/documento.md" ]] && cp "$D/documento.md" "$D/documento.base.md"; }
    BASEDOC=(); [[ -s "$D/documento.base.md" ]] && BASEDOC=(--base "$D/documento.base.md")
    python3 "$PAC_PY" juntar "$D/documento.md" "${BASEDOC[@]+"${BASEDOC[@]}"}" "${PARTS[@]}"; RC=$?
    # 0066-ajustes2: IDs usados lidos AGORA (publicados + rascunhos locais dos outros documentos) — com dois documentos
    # gerados em paralelo, a lista do momento do `areas` nao via os IDs novos do outro (GAP/CFG colidiam)
    # (o proprio documento fica de fora — senao juntar de novo mudaria a numeracao)
    { bash "$0" ids "$MOD" 2>/dev/null | grep 'publicado' | cut -f1
      for o in "$(moddir "$MOD")"/*/documento.md; do [[ -s "$o" && "$o" != "$D/documento.md" ]] && python3 "$TOOL_PY" ids "$o" | cut -f1; done
    } | sort -u > "$TMP/ids-agora.txt"
    [[ -s "$TMP/ids-agora.txt" ]] && cat "$D/ids-usados.txt" "$TMP/ids-agora.txt" 2>/dev/null | sort -u > "$TMP/ids-todos.txt" && cp "$TMP/ids-todos.txt" "$D/ids-usados.txt"
    [[ -s "$D/areas.json" ]] && python3 "$PAC_PY" compactar "$D/documento.md" --areas "$D/areas.json" --ids "$D/ids-usados.txt"
    progress "$MOD" "$DOC" "$(jq -n --arg n "${#PARTS[@]}" '{log: "Partes das areas juntadas (\($n)) e IDs compactados", kind: "progress"}')"
    [[ $RC -eq 0 ]] || echo "AVISO: IDs repetidos entre partes (acima) — renumere numa das partes e junte de novo" >&2
    ;;

  evidencia)
    MOD="${1:?modulo}"; DOC="${2:?documento}"; F="$(doc_file "$MOD" "$DOC" "${3:-}")"
    INV="$(moddir "$MOD")/inventario.json"; [[ -s "$INV" ]] || die "rode antes: re.sh inventario $MOD (as fontes vem dele)"
    python3 "$PAC_PY" evidencia "$F" "$INV" --json "$(docdir "$MOD" "$DOC")/evidencia.json" --max "${RE_MAX_EVIDENCE:-60}"
    ;;

  retrato)
    WHAT="${1:-status}"; shift || true
    case "$WHAT" in
      status)
        for f in "$RETRATO"/banco/*/meta.json; do [[ -s "$f" ]] && jq -r --arg a "$(age_days "$f")" '"Banco \(.environment) \(.host) (\(.global) + \(.locals|join(", "))): \(.collectedAt) (\($a) dias) · jobs \(.jobs)" + (if (.problems|length) > 0 then " · lacunas: " + (.problems|join("; ")) else "" end)' "$f"; done
        for f in "$RETRATO"/infra/conta-*.json; do [[ -s "$f" ]] && jq -r --arg a "$(age_days "$f")" '"AWS conta \(.account) (\(.regions|join(", "))): \(.collectedAt) (\($a) dias) · \(.resources|length) recursos" + (if (.problems.denied|length) > 0 then " · \(.problems.denied|length) leituras sem permissao" else "" end)' "$f"; done
        ls "$RETRATO"/banco/*/meta.json "$RETRATO"/infra/conta-*.json >/dev/null 2>&1 || echo "Nenhum retrato ainda — re.sh retrato banco · re.sh retrato infra"
        echo "Idade maxima para uso: $(gen snapshotMaxAgeDays 7) dias (ReverseEngineeringGeneration.snapshotMaxAgeDays)"
        ;;
      banco)
        [[ -f "$SQL_SH" ]] || die "sql-query.sh nao encontrado ($SQL_SH) — instale a skill analisar-bug"
        api GET "/settings"; check; cp "$TMP/resp" "$TMP/settings.json"
        jq -e '.referenceDatabase.global' "$TMP/settings.json" >/dev/null || die "banco de referencia nao configurado (ReverseEngineeringReferenceDatabase)"
        ENVN="$(jq -r '.referenceDatabase.environment' "$TMP/settings.json")"; HOSTA="$(jq -r '.referenceDatabase.host' "$TMP/settings.json")"
        RDIR="$RETRATO/banco/$ENVN-$HOSTA"
        if [[ " $* " != *" --forcar "* ]] && fresh "$RDIR/meta.json" 1; then echo "Retrato do banco de hoje ja existe ($RDIR) — --forcar para ler de novo"; exit 0; fi
        ARGS=(retrato --sql "$SQL_SH" --host "$HOSTA" --environment "$ENVN" --global "$(jq -r '.referenceDatabase.global' "$TMP/settings.json")" --out "$RDIR")
        while IFS= read -r l; do ARGS+=(--local "$l"); done < <(jq -r '.referenceDatabase.locals[]?' "$TMP/settings.json")
        echo "Retrato do banco $ENVN ($HOSTA) — somente leitura de metadados; leva alguns minutos..."
        python3 "$BANCO_PY" "${ARGS[@]}" || die "retrato do banco falhou (VPN? credencial do alias $HOSTA?)"
        ;;
      infra)
        command -v aws >/dev/null || die "AWS CLI nao encontrado (aws)"
        api GET "/settings"; check; cp "$TMP/resp" "$TMP/settings.json"
        mkdir -p "$RETRATO/infra/_ultima"
        echo "Retrato da conta AWS (somente leitura; ~15 min)..."
        python3 "$INFRA_PY" --module _conta --so-conta --out "$RETRATO/infra/_ultima" --settings "$TMP/settings.json" \
          --retrato-dir "$RETRATO/infra" --retrato-max-dias 0 --gravar-retrato "$@" || die "retrato da AWS falhou (perfil do AWS CLI? aws sts get-caller-identity)"
        ;;
      *) die "uso: re.sh retrato banco|infra|status [--forcar]" ;;
    esac
    ;;

  armadilhas)
    MOD="${1:?modulo}"; api GET "/traps?module=$(urlenc "$MOD")"; check
    jq -r 'if length == 0 then "Nenhuma armadilha registrada." else .[] | "- \(.title)\(if .needsReview then " (a conferir)" else "" end) · itens: \(.items | join(", ")) · cards: \(.cards | join(", "))" end' "$TMP/resp"
    ;;

  armadilha)
    MOD="${1:?modulo}"; shift
    TF="$(opt --texto-file "" "$@")"; [[ -s "$TF" ]] || die "informe --texto-file com o texto (sintoma, causa, como diagnosticar)"
    jq -n --arg t "$(opt --titulo "" "$@")" --rawfile x "$TF" --arg i "$(opt --itens "" "$@")" --arg c "$(opt --cards "" "$@")" --arg o "$(opt --origem learning "$@")" \
      '[{title: $t, text: $x, items: ($i | split(",") | map(select(length > 0))), cards: ($c | split(",") | map(select(length > 0))), origin: $o}]' > "$TMP/body"
    api POST "/modules/$(urlenc "$MOD")/traps" "$TMP/body"; check
    jq -r '.[] | "Armadilha registrada: \(.title)\(if .needsReview then " (a conferir por um aprovador)" else "" end) · itens \(.items | join(", "))"' "$TMP/resp"
    ;;

  migrar)
    MOD="${1:?modulo}"; FILE="${2:?migracao.json}"; [[ -s "$FILE" ]] || die "arquivo nao encontrado: $FILE"
    jq -e '(.traps // []) | type == "array"' "$FILE" >/dev/null || die "$FILE: {\"traps\": [...], \"suggestions\": [...]}"
    if [[ "$(jq '(.traps // []) | length' "$FILE")" -gt 0 ]]; then
      jq '[.traps[] | {title, text, items: (.items // []), cards: (.cards // []), origin: "migrated"}]' "$FILE" > "$TMP/body"
      api POST "/modules/$(urlenc "$MOD")/traps" "$TMP/body"; check
      echo "Armadilhas ligadas aos itens (a conferir): $(jq length "$TMP/resp")"
    fi
    for row in $(jq -c '(.suggestions // [])[]' "$FILE"); do
      jq -n --argjson r "$row" '{itemId: $r.itemId, sectionKey: $r.sectionKey}' > "$TMP/body"
      api POST "/suggestions/$(jq -r '.id' <<<"$row")/item" "$TMP/body"; check
    done
    echo "Sugestoes ligadas a itens: $(jq '(.suggestions // []) | length' "$FILE")"
    api POST "/modules/$(urlenc "$MOD")/traps/migrated"; check
    echo "Migracao registrada: a secao antiga de armadilhas sai do espelho; as novas aparecem nas analises (marcadas 'a conferir' ate um aprovador conferir na tela)."
    for d in $(open_docs "$MOD"); do progress "$MOD" "$d" '{"log":"Armadilhas antigas e sugestoes ligadas aos itens (migracao 0054)","kind":"progress"}'; done
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
    # 0054: as armadilhas (o que ja deu errado) vem junto com o item
    jq -r '.[] | "--- \(.ref) (\(.docType), v\(.sectionVersion))\(if (.referencedBy|length) > 0 then " · citado por: " + (.referencedBy[:8]|join(", ")) else "" end)\n\(.body)\n",
      (.traps // [] | .[] | "  ARMADILHA\(if .needsReview then " (a conferir)" else "" end): \(.title)\(if (.cards|length) > 0 then " · cards " + (.cards[:4]|join(", ")) else "" end)\n    \(.text | gsub("\n"; "\n    "))\n")' "$TMP/resp"
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

  ""|-h|--help|help) sed -n '2,45p' "$0" | sed 's/^# \{0,1\}//' ;;
  *) die "comando desconhecido: $CMD (re.sh help)" ;;
esac
