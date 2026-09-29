# Status — Feature 0022 (Feedback na troca de tela, refresh, tela de PR limpa, card na URL)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0022` no backend (`../prform.api-0022`) e no front (`../solvace.prform.web/prform-app-0022`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| F1 | Refresh do token que nunca trava (timeout, erro → false, um por vez) | 1 | — | ✅ concluída | Claude | front `0fbc1b3` |
| F2 | Feedback global na troca de tela (barra + "Carregando…/Renovando sessão…", erro) | 1 | — | ✅ concluída | Claude | front `0fbc1b3` |
| F3 | Tela de PR começa limpa + card na URL | 1 | — | ✅ concluída | Claude | front `0fbc1b3` |
| T1 | Teste no navegador (API simulada) | 2 | F1–F3 | ✅ concluída | Claude | — |
| Q1 | PRs e deploy | 3 | T1 | ⬜ pendente | usuário + Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Notas
- **T1** — Chrome headless com a API toda simulada (nada sai para a produção), SW desligado no teste; mesmos cenários no build da `master` (**antes**) e no da 0022 (**depois**):

  | Cenário | Antes | Depois |
  |---|---|---|
  | A — token expirado, refresh responde 500 | fica em `/auth/dashboard` com a **tela em branco** (5 s) | `/login` com "Sessão encerrada" |
  | A2 — refresh não responde | tela em branco indefinidamente (17,5 s) | barra + "Renovando sessão…"; no timeout (15 s) vai para o login |
  | B — refresh ok em 2,5 s | nada na tela durante a espera | "Renovando sessão…" e depois o dashboard |
  | C — chunk da tela lento (2 × 2,5 s) | nada | barra em 0,4 s, "Carregando…" em 1,8 s; some quando a tela monta (5,1 s) |
  | D — chunk falha (sem internet) | nada acontece | "Não foi possível abrir a tela… **Recarregar**" |
  | E1 — buscar 4242 | URL sem card | `?card=4242` |
  | E2 — sair e voltar pelo menu | card vazio, **2 painéis "preenchidos" e Limpar habilitado** | tudo limpo, Limpar desabilitado |
  | E3 — buscar e F5 | volta vazio | volta no 4242 (preenchido) |
  | E4 — Limpar | — | URL sem `card` |

  Nenhum erro de página. A barra e o aviso aparecem também quando o refresh é disparado pelo guard na primeira carga.

## Log
- 2026-09-28 — Planejamento (spec, plano, status); worktrees `feature/0022` no backend e no front.
- 2026-09-28 — F1–F3 e T1 concluídas (front `0fbc1b3`). Falta o Q1 (PRs e deploy).
