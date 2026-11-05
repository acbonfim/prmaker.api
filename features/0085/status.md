# Status — Feature 0085

| Fase | Descrição | Status | Responsável | Commits | Notas |
|---|---|---|---|---|---|
| B1 | Domínio, migração, API de itens, sync plano→item, cancelar com desfazer | concluída | agente B1 | e784fe9, 457f397, df0f63d | migração `WorkItems0085`; tipo do item vem do request (front manda `type`); dependências lazy p/ evitar ciclo na DI; sem projeto de testes (18 casos num console descartável) |
| B2 | Fila/executor 1.3.0, MCP, regras, seeds, especialista no servidor | em andamento | agente B2 | | |
| F1 | Página Bancada (rota, cabeçalho, lateral, plano, mobile, tour) | concluída | agente F1 | front fd3d8b5, e20af48, f808dd2 | tour 13 passos; aba em evidência automática (itens × plano); monta PRs/horas do F3 |
| F2 | Itens de trabalho no front (lista, modais, serviço) | concluída | agente F2 | front 71247a1, d079763, c90ea38, 2ced36e, 8aa9730 | 9 specs do modelo ok; "Pedir ao especialista" = mensagem `@especialista` no CaseChat; reordenar por setas |
| S1 | Skill especialista-do-caso (regras, itens, sem duplicar) | concluída | agente S1 | 5196d38 | `prmake-case.sh rules|items|catalog` (cache 10 min em dados/); testado com API falsa |
| F3 | PRs/ações GitHub+DevOps e horas do card na Bancada | concluída | agente F3 | front 586de17 | `CardPrStateService` local (providers do componente) — singleton da tela de PR intocado; chips de branch pelas regras; 18 specs ok |
| S2 | Skills executar/desfazer item, analisar-us, analisar-bug | concluída | agente S2 | 983f029, 3a29d9b | `prmake-plan.sh rules|work-items|work-task|close-pr|delete-branch`, `open-pr/worktree --item`; Kind `analisar-us`; testado com API/git falsos |
| Q1 | Build + e2e local + revisão do contrato | pendente | Claude | | |

## Log
- 2026-11-05 — spec, contexto mapeado, plano com contrato; agentes disparados em paralelo.
- 2026-11-05 — pedido 2: área de PRs (ver/abrir manual) com ações GitHub/DevOps e seção de horas (Bug × média de bugs; US planejado × realizado) → agente F3; médias por tipo semeadas pelo B2.
