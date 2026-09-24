# Feature 0004 — Markdown automático na linha do tempo

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch: `feature/0004` nos dois repos, a partir de `master` (com 0001–0003). **Só o front muda.**

## 1. Análise
- A timeline guarda o texto exatamente como recebido (tela, skills `prmake-timeline`/`analisar-bug`, importação do Teams) e o `card-timeline` exibe `{{ entry.description }}` com `white-space: pre-wrap` — por isso `**negrito**`, títulos e listas aparecem crus (ver `img.png`).
- Único lugar que exibe a timeline: `components/card-timeline`. O handover usa as entradas só como texto no prompt da IA (não muda).
- Backend e contrato do endpoint **não mudam** (spec 4).

## 2. Solução
- **Detecção automática** (spec 2): função pura `looksLikeMarkdown(texto)` — markdown quando houver algum sinal forte: bloco de código (```), título (`# `), negrito (`**x**`/`__x__`), código inline, link `[x](url)`, citação (`> `), tabela (`|---|`), ou **2+ linhas** de lista (`- `, `* `, `+ `, `1. `). Asterisco/hífen soltos numa frase não contam.
- **Texto normal** (spec 3): segue exatamente como hoje (texto puro, quebras de linha preservadas).
- **Markdown**: convertido com `marked` (GFM, quebra de linha simples vira `<br>`, como já é no resto do app) e exibido via `[innerHTML]` — o sanitizador do Angular remove scripts/atributos perigosos. Links abrem em nova aba.
- **Edição** continua no texto original (o usuário edita o markdown, não o HTML).
- Estilo do markdown compacto, no mesmo tema da timeline (títulos pequenos, listas, código, citações, tabelas).

## 3. Fases

### F1 — Renderização automática de markdown na timeline (front)
- [x] `helpers/markdown-detect.ts` (`looksLikeMarkdown`) + pipe puro `timelineMarkdown` (html ou null).
- [x] `card-timeline`: markdown → `[innerHTML]`; texto normal → como hoje; clique em link abre nova aba.
- [x] Estilos do conteúdo markdown.
- [x] Teste do detector com os textos reais da imagem + casos de borda (sem falso positivo em texto normal).

### Q1 — Validação (usuário)
- [ ] Abrir um card com registros em markdown (ex.: análise da skill `analisar-bug`) e registros normais; conferir os dois.
