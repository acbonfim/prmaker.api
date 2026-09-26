# Status — Feature 0016 (Limpeza dos bancos, do código e da configuração legados)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0016` no backend (`../prform.api-0016`) e no front (`../solvace.prform.web/prform-app`, só a C3), a partir de `master`.
> **Pré-requisito**: 0015 em produção.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| C1 | Remover Pomelo/SqlServer/Sqlite; `AuthenticationContext` na `PrformDatabase` | A | 0015 estável (virada em 2026-09-26) | ⬜ pendente | Claude | — |
| C2 | Valores reais fora dos `appsettings`; chaves mortas; dev local | A | 0015 estável | ⬜ pendente | Claude | — |
| C3 | Tempo real só com token (back + front) | A | 0015 estável | ⬜ pendente | Claude | — |
| C4 | Arquivos e docs obsoletos (`CLAUDE.md`, `deploy/README.md`, `deploy.md`, pipeline e scripts antigos) | A | — | ⬜ pendente | Claude | — |
| C5 | Deploy da onda A + testes | A | C1–C4 | ⬜ pendente | usuário + Claude | — |
| R1 | Rotação das credenciais expostas | B | — | ⬜ pendente | usuário + Claude | — |
| B1 | **Ponto sem volta**: remover secrets/envs de fallback (Terraform) | B | C5; SQL Server ≥ ~2026-10-26; MySQL ≥ 0015 + 30 dias | ⬜ pendente | usuário + Claude | — |
| B2 | Backups finais (mysqldump, .bak) | B | B1 | ⬜ pendente | usuário | — |
| B3 | Excluir `db30567`, `db31021` e `db70140` (Postgres gratuito, substituído pelo premium) no painel | B | B2 | ⬜ pendente | usuário | — |
| B4 | Remover o migrador | B | B3 | ⬜ pendente | Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Datas
- Virada da 0014 (auth → Postgres): 2026-09-26 → SQL Server `db30567` pode sair a partir de ~2026-10-26.
- Virada da 0015 (PR → Postgres): 2026-09-26 → MariaDB `db31021` pode sair a partir de ~2026-10-26. (A origem era **MariaDB 10.11**, não MySQL; tem também a `pullrequestslegacybackup` com 217 linhas e as tabelas `aspnet*`/`services` legadas, que precisam entrar no backup final.)
- **2026-09-26 02:19 UTC — banco trocado para o PostgreSQL premium `db70152` (EUA, Salt Lake City; fuso do servidor `America/Denver`)**, perto do Cloud Run (us-central1). O `db70140` (gratuito, Alemanha) não podia ser promovido a premium pelo painel. Cópia com `pg_dump`/`pg_restore` dos schemas `auth/prform/vacations/timeline`: 24 tabelas, 690 linhas, checksums (sessões em UTC) idênticos; nenhuma escrita no antigo durante a troca. Revisões: `cime-auth-00021-l7d`, `cime-pullrequest-00024-pf7` (env `Database__Host=db70152` no `plain_env` para forçar a revisão). O `db70140` fica parado como fallback por alguns dias → excluir na onda B (sem backup necessário além do que já está no `db70152`; um `pg_dump` final por garantia). As versões antigas dos secrets (apontando para o `db70140`) foram destruídas pelo Terraform; para voltar, reverter o `secrets.auto.tfvars` (backup no scratchpad) e aplicar.
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
| Senha do `db70140` | conversa (0014) | — (banco será excluído) | — | — |
| Senha do `db70152` (premium, em uso) | conversa (2026-09-26) | ⬜ | ⬜ (`postgres-auth-connection` e `postgres-prform-connection`) | ⬜ |
| api-key pessoal do usuário (x-api-key) | conversa (0015, Q1) | ⬜ (gerar nova no app) | — | ⬜ |

## Decisões
- D1: duas ondas (código cedo; bancos/secrets depois do período de segurança).
- D2: remoção dos secrets de fallback só com "ok" explícito do usuário (ponto sem volta).
- D3: nenhum valor real em `appsettings` versionado.
- D4: API principal com uma chave de conexão só (`PrformDatabase`).
- D5: rotação antes de apagar.
- D6: tempo real em processo continua (dev), mas só com token.

## Log
- 2026-09-26 — Planejamento: spec, `plan.md` e `status.md`; worktree `feature/0016`.
