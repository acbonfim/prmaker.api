# Feature 0002 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0002` em `prform.api` (backend) e `solvace.prform.web/prform-app` (frontend), a partir de `master` (com a 0001 mergeada).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | Modelo e migração (IsPersonal + UserPluginConfigurations) | back | — | 1 | 🟡 | Claude (sessão principal) | 2026-09-24 | | |
| B2 | Proteção de segredos (AES-GCM) | back | — | 1 | 🟡 | Claude (sessão principal) | 2026-09-24 | | |
| F1 | Flag "Uso pessoal" no admin de plugins | front | contrato | 1 | 🟡 | Claude (sessão principal) | 2026-09-24 | | |
| B3 | Aplicação, cache por usuário e endpoints | back | B1, B2 | 2 | ⬜ | | | | |
| F2 | Modal "Minhas integrações" | front | contrato | 2 | ⬜ | | | | |
| B4 | GitHub/Azure com token pessoal + bloqueio | back | B3 | 3 | ⬜ | | | | |
| F3 | Bloqueio no front | front | F2 | 3 | ⬜ | | | | |
| Q1 | Integração, chave de criptografia e publicação | ambos | todas | 4 | ⬜ | | | | |

## Decisões

Defaults em `plan.md` §2. Registrar aqui quando confirmadas/alteradas.

| # | Situação | Observação |
|---|---|---|
| D1 | ✅ confirmada (default) | sem fallback para o global |
| D2 | ✅ confirmada (default) | todos os campos preenchidos |
| D3 | ✅ confirmada (default) | sensíveis criptografados e nunca devolvidos |
| D4 | ✅ confirmada (default) | 403 com código; tela de PR bloqueada |
| D5 | ✅ confirmada (default) | |
| D6 | ✅ confirmada (default) | |
| D7 | ✅ confirmada (default) | |
| D8 | ✅ confirmada (default) | |
| D9 | ✅ confirmada (default) | |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada: o que foi feito, desvios do plano, mudanças de contrato, pendências. -->

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | 0001 mergeada (PRs #5 back / #2 front); `master` local atualizada nos dois repos; branches `feature/0002` criadas a partir dela. Plano criado. |
| 2026-09-24 | B1, B2, F1 | Usuário aprovou o plano com os defaults D1–D9. Onda 1 iniciada. |
