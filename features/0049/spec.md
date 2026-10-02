# 0049 — Correção numa sessão nova, arquivos direto pelo PRMake e comentários agrupados (consumo do executor)

## Contexto
Card 75091: o plano de correção custou US$ 2,45 em 35 respostas — 4 mi de cache lido (65% do custo) contra 29,8 mil de
saída. Esperar o usuário não custa nada (nenhuma chamada nos intervalos); o custo vem de cada retomada: a correção
continuava a sessão da análise, e cada resposta relia ~90–140 mil tokens de contexto (~US$ 0,045 por resposta, antes de
escrever qualquer coisa), mais ~US$ 0,10 fixos por retomada para ler plano e notas. Duas retomadas (US$ 0,68) foram só
tentativas de gravar o script na pasta do card antes de usar o `prmake_file`. Com a 0047 (Sonnet na correção) retomar a
sessão do Opus ainda reescreve o cache inteiro no outro modelo.

## Objetivo
1. **Correção numa sessão nova**: quando o card passa para a correção, o executor não retoma a sessão da análise — abre
   outra com o prompt pedindo `prmake-plan.sh contexto-correcao <card>` (resumo deixado pela análise no checkpoint de
   `propor-solucoes`, respostas, comentários e arquivos). As retomadas seguintes da correção continuam essa sessão nova.
   O plano de correção fica com a sessão que está rodando (não herda a da análise) e o consumo vai para ele sem linha de
   base. Configurável: "Skills Configurations" → `ExecutorCorrectionNewSession` (padrão `true`). Funciona com o
   executor atual (decisão no servidor: o pedido chega sem sessão para retomar).
2. **Arquivos direto pelo PRMake**: no executor, scripts/análises/texto do chamado vão com `prmake_file` numa chamada
   (sem gravar local + `sync`); gravação local negada → `prmake_file` na hora, nunca `block`.
3. **Comentários em sequência = uma retomada**: um comentário espera `ExecutorNoteDelaySeconds` (padrão 120 s) antes
   de retomar; outro comentário nesse meio empurra a espera (até 3×). "Continuar"/"Começar agora" ou outra ação do
   usuário começa na hora. A tela mostra a espera e o botão.

## Fora do escopo
- Correção pelo terminal (continua na mesma conversa).
