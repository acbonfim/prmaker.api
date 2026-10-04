# Status — Feature 0055

Branch `feature/0055` em `prform.api-0055` e `prform-app-0055`. **Implementada em 2026-10-04.**

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| S1 | ✅ concluída | Claude | ver PR |
| E1 | ✅ concluída | Claude | ver PR |
| B1 | ✅ concluída | Claude | ver PR |
| F1 | ✅ concluída | Claude | ver PR (front) |
| T1 | ✅ local | Claude | — (harness fora do git em `.t0055/`) |

## Decisões
- Tokens estimados pelo tamanho do resultado da ferramenta (÷ 4): o transcript só traz o uso por resposta; para comparar
  origens a estimativa basta e não custa IA.
- Confirmação × exploração pelo nome do arquivo: leitura de um arquivo que algum item da engenharia reversa lido ANTES
  na sessão cita (`Onde:`/evidência, inclusive o bloco do contexto do card) é confirmação; o resto é exploração.
- O mesmo arquivo lido por caminho relativo, absoluto ou de outro worktree conta junto (3 últimos segmentos).
- Não é código do produto: pasta do card (`dados/`, anexos), `~/.claude`, `~/.prmake`, espelho, skills, scripts
  `prmake-*`, temporários e imagens. Worktrees da correção (`.prmake-wt/`) são código.
- `kbCalls`/`searchCalls` continuam (comparativos antigos); sessão anterior à 0055 mostra o texto de antes.
- Executor e skill com as mesmas regras (paridade testada em 6 transcripts, 4 reais); executor 1.0.10.

## T1 (local)
- Paridade Python × C#: idênticos em 4 transcripts reais de análise, no da sessão do piloto SA3 e num sintético com
  contexto, itens, confirmação por `Read` e por `sed`, exploração e subagente.
- Harness: `prmake-plan.sh start` criou o plano do card 75091; `usage` enviou "re 3× · base 1× · code-confirm 1× ·
  code-explore 1× · code-search 1×"; API devolveu `reverseShare` 0,287 e o arquivo explorado; relatório com
  `readPlans` e médias por origem; popover na tela com a tabela, o destaque da exploração e o arquivo (lacuna?).
- Testes: 6 do leitor (Python), 4 do domínio (novo projeto `solvace.executionplans.tests`).

## Log
- 2026-10-04 — pedido do usuário ("analisa o que foi lido direto da engenharia reversa ao invés de ir ao código?";
  "pode começar"; "pode mergear no final"). Spec, plano e execução no mesmo dia.
