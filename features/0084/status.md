# Status — Feature 0084

| Fase | Descrição | Status | Responsável | Commits | Notas |
|---|---|---|---|---|---|
| F1 | Tour genérico | concluída | Claude | front | `prepare`/`requires` no passo, `prepare`/`can` no host, `hasCard` opcional, `help` na definição (as dicas da tela do card saíram do diálogo para `pr-screen.tour.ts`), `stop(tourId)` |
| F2 | Tour da Base Solvace | concluída | Claude | front | `components/guided-tour/base-solvace.tour.ts`; âncoras em `architecture.component.html`, `kb-friendly-overview` e no menu do usuário (`user-menu`); selo de IA lê `Architecture/ask/status` e relê ao fechar "Minhas integrações" |
| Q1 | Build + e2e | concluída (local) | Claude | | `ng build` ok; harness `.t0084` (Postgres 55484, auth migrada no mesmo banco p/ tokens com ExternalId, seed de 7 sistemas + 3 artigos do KC); roteiros em `e2e/` |

## Log
- 2026-11-04 — spec, plano, F1, F2 e Q1 local. Testado: abre sozinho na primeira entrada (visão geral) e não reabre;
  usuário comum 17 passos, admin 21; o tour abre o mapa e um sistema sozinho e termina de volta na visão geral; passo
  opcional sem elemento (ex.: Knowledge Center vazio, "Detalhes técnicos" no Técnico) some sem furar a contagem;
  celular abre/fecha a gaveta da lista; guia "Como usar esta tela" esconde os passos de admin de quem não é; "Mostrar na
  tela" de um passo de sistema abre o sistema; selo de IA abre Minhas integrações. Regressão: tela do card (0081) igual.

## Como funciona
- Primeira entrada com link direto para uma seção (`?p=`, `?ask=`…) não é interrompida: o tour espera a pessoa chegar
  à visão geral.
- O sistema mostrado no tour é o aberto ou o "mais completo" (guia no Simples, ligações, engenharia reversa para admin).
- Flag por usuário no localStorage `prmake.tour.base-solvace.<externalId>`. Mudou o uso da tela: ajuste o passo e suba
  `version` (+ `since` dos passos novos) — quem já viu recebe só as novidades.
- Troca de aba interna (0065) encerra o tour (a tela fica guardada e o balão ficaria por cima da outra aba).

## Pendências
- Validar com usuários reais (QA/suporte) os textos e a ordem dos passos.
