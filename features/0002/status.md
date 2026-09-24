# Feature 0002 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0002` em `prform.api` (backend) e `solvace.prform.web/prform-app` (frontend), a partir de `master` (com a 0001 mergeada).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | Modelo e migração (IsPersonal + UserPluginConfigurations) | back | — | 1 | ⬜ | | | | |
| B2 | Proteção de segredos (AES-GCM) | back | — | 1 | ⬜ | | | | |
| F1 | Flag "Uso pessoal" no admin de plugins | front | contrato | 1 | ⬜ | | | | |
| B3 | Aplicação, cache por usuário e endpoints | back | B1, B2 | 2 | ⬜ | | | | |
| F2 | Modal "Minhas integrações" | front | contrato | 2 | ⬜ | | | | |
| B4 | GitHub/Azure com token pessoal + bloqueio | back | B3 | 3 | ⬜ | | | | |
| F3 | Bloqueio no front | front | F2 | 3 | ⬜ | | | | |
| Q1 | Integração, chave de criptografia e publicação | ambos | todas | 4 | ⬜ | | | | |

## Decisões

Defaults em `plan.md` §2. Registrar aqui quando confirmadas/alteradas.

| # | Situação | Observação |
|---|---|---|
| D1 | a confirmar | sem fallback para o global |
| D2 | a confirmar | todos os campos preenchidos |
| D3 | a confirmar | sensíveis criptografados e nunca devolvidos |
| D4 | a confirmar | 403 com código; tela de PR bloqueada |
| D5 | a confirmar | |
| D6 | a confirmar | |
| D7 | a confirmar | |
| D8 | a confirmar | |
| D9 | a confirmar | |

## Notas de handoff

<!-- Uma seção por fase concluída/bloqueada: o que foi feito, desvios do plano, mudanças de contrato, pendências. -->

## Log

| Data | Fase | Evento |
|---|---|---|
| 2026-09-24 | — | 0001 mergeada (PRs #5 back / #2 front); `master` local atualizada nos dois repos; branches `feature/0002` criadas a partir dela. Plano criado. |
