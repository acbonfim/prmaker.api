#!/usr/bin/env bash
# Cenário do card 77881 (0080): análise com decisões, correção com divergência pendente.
set -euo pipefail
cd "$(dirname "$0")"
API=http://127.0.0.1:5083/api/v1; B=$(cat bruno.txt); CARD=77881
skill() { curl -sS --fail-with-body -X "$1" "$API/$2" -H "x-api-key: $B" -H 'X-Execution-Client: skill' -H 'Content-Type: application/json' -d "${3:-{\}}"; }
user() { curl -sS --fail-with-body -X "$1" "$API/$2" -H "x-api-key: $B" -H 'Content-Type: application/json' -d "${3:-{\}}"; }

A=$(skill POST ExecutionPlan '{"cardNumber":"77881","title":"Análise do bug 77881: não é possível subir nova imagem da tela de login nos parâmetros do cliente","phase":"analysis","steps":[
 {"key":"identificar-card","title":"Identificar o card"},{"key":"coletar-dados","title":"Ler o card"},{"key":"investigar-codigo","title":"Investigar o código"},
 {"key":"consultar-ambiente","title":"Consultar dados e ambiente"},{"key":"causa-raiz","title":"Levantar a causa raiz"},{"key":"montar-analise","title":"Montar a análise e os scripts"},
 {"key":"publicar","title":"Publicar na timeline"},{"key":"propor-solucoes","title":"Propor soluções e decidir com você","kind":"question"}]}' | jq -r .id)
echo "$A" > analysis.txt
for k in identificar-card coletar-dados investigar-codigo consultar-ambiente causa-raiz montar-analise publicar; do skill PATCH "ExecutionPlan/$A/steps/$k" '{"status":"completed"}' >/dev/null; done
skill PATCH "ExecutionPlan/$A/steps/propor-solucoes" '{"status":"running"}' >/dev/null
Q=$(skill POST "ExecutionPlan/$A/questions" '{"questions":[
 {"stepKey":"propor-solucoes","text":"Qual solução seguir? (detalhes em solucoes.md)","options":[{"label":"Validação no próprio endpoint","recommended":true},{"label":"Limite pelo parâmetro de upload"}]},
 {"stepKey":"propor-solucoes","text":"O limite de 300 KB vale para quais imagens do upload-image?","options":[{"label":"As três (Tela Login, Logo e Logo Negativo)"},{"label":"Só a Tela Login"}]},
 {"stepKey":"propor-solucoes","text":"Além de .png, o fundo do login deve aceitar .jpeg?","options":[{"label":"Não: só .jpg e .png"},{"label":"Sim"}]},
 {"stepKey":"propor-solucoes","text":"Quais PRs abrir no edv-solvace-api?","options":[{"label":"Só qa por enquanto (bugfix/77881-qa)"}]},
 {"stepKey":"propor-solucoes","text":"Classificação e fechamento: posso mover o card para Test in production ao final?","options":[{"label":"nao alterar isso"}]},
 {"stepKey":"propor-solucoes","text":"Posso seguir com a correção agora (worktree, commit e PR, sem merge)?","options":[{"label":"Não, só deixar o plano de correção pronto"},{"label":"Sim"}]}]}')
echo "$Q" | jq -r '.[] | .id + "|" + (.options[0].label)' | while IFS='|' read -r qid ans; do user POST "ExecutionPlan/$A/questions/$qid/answer" "$(jq -n --arg a "$ans" '{answer:$a}')" >/dev/null; done
skill PATCH "ExecutionPlan/$A/steps/propor-solucoes" '{"status":"completed"}' >/dev/null

C=$(skill POST ExecutionPlan "$(jq -n --arg p "$A" '{cardNumber:"77881",title:"Correção 77881: validação de tamanho (300 KB) e PNG no upload-image",phase:"correction",parentPlanId:$p,steps:[
 {key:"impacto-na-base",title:"Impacto na base"},{key:"corrigir-edv-solvace-api",title:"Corrigir EnvironmentService no edv-solvace-api",kind:"code",repository:"edv-solvace-api"},
 {key:"validar",title:"Validar build e testes",kind:"validation"},{key:"pr-edv-solvace-api",title:"PR para QA no edv-solvace-api",kind:"pr",repository:"edv-solvace-api"},
 {key:"preparar-fechamento",title:"Preparar o fechamento (root cause e resumo PT/EN)"},{key:"validar-qa",title:"Validar em QA",executor:"user",kind:"validation"},
 {key:"fechar-card",title:"Fechamento: conferir pendências"}]}')" | jq -r .id)
echo "$C" > correction.txt
skill PATCH "ExecutionPlan/$C/steps/impacto-na-base" '{"status":"completed"}' >/dev/null
skill PATCH "ExecutionPlan/$C/steps/corrigir-edv-solvace-api" '{"status":"running"}' >/dev/null
skill POST "ExecutionPlan/$C/logs" '{"logs":[{"stepKey":"corrigir-edv-solvace-api","kind":"divergence","message":"A análise foi feita na **master** (extensão do fundo fixa em `.jpg`). A branch **qa** já tem o fix do card #75706: aceita .png/.jpeg/.webp e grava sempre `background-image.jpg`. A correção na qa só acrescenta o limite de 300 KB e a mensagem — sem mexer em extensões nem no GetImage."}]}' >/dev/null
skill PATCH "ExecutionPlan/$C/steps/corrigir-edv-solvace-api" '{"status":"completed"}' >/dev/null
skill PATCH "ExecutionPlan/$C/steps/validar" '{"status":"completed"}' >/dev/null
echo "gate:"; curl -s -o /dev/null -w '%{http_code}\n' -X PATCH "$API/ExecutionPlan/$C/steps/pr-edv-solvace-api" -H "x-api-key: $B" -H 'X-Execution-Client: skill' -H 'Content-Type: application/json' -d '{"status":"running"}'
echo "analysis=$A correction=$C"
