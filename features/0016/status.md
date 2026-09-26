# Status — Feature 0016 (Limpeza dos bancos, do código e da configuração legados)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0016` no backend (`../prform.api-0016`) e no front (`../solvace.prform.web/prform-app`, só a C3), a partir de `master`.
> **Pré-requisito**: 0015 em produção.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| C1 | Remover Pomelo/SqlServer/Sqlite; `AuthenticationContext` na `PrformDatabase` | A | 0015 estável | ⬜ pendente | Claude | — |
| C2 | Valores reais fora dos `appsettings`; chaves mortas; dev local | A | 0015 estável | ⬜ pendente | Claude | — |
| C3 | Tempo real só com token (back + front) | A | 0015 estável | ⬜ pendente | Claude | — |
| C4 | Arquivos e docs obsoletos (`CLAUDE.md`, `deploy/README.md`, `deploy.md`, pipeline e scripts antigos) | A | — | ⬜ pendente | Claude | — |
| C5 | Deploy da onda A + testes | A | C1–C4 | ⬜ pendente | usuário + Claude | — |
| R1 | Rotação das credenciais expostas | B | — | ⬜ pendente | usuário + Claude | — |
| B1 | **Ponto sem volta**: remover secrets/envs de fallback (Terraform) | B | C5; SQL Server ≥ ~2026-10-26; MySQL ≥ 0015 + 30 dias | ⬜ pendente | usuário + Claude | — |
| B2 | Backups finais (mysqldump, .bak) | B | B1 | ⬜ pendente | usuário | — |
| B3 | Excluir `db30567` e `db31021` no painel | B | B2 | ⬜ pendente | usuário | — |
| B4 | Remover o migrador | B | B3 | ⬜ pendente | Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Datas
- Virada da 0014 (auth → Postgres): 2026-09-26 → SQL Server `db30567` pode sair a partir de ~2026-10-26.
- Virada da 0015 (PR → Postgres): a preencher → MySQL `db31021` pode sair 30 dias depois.
- Suporte ao .NET 8 acaba em 2026-11-10: a onda A (sem Pomelo) precisa estar pronta antes do upgrade.

## Checklist de rotação (R1)
| Credencial | Onde está exposta | Trocada no provedor | Secret atualizado | Teste |
|---|---|---|---|---|
| Senha SMTP (`email-password`) | `appsettings` da auth (git) | ⬜ | ⬜ | ⬜ |
| Token GitHub (`github-token`) | `appsettings` da API (git) | ⬜ | ⬜ | ⬜ |
| PAT Azure DevOps (`azuredevops-pat`) | `appsettings` da API (git) | ⬜ | ⬜ | ⬜ |
| Segredo JWT (`jwt-secret`) | `appsettings` da API (git) | ⬜ | ⬜ | ⬜ |
| Chaves de IA | `appsettings` da API (git) | ⬜ | ⬜ | ⬜ |
| Senha Web Deploy MonsterASP | conversa (0013) | ⬜ | ⬜ | ⬜ |
| Senha do `db70140` | conversa (0014) | ⬜ | ⬜ | ⬜ |

## Decisões
- D1: duas ondas (código cedo; bancos/secrets depois do período de segurança).
- D2: remoção dos secrets de fallback só com "ok" explícito do usuário (ponto sem volta).
- D3: nenhum valor real em `appsettings` versionado.
- D4: API principal com uma chave de conexão só (`PrformDatabase`).
- D5: rotação antes de apagar.
- D6: tempo real em processo continua (dev), mas só com token.

## Log
- 2026-09-26 — Planejamento: spec, `plan.md` e `status.md`; worktree `feature/0016`.
