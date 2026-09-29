#!/usr/bin/env bash
# Consulta o AWS Cognito para dar contexto de usuario/ambiente na analise de um bug.
# Cada ambiente/tenant Solvace tem seu proprio user pool, cujo NOME == nome do ambiente
# (ex.: demo, qa, takeda, sandboxtakeda). Este script resolve o pool pelo nome (paginando)
# e consulta usuarios/grupos.
#
# Uso:
#   cognito-query.sh pools [filtro]
#       lista pools cujo nome contem <filtro> (sem filtro = todos). So leitura.
#   cognito-query.sh user <ambiente> <emailOuUsername>
#       resolve o pool do <ambiente> e retorna o usuario (atributos, status, enabled, grupos).
#   cognito-query.sh groups <ambiente> <username>
#       lista os grupos do usuario no pool do <ambiente>.
#   cognito-query.sh pool-id <ambiente>
#       imprime apenas o UserPoolId resolvido para o <ambiente>.
#
# Env opcional:
#   AWS_REGION      = regiao (default us-east-1)
#   AWS_PROFILE     = profile do AWS CLI (senao usa o default/credenciais do ambiente)
set -euo pipefail

REGION="${AWS_REGION:-us-east-1}"
AWS=(aws --region "$REGION")
[[ -n "${AWS_PROFILE:-}" ]] && AWS+=(--profile "$AWS_PROFILE")

die() { echo "ERRO: $*" >&2; exit 1; }
command -v aws >/dev/null || die "aws CLI nao encontrado no PATH"
command -v jq  >/dev/null || die "jq nao encontrado no PATH"

# --- lista todos os pools (id + name), paginando ---
list_all_pools() {
  local token="" resp
  while :; do
    if [[ -z "$token" ]]; then
      resp="$("${AWS[@]}" cognito-idp list-user-pools --max-results 60 2>/dev/null)" || die "falha ao listar pools (credenciais AWS ok?)"
    else
      resp="$("${AWS[@]}" cognito-idp list-user-pools --max-results 60 --next-token "$token" 2>/dev/null)" || die "falha ao listar pools"
    fi
    echo "$resp" | jq -r '.UserPools[] | "\(.Id)\t\(.Name)"'
    token="$(echo "$resp" | jq -r '.NextToken // empty')"
    [[ -z "$token" ]] && break
  done
}

# --- resolve UserPoolId a partir do nome do ambiente: match exato, senao unico substring ---
resolve_pool() {
  local env="$1" all exact subs n
  [[ -n "$env" ]] || die "informe o ambiente (nome do pool)"
  all="$(list_all_pools)"
  exact="$(echo "$all" | awk -F'\t' -v e="$env" '$2==e{print $1}')"
  if [[ -n "$exact" ]]; then printf '%s' "$exact"; return; fi
  subs="$(echo "$all" | awk -F'\t' -v e="$env" 'index(tolower($2),tolower(e)){print}')"
  n="$(echo "$subs" | grep -c . || true)"
  if [[ "$n" -eq 1 ]]; then echo "$subs" | cut -f1; return; fi
  if [[ "$n" -eq 0 ]]; then die "nenhum pool com nome ~ '$env' (rode: cognito-query.sh pools $env)"; fi
  { echo "ERRO: '$env' e ambiguo, use o nome exato. Candidatos:"; echo "$subs" | sed 's/^/  /'; } >&2
  exit 1
}

CMD="${1:-}"; shift || true
case "$CMD" in
  pools)
    FILTER="${1:-}"
    if [[ -z "$FILTER" ]]; then list_all_pools | sort -t$'\t' -k2;
    else list_all_pools | awk -F'\t' -v f="$FILTER" 'index(tolower($2),tolower(f))' | sort -t$'\t' -k2; fi
    ;;

  pool-id)
    resolve_pool "${1:-}"; echo
    ;;

  user)
    ENV="${1:?informe o ambiente}"; KEY="${2:?informe o email ou username}"
    POOL="$(resolve_pool "$ENV")"
    echo ">> ambiente=$ENV pool=$POOL regiao=$REGION" >&2

    # tenta admin-get-user direto (quando KEY ja e o username)
    U="$("${AWS[@]}" cognito-idp admin-get-user --user-pool-id "$POOL" --username "$KEY" 2>/dev/null || true)"

    # senao, se parece email, busca por filtro de email e resolve o Username real
    if [[ -z "$U" && "$KEY" == *@* ]]; then
      UNAME="$("${AWS[@]}" cognito-idp list-users --user-pool-id "$POOL" \
                 --filter "email = \"$KEY\"" --limit 1 2>/dev/null \
               | jq -r '.Users[0].Username // empty')"
      [[ -n "$UNAME" ]] && U="$("${AWS[@]}" cognito-idp admin-get-user --user-pool-id "$POOL" --username "$UNAME" 2>/dev/null || true)"
    fi
    [[ -n "$U" ]] || die "usuario '$KEY' nao encontrado no pool '$ENV'"

    UNAME="$(echo "$U" | jq -r '.Username')"
    GROUPS="$("${AWS[@]}" cognito-idp admin-list-groups-for-user --user-pool-id "$POOL" --username "$UNAME" 2>/dev/null \
              | jq -c '[.Groups[].GroupName]' 2>/dev/null || echo '[]')"

    # admin-get-user usa .UserAttributes; list-users usaria .Attributes
    echo "$U" | jq --argjson groups "$GROUPS" '{
      Username,
      UserStatus,
      Enabled,
      Created: .UserCreateDate,
      Modified: .UserLastModifiedDate,
      Groups: $groups,
      Attributes: ([.UserAttributes[] | {(.Name): .Value}] | add)
    }'
    ;;

  groups)
    ENV="${1:?informe o ambiente}"; UNAME="${2:?informe o username}"
    POOL="$(resolve_pool "$ENV")"
    "${AWS[@]}" cognito-idp admin-list-groups-for-user --user-pool-id "$POOL" --username "$UNAME" \
      | jq '[.Groups[] | {GroupName, Description, Precedence}]'
    ;;

  ""|-h|--help|help)
    sed -n '2,30p' "$0"
    ;;
  *)
    die "subcomando desconhecido: '$CMD' (use: pools | user | groups | pool-id)"
    ;;
esac
