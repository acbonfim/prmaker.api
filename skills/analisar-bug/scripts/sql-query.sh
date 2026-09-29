#!/usr/bin/env bash
# Wrapper SOMENTE LEITURA para consultar SQL Server (RDS) na analise de bugs.
# Repassa tudo para sql-query.py rodando no venv dedicado da skill.
#
# Exemplos:
#   sql-query.sh --host prod3 -d SolvaceDB -q "SELECT TOP 10 * FROM Users"
#   sql-query.sh --host prod -d SolvaceDB -f consulta.sql
#   echo "SELECT @@VERSION" | sql-query.sh --host prod4
#
# Aliases de host: prod | prod3 | prod4 (ou o hostname completo).
# Credenciais: ~/.claude/sqlserver-credentials.json (nunca impressas).
# GARANTIA: a query e validada como read-only E executada em transacao com ROLLBACK.
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
VENV="$DIR/../.venv/bin/python"
[[ -x "$VENV" ]] || { echo "ERRO: venv nao encontrado em $VENV (recrie: python3 -m venv ~/.claude/skills/analisar-bug/.venv && ~/.claude/skills/analisar-bug/.venv/bin/pip install python-tds)" >&2; exit 1; }
exec "$VENV" "$DIR/sql-query.py" "$@"
