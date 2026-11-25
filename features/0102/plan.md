# Plano 0102 — Perguntas da Base Solvace pelo executor

Branches `feature/0102` em `prform.api-0102` (back, executor) e `prform-app-0102` (front), a partir da origin/master.

## Decisões

- **Flag pessoal no "AI Configurations"**: chave `BaseSolvaceRunner` = `provider` | `executor`, campo pessoal opcional que
  cai no valor global (`provider`). Semeada por migração (só a chave que falta). Em "Minhas integrações" o campo vira uma
  lista de escolha: `UserIntegrationField.options` vem do catálogo (0086) quando a chave é `Enum` sem valor livre.
- **Escopo**: Pergunte (`ask`, com anexos) e "Analisar a fundo" (`ask/deep`). Guia, aprender com card e chat de seção
  (admin) continuam no provedor.
- **Pedido novo na fila**: kind `base-ask`, pista própria `base` (2 por máquina, fora do limite das análises), só para
  executor ≥ **1.9.0**. O card do pedido é uma chave própria por pergunta (`BQ-<12 hex>`): não colide com o "um pedido
  ativo por card", e o andamento vai no grupo de sempre (`execplan:BQ-…`, evento `executionPlanUpdated`).
- **Pergunta** (`execution."BaseQuestions"`): dono, texto, deep, anexos (base64, apagados ao responder), pedido, resposta
  (JSON de `ArchitectureAskResponse`), erro. O status vem do pedido (fila/rodando/falhou/cancelado/expirou); `done` sem
  resposta = "o Claude terminou sem responder".
- **Token restrito** (como o do especialista): claim `baseAskRequest`; só `/mcp` com `prmake_base_search/get/module/impact`
  e `prmake_base_answer`, e só enquanto o pedido estiver ativo. O executor roda com `--strict-mcp-config` (só o PRMake),
  perfil de settings do especialista (sem commit/push/PR) e na pasta neutra (`desk`).
- **Resposta** pela ferramenta MCP `prmake_base_answer(question, answer, coverage, kind, refs[])`: o servidor monta
  `results` pelos refs (índice da ER), grava, registra a pergunta/lacuna como o Pergunte faz e avisa a tela.
- **Modelo/effort** pela "Skills Configurations": `BaseAskExecutorModel` (padrão `sonnet`), `BaseAskExecutorEffort`
  (padrão `low`), `BaseAskExecutorDeepEffort` (padrão `medium`). O claim leva `Effort`; o executor passa `--effort`.
- **Anexos** vão no claim (`Files`); o executor grava numa pasta do pedido e o prompt manda ler com `Read`.

## Contratos

| Método | Rota | O quê |
|---|---|---|
| GET | `Architecture/ask/status` | + `runner: 'provider'\|'executor'`, `executorOnline: boolean`, `executorReason?: string` |
| POST | `Architecture/ask/executor` | `{ question, deep?, attachments? }` → `BaseQuestionResponse` (409 `NO_EXECUTOR` / `OUTDATED_EXECUTOR`) |
| GET | `Architecture/ask/executor/{id}` | `BaseQuestionResponse` |
| POST | `Architecture/ask/executor/{id}/cancel` | cancela o pedido |

```ts
interface BaseQuestionResponse {
  id: string; key: string;            // key = 'BQ-…' (grupo realtime execplan:{key})
  question: string; deep: boolean;
  status: 'queued'|'claimed'|'running'|'done'|'failed'|'cancelled'|'expired';
  waitReason: string | null; waitCode: string | null;
  activity: { label: string; tool: string | null; at: string } | null;
  answer: ArchitectureAskResponse | null;   // provider = 'Claude Code', model = modelo do executor
  error: string | null;
  createdAt: string; answeredAt: string | null; costUsd: number | null;
}
```

## Fases

| Fase | O quê | Depende |
|---|---|---|
| B1 | Entidade `BaseQuestion`, kind/pista `base-ask`, migração, repositório | — |
| B2 | Fila: enfileirar, claim (prompt, modelo, effort, arquivos, token), gate 1.9.0, finish sem resposta | B1 |
| B3 | Token restrito + middleware; MCP `prmake_base_answer`; endpoints `ask/executor`; `ask/status` | B2 |
| B4 | Flag `BaseSolvaceRunner` (migração + catálogo) e `options` nos campos de "Minhas integrações"; chaves do modelo | — |
| E1 | Executor 1.9.0: pista `base`, `--effort`, MCP estrito, pasta neutra, anexos | B2 |
| F1 | Minhas integrações: campo com opções vira lista de escolha | B4 |
| F2 | Pergunte/a fundo pelo executor: andamento ao vivo, cancelar, sem executor → IA do servidor desta vez; chip do modo | B3 |
| Q1 | Testes (unidade back/executor, specs front, build) | todas |
| Q2 | e2e local com executor real + Claude real (só a pergunta, assinatura) | Q1 |
