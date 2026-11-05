# Status — Feature 0085

| Fase | Descrição | Status | Responsável | Commits | Notas |
|---|---|---|---|---|---|
| B1 | Domínio, migração, API de itens, sync plano→item, cancelar com desfazer | em andamento | agente B1 | | |
| B2 | Fila/executor 1.3.0, MCP, regras, seeds, especialista no servidor | em andamento | agente B2 | | |
| F1 | Página Bancada (rota, cabeçalho, lateral, plano, mobile, tour) | em andamento | agente F1 | | |
| F2 | Itens de trabalho no front (lista, modais, serviço) | em andamento | agente F2 | | |
| S1 | Skill especialista-do-caso (regras, itens, sem duplicar) | concluída | agente S1 | 5196d38 | `prmake-case.sh rules|items|catalog` (cache 10 min em dados/); testado com API falsa |
| F3 | PRs/ações GitHub+DevOps e horas do card na Bancada | em andamento | agente F3 | | pedido 2 do usuário |
| S2 | Skills executar/desfazer item, analisar-us, analisar-bug | concluída | agente S2 | 983f029, 3a29d9b | `prmake-plan.sh rules|work-items|work-task|close-pr|delete-branch`, `open-pr/worktree --item`; Kind `analisar-us`; testado com API/git falsos |
| Q1 | Build + e2e local + revisão do contrato | pendente | Claude | | |

## Log
- 2026-11-05 — spec, contexto mapeado, plano com contrato; agentes disparados em paralelo.
- 2026-11-05 — pedido 2: área de PRs (ver/abrir manual) com ações GitHub/DevOps e seção de horas (Bug × média de bugs; US planejado × realizado) → agente F3; médias por tipo semeadas pelo B2.
