# 0047 — Opus na análise, Sonnet na correção (sem o usuário trocar o modelo) e consumo por modelo

## Contexto
A análise de um card pede raciocínio (investigar código, banco, regras de negócio, propor soluções) e a correção é
execução (editar código, abrir PRs, chamados, fechamento). Hoje tudo roda no modelo que estiver configurado na máquina
de quem executa. O pedido: usar sempre a **versão mais nova do Opus na análise** e a **versão mais nova do Sonnet na
correção**, sem ninguém trocar o modelo na mão. Com a troca, o consumo exibido no PRMake precisa separar os modelos —
hoje a sessão guarda um modelo só e a estimativa pela tabela de preços cobraria tudo pelo preço dele.

## Objetivo
1. **Executor do PRMake**: o PRMake decide o modelo de cada execução pela fase do card e o executor roda o Claude Code
   com ele (`--model`). Fase = correção quando já existe plano de correção aberto ou quando as perguntas de
   `propor-solucoes` do plano de análise já foram respondidas; senão, análise. A mesma sessão continua (retomar com outro
   modelo é suportado pelo Claude Code).
2. **Configurável no PRMake** (nada fixo no código): "Skills Configurations" → `ExecutorAnalysisModel` = `opus` e
   `ExecutorCorrectionModel` = `sonnet` (os apelidos sempre apontam para a versão mais nova), semeados por migração.
   Vazio = o modelo padrão da máquina.
3. **Terminal**: a skill `analisar-bug` pede `model: opus` no cabeçalho — a resposta da análise sai no Opus. (Limitação
   do Claude Code: vale para aquela resposta; as seguintes usam o modelo da sessão.)
4. **Consumo por modelo**: executor e skill mandam os tokens separados por modelo (entrada nova, cache lido, cache
   escrito, saída, respostas); o PRMake guarda por sessão (com a linha de base por modelo da correção) e devolve por
   modelo no plano e no relatório. A tela calcula a estimativa somando cada modelo pelo seu preço e mostra uma linha por
   modelo no detalhe. Cada execução do executor grava o modelo que ela usou de fato.
5. Registros antigos (um modelo só) continuam como estão.

## Fora do escopo
- Subagente com Sonnet para a correção feita pelo terminal.
