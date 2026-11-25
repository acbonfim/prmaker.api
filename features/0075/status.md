# Feature 0075 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0075` em `prform.api` (back + skills) e `solvace.prform.web/prform-app` (front).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Depende de | Onda | Status | Responsável | Início | Conclusão | Commits |
|---|---|---|---|---|---|---|---|---|---|
| B1 | Code refs: modelo, API, MCP, config | back | — | 1 | ✅ | Claude (sessão principal) | 2026-11-04 | 2026-11-04 | 50ea9d1 |
| F1 | Markdown com extensões (callouts, `ref:`, auto-link, `anexo:`) | front | — | 1 | ✅ | Claude (subagente) | 2026-11-04 | 2026-11-04 | front 1f0bb8d |
| S2 | `prmake-plan.sh coderef` + `coderef.py` | skills | B1 (teste) | 1 | ✅ | Claude (sessão principal) | 2026-11-04 | 2026-11-04 | 78b1a85 |
| F2 | Popover da ref (diff anotado, highlight) | front | B1, F1 | 2 | ✅ | Claude (subagente) | 2026-11-04 | 2026-11-04 | front 2025163 |
| F3 | Imagens `anexo:` no markdown, Antes × Depois | front | F1, B3 | 2 | ✅ | Claude (subagente) | 2026-11-04 | 2026-11-04 | front 2025163 |
| B2 | Regra das citações no `advance` | back | B1 | 2 | ✅ | Claude (sessão principal) | 2026-11-04 | 2026-11-04 | 3697f5c |
| B3 | Arquivo do plano por nome + `prmake_attachment` | back | — | 2 | ✅ | Claude (sessão principal) | 2026-11-04 | 2026-11-04 | 3697f5c |
| S1 | Modelo novo da análise e das soluções | skills | B1, S2 | 2 | ✅ | Claude (sessão principal) | 2026-11-04 | 2026-11-04 | 1ce7eec |
| S3 | Mocks visuais (`mock-shot.sh` + receitas) | skills | B3 | 2 | ✅ | Claude (sessão principal) | 2026-11-04 | 2026-11-04 | 1ce7eec |
| B4 | Situação do card (serviço, handoff, Timeline, Home) | back | B5 | 3 | ✅ | Claude (sessão principal) | 2026-11-04 | 2026-11-04 | 13e25b6, 7265b61, ee7f82f |
| B5 | Revisões dos PRs (aprovações) | back | — | 3 | ✅ | Claude (sessão principal) | 2026-11-04 | 2026-11-04 | 13e25b6 |
| B6 | Dev Test in QA automático (+ Ready for QA pela tela, endpoint existente) | back | — | 3 | ✅ | Claude (sessão principal) | 2026-11-04 | 2026-11-04 | 13e25b6, ee7f82f |
| B7 | Publicar situação no DevOps (manual) | back | B4, D1 | 3 | ✅ | Claude (sessão principal) | 2026-11-04 | 2026-11-04 | 13e25b6 |
| F4 | Cartão "Situação atual" + fato + chip + handover | front | B4, F1, F2 | 3 | ✅ | Claude (subagente) | 2026-11-04 | 2026-11-04 | front d6554ea, 11423d6 |
| F5 | Concluir "Validar em QA" → Ready for QA | front | B6 | 3 | ✅ | Claude (subagente) | 2026-11-04 | 2026-11-04 | front d6554ea, 11423d6 |
| S4 | Skill: handoff, QA, handover com situação | skills | B4, B6 | 3 | ✅ | Claude (sessão principal) | 2026-11-04 | 2026-11-04 | af5d13a |
| Q1 | Integração e e2e local (`.t0075/`) | ambos | todas | 4 | ✅ | Claude | 2026-11-04 | 2026-11-04 | 60abf43 |
| Q2 | Produção | ambos | Q1 | 4 | 🟡 | Claude | 2026-11-04 | | |

**Entrega**: um PR por repositório com a feature inteira (o usuário pediu para abrir e mesclar ao final — back e
front juntos, 2026-11-04). PR-A/PR-B do plano viraram um só.

## Decisões
- **D1** — publicar a situação na discussion do DevOps: **manual** (botão com confirmação; o cliente lê a discussion);
  republicar atualiza o mesmo comentário.
- **D2** — fotografia: foco ± `CodeRefContextLines` (30), máx. 600 linhas/64 KB por ref, 30 notas, 80 refs por plano.
- **D3** — Ready for QA na conclusão de *Validar em QA* usa o endpoint existente `POST Azure/card/{id}/actions/ready-for-qa`
  (sem endpoint novo no plano).
- **D4** — "Abrir no GitHub" fora do popover: a API só conhece o nome do repositório (sem o dono).

## Q1 — back (Postgres 18 isolado, porta 55475; API 5083; auth falso 8099; tudo em `.t0075/`)
- Migrações de todos os contextos aplicadas do zero (`--migrate`); seed `SeedSkillsSettings0075` com as 4 chaves.
- Fluxo real pelo `prmake-plan.sh`: `start` → análise com as citações do item 1.6 → `advance montar-analise` **recusado**
  listando as 5 citações e os 2 `ref:` sem fotografia → 3 × `coderef` (change com `--after` + notas; read; suspect) →
  `advance` aceito. `GET card/{c}/code-refs` (cabeças) e `/{ref}` (foco, notas do jsonb, diff) ok; 404 sem ref.
- Mocks Antes × Depois a partir do markup real do SA3 + CSS limitless da cópia do legado (`mock-shot.sh`), `upload`
  e `GET artifacts/by-name/...` idêntico ao arquivo.
- Correção: PRs abertos → 🔀 aguardando aprovação e merge; chamado com `01_*.sql`/`chamado-*.md` → 🎫 (PR em "também");
  chamado resolvido + PRs mesclados (dev e `qa`) → o PRMake tentou mover para Dev Test in QA sozinho (sem o plugin
  AI Configurations no banco isolado: erro gravado, aviso único no plano, situação 🧪 com o aviso e o roteiro do
  `impacto-na-base`); `handoff` pelo script e pelo MCP; Timeline com uma entrada por mudança de situação.
- MCP (`/mcp` JSON-RPC): 31 ferramentas; `prmake_code_ref`, `prmake_handoff`, `prmake_plan` (codeRefs) e
  `prmake_attachment` achando arquivo do plano pelo nome.
- Testes: executionplans 38/38, prform 114/114.

## Q1 — navegador (front servido em localhost:4200 contra a API local; prints em `.t0075/shots/`)
Timeline com callout e links de código coloridos por tipo (alterar/leitura/suspeito), Antes × Depois lado a lado com
legenda; popover da `sa3_busca_a3.asp:239-289` (Proposta com notas ①②③, Código atual com foco), suspeito sem proposta,
zoom da imagem; cartão *Situação atual* (QA) no topo do plano com handoff e link `ref:` abrindo o popover; fato no topo
do card; celular 390 px (folha inferior, sem rolagem horizontal); chip da Home e diálogo do F5 (resposta simulada —
o usuário de teste não tem cards na Home; ready-for-qa interceptado). Ajustes do e2e: front 11423d6, back 60abf43.
Specs do front: 61/61. Pendente para produção: tempo real `situation` ponta a ponta, Publicar no DevOps e Ready for QA
com o DevOps real.

## Notas de handoff
- Worktrees: back `../prform.api-0075`, front `../solvace.prform.web/prform-app-0075` (`node_modules` → `prform-app-0019`).
- Commitar só os arquivos da fase (`git commit -- <arquivos>`).
- Specs do front: build de teste completo quebra por specs antigos (`async` do Angular) — rodar só os da fase com um
  tsconfig temporário (ver F1/F2).

## Log
- 2026-11-04 — spec, plano e status; branches `feature/0075` (back 94a08a4, front b08550d).
- 2026-11-04 — B1–B7, S1–S4, F1–F3 concluídas; Q1 do back ok.
- 2026-11-04 — F4/F5 e e2e do navegador ok; PRs abertos e mesclados (back e front juntos) por pedido do usuário.
