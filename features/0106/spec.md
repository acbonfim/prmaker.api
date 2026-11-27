# 0106 — Aprender com os erros (lições) e pedido de mudanças nos PRs

Pedido (2026-11-27, Alex Carlos): "no card 79047 o agente cometeu alguns erros. Eu preciso que aprenda com os erros e
atualize as skills, mas também realize de alguma maneira algo pra que esses casos se retroalimentem e possa aprender com
esses erros e não repita, sem intervenção humana. Outro ponto, a gente não tem um fluxo pra quando recebemos request
change em PRs, a aplicação se perdeu um pouco pois foi algo fora do comum. Então preciso que a aplicação saiba lidar com
esse tipo de situação de uma maneira mais simples."

## O que deu errado no 79047

| # | O que aconteceu | Lição |
|---|---|---|
| E1 | Pediram "análise inicial" e o especialista montou um item à mão (#1174) em vez da análise do padrão; disse que não conseguia iniciá-la. | "Análise inicial" é sempre o plano de análise do padrão — o especialista pede pela ferramenta `prmake_analysis_start` (mesmo botão da Bancada), nunca monta item à mão. |
| E2 | "Não consigo ler os comentários do PR: os repositórios ficam no Azure DevOps" — errado (GitHub `electradv/*`), e não havia ferramenta para ler o review. | Os repositórios dos cards ficam no GitHub; review e comentários do PR se leem com `prmake_pr_reviews`. Nunca afirmar sem chamar a ferramenta. |
| E3 | O item de revisão (#1203) publicou na Timeline sem ninguém pedir (o modelo `revisar-pr` tinha a sub-tarefa fixa). | Timeline só a pedido: o modelo passa a perguntar (`timeline`, padrão "não"). |
| E4 | A revisão misturou achados de código que o PR não mexe; a pessoa teve de pedir "só o código novo". | Pedido de mudanças de um PR se avalia só no código que o PR muda (linhas adicionadas); o resto vai separado, como seguimento. |
| E5 | O PR de `development` (#31429) já estava mesclado; a etapa de PRs disse que "as regras não têm nome de branch para um segundo PR" e o ajuste ficou fora do development. | Destino com PR já mesclado: os commits novos vão para a MESMA branch do destino (`hotfix/79047-dev`) e abre-se um PR novo dela — o nome da branch é o mesmo da regra. |
| E6 | Depois dos ajustes ninguém respondeu os comentários no PR nem pediu nova revisão. | Ajuste de review termina respondendo cada comentário no PR (o que mudou, commit) e pedindo nova revisão. |
| E7 | O especialista passou a responsabilidade ("a etapa de PRs deve tratar isso; se não tratar, me avise"). | Guardião confere e resolve (ajusta a instrução da etapa) em vez de pedir para a pessoa vigiar. |

## O que entregar

1. **Skills corrigidas** com E1–E7 (especialista-do-caso, executar-item-de-trabalho, analisar-bug/us, especialista de
   gestão e validar-ambiente na parte de lições).
2. **Lições aprendidas (retroalimentação sem pessoa):**
   - O PRMake guarda lições (`quando` → `faça/nunca`, por que, de onde veio, quantas vezes aconteceu).
   - Qualquer agente que erra e é corrigido grava a lição na hora (`prmake_lesson_record`); lição parecida com uma que
     já existe **reforça** a existente (conta a reincidência) em vez de duplicar.
   - O PRMake registra sozinho **sinais de erro** (item criado pelo especialista cancelado logo depois, entrega refeita,
     item reaberto para refazer, pedido de mudanças num PR do card). O próximo agente que entra no card vê os sinais e
     os transforma em lição (ou descarta com o motivo).
   - As lições ativas entram sozinhas no que todo agente lê no início (`prmake_rules`, `prmake_case_context`),
     ordenadas pelas mais reincidentes — sem editar skill e sem pessoa no meio. Lição nunca sobrepõe as regras fixas
     nem a governança.
   - Tela (Governança → Lições) para ver, editar, aposentar e reativar; nada exige ação para funcionar.
3. **Pedido de mudanças em PR (fluxo simples):**
   - O PRMake percebe quando um PR do card recebe "changes requested" e cria sozinho o item **Atender o review do PR
     #n** (pendente), avisando na conversa do caso. Executar = 1 clique.
   - Sub-tarefas do item: ler o review e decidir ponto a ponto (só o código novo do PR) → ajustar na branch do card →
     validar → atualizar os PRs do card (PR aberto: a mesma branch, o PR se atualiza; PR já mesclado: a mesma branch +
     PR novo) → responder os comentários no PR e pedir nova revisão. Nunca mescla nem aprova.
   - Ferramentas: `prmake_pr_reviews` (ler), `prmake_pr_review_reply` (responder), `prmake_pr_request_review`.
   - A tela mostra no PR "Mudanças pedidas por X".
