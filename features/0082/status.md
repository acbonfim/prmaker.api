# Status — Feature 0082

| Fase | Descrição | Status | Responsável | Commits | Notas |
|---|---|---|---|---|---|
| B1 | Correção sobre análise vazia | concluída | Claude | back | análise vazia = nenhuma etapa concluída e nenhum arquivo; parte da análise mais recente com etapa concluída, criada antes; a vazia é cancelada com "Versão sem análise: a correção partiu da análise vN" |
| B2 | Migração de dados | concluída | Claude | back | testada no Postgres do harness 0080 em transação desfeita: card com análise feita → correção reapontada e vazia cancelada; card sem análise feita → intocado |
| F1 | Botão da versão + barra da versão | concluída | Claude | front | `.vbar`: botão "Análise v2 · mais recente" abre `app-plan-history`; a jornada virou as etapas do plano em tela (clique → `goToStep`) |
| F2 | "Versão anterior" por fase | concluída | Claude | front | `isOlderVersion` (mesma fase, ignora `isEmptyVersion`); o texto aponta a mais nova |
| F3 | Análise / plano completos | concluída | Claude | front | `PlanDocumentService.build`: resumo, documentos `.md`, etapas (descrição, motivo, checkpoint, arquivos), scripts/arquivos de texto, código (fotografias: hoje × como fica × implementado; na correção inclui as da análise de origem, sem as só-leitura) e links; análise vazia mostra a última feita |
| F4 | Tour v2 | concluída | Claude | front | passos `version`, `journey` (novo texto) e `document` com `since: 2` |
| Q1 | Build e testes | concluída (local) | Claude | | `dotnet build` + 63 testes; `ng build`; 4 specs Karma com tsconfig temporário (specs antigos não compilam) |

## Log
- 2026-11-04 — pedido do usuário no 77900; B1–F4 e Q1 local.

## Pendências
- Validar na tela real do 77900 depois do deploy: Correção v1 ligada à análise v1; Análise v2 cancelada ("Versão sem
  análise"); "Ver o plano completo" com o código da legenda (QMA:107, QPL:163).
- A skill ainda abre a análise nova no `start` (o servidor limpa). Se incomodar, ensinar o `start` a não abrir análise
  quando o pedido é proposta de correção.
