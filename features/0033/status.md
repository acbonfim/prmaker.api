# Status — Feature 0033

| Fase | Descrição | Status |
|---|---|---|
| D0 | Decisões: KC pelo banco Aurora local; vigia opcional por pessoa | ✅ concluída |
| K0 | Credencial do KC DEV + leitura testada (db `KnowledgeCenter`, schema `knowledge_center`) | ✅ acesso ok (falta o `kc-query.sh`, fase S3) |
| B1 | Plugin Knowledge Center Configurations (ambiente/alias/schema) no Skills/config | ⬜ pendente |
| K1 | `kc-query.sh` + sincronização incremental + teste do filtro | ⬜ pendente |
| B2 | Módulo de arquitetura (projetos, seções versionadas, índice, export) | ⬜ pendente |
| B3 | Chat de IA com histórico + endpoint de sugestão | ⬜ pendente |
| B4 | Sessão do executor no plano, custo da análise, pedido de continuar | ⬜ pendente |
| F1 | Tela Arquitetura (leitura) | ⬜ pendente |
| F2 | Edição, versões e chat de melhoria (admin) | ⬜ pendente |
| F3 | Retomar no Claude + custo no plano de execução | ⬜ pendente |
| S1 | Skill mapear-arquitetura + espelho local | ⬜ pendente |
| S2 | Geração da base (semente claude-global + projetos por prioridade) | ⬜ pendente |
| S3 | analisar-bug enxuta (references, contexto, arquitetura, KC, imagens, saídas) | ⬜ pendente |
| S4 | prmake-card / resume; vigia opcional | ⬜ pendente |
| Q1 | Medição antes/depois, teste local, PRs | ⬜ pendente |

## Log
- 2026-09-30: análise e plano (`plan.md`). Worktrees `prform.api-0033` e `prform-app-0033` na branch `feature/0033`.
  Linha de base de custo: card 74775 = 120 turnos, 12,7 M tokens de cache lidos, 72 mil de saída.
- 2026-09-30: D0 — KC pelo banco Aurora (somente leitura, máquina local, credencial em `~/.claude/`), ambiente no
  plugin do PRMake; vigia em segundo plano opcional por pessoa, desligado por padrão, nunca faz merge.
- 2026-09-30: acesso ao KC verificado só até a rede (porta 5432 do `solvace-pstgdev`/`pstgprd` responde); login
  pendente de credencial. KC passa a entrar já na primeira geração da engenharia reversa (S2).
- 2026-09-30: login testado com `multilingual/development`: conecta, acha o database `KnowledgeCenter` /
  schema `knowledge_center` (~59 artigos), mas sem USAGE/SELECT. Precisa de credencial com leitura no schema.
- 2026-09-30: credencial do KC (`app_devadmin`) testada: leitura ok, escrita recusada pela sessão somente leitura.
  DEV tem 25 artigos publicados, a maioria teste de QA; útil: ~13 do Action Plan + visão geral. Filtro de ruído no plugin.
- 2026-09-30: garantias do KC definidas pelo usuário (filtro de teste fixo, sync pelas skills, consultar sempre,
  troca para prod por configuração) — seção própria no `plan.md`.
