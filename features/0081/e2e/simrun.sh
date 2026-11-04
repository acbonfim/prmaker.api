#!/usr/bin/env bash
# 0081: simula o executor pegando e rodando um pedido do card (para o ícone animado do cabeçalho).
cd "$(dirname "$0")"; A=http://127.0.0.1:5083/api/v1; B=$(cat bruno.txt); W=$(cat worker.txt)
w() { curl -sS -X "$1" "$A/$2" -H "x-api-key: $W" -H 'Content-Type: application/json' -d "${3:-{\}}"; }
w POST ExecutionWorker/report '{"status":"idle","version":"1.2.1","running":0}' >/dev/null
curl -sS -X POST "$A/ExecutionQueue" -H "x-api-key: $B" -H 'Content-Type: application/json' -d '{"cardNumber":"77881","kind":"resume"}' > req.json
sleep 1
w GET ExecutionQueue/next > claim.json; RID=$(python3 -c "import json;print(json.load(open('claim.json'))['request']['id'])")
echo "$RID" > rid-run.txt
w POST "ExecutionQueue/$RID/start" '{"pid":4242,"sessionId":"sess-0081"}' >/dev/null
now() { python3 -c "import datetime;print(datetime.datetime.now(datetime.timezone.utc).isoformat())"; }
w POST "ExecutionQueue/$RID/heartbeat" "{\"pid\":4242,\"activity\":{\"label\":\"Lendo src/app/upload/upload.service.ts\",\"tool\":\"Read\",\"at\":\"$(now)\"},\"recent\":[{\"label\":\"Consultou a Base Solvace: upload de evidências\",\"tool\":\"mcp\",\"at\":\"$(now)\"},{\"label\":\"Buscou 'aceita PNG' no código\",\"tool\":\"Grep\",\"at\":\"$(now)\"},{\"label\":\"Lendo src/app/upload/upload.service.ts\",\"tool\":\"Read\",\"at\":\"$(now)\"}]}" >/dev/null
echo "running $RID"
