# 0071 — Pergunte à Base Solvace: respostas completas e rápidas

## Problema
"o que preciso para transferir um usuário para outra planta?" (módulo `revamp-users`) voltou sem tela, sem regras e sem
quem pode — e levou 69,5 s. A Base tem tudo: UC-065 (tela), RN-293/294 (pré-condição, diálogo), RN-266 (quem pode),
RN-267 (efeitos), NTF-013.

## Causas (investigação de 2026-10-23, logs do Cloud Run + espelho local)
1. O Pergunte busca em **seções inteiras** (o documento `re-funcional` tem 538 mil caracteres) e leva **um bloco de
   ≤ 1.800 caracteres por seção** (`BestBlock`). O bloco escolhido foi o UC-053 (endpoint); UC-065/RN-293/294/266 ficaram fora.
2. A busca por **item** (RN/UC/TELA/PRF…, `ReverseSearch`, que o MCP usa) não é usada pelo Pergunte; os itens citam uns aos outros (`Refs`).
3. Termos com ruído ("outra", radical "plan" de planta casa com "plano").
4. Prompt técnico: "2 a 4 frases", citações [n], sem formato para quem não programa.
5. Latência: IA de plano 3,7 s + **busca ~56 s** (SQL só 1,8 s) + IA da resposta 7,3 s. `min_instances = 0`; a busca pontua as 831 seções.

## Objetivo
Resposta em português simples, para usuário não técnico: resumo, quem pode, antes de começar, passo a passo na tela,
o que acontece depois, cuidados, o que não está confirmado. E em poucos segundos.

## Fora de escopo (próximas fases)
Busca no PostgreSQL (tsvector) / índice persistido; `min_instances=1`; gerar procedimentos TUT em massa (custa API).
Teste com IA real: só perguntas avulsas (a conta tem ~US$ 4) — nada de bateria em lote.
