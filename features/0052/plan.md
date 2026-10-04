# Plano — Feature 0052 (Engenharia reversa por módulo)

Branch `feature/0052` em `prform.api-0052` (API, executor, skills) e `prform-app-0052` (front, `node_modules` →
`prform-app-0019`).

## Modelo

**Documentos** (tipo → seção publicada na Base Solvace, ordem, itens que define):

| Tipo | Título | Seção | Ordem | Itens (prefixo do ID) |
|---|---|---|---|---|
| `funcional` | Levantamento funcional | `re-funcional` | 110 | FN funcionalidade · UC caso de uso · RN regra de negócio · PRF perfil/permissão · EST estado/ciclo de vida · NTF notificação · CFG configuração/parâmetro · REL relatório/indicador |
| `arquitetura` | Levantamento de arquitetura | `re-arquitetura` | 120 | TEC tecnologia · CMP componente · API endpoint/contrato · DB tabela/entidade · EVT evento/fila · JOB job/rotina · INT integração |
| `uiux` | UI/UX — telas, back × front | `re-uiux` | 125 | TELA tela · FLX fluxo de navegação |
| `visao` | Especificação de visão | `re-visao` | 130 | OBJ objetivo/escopo · PER persona · GLO termo |
| `spec-arquitetura` | Especificação de arquitetura | `re-spec-arquitetura` | 140 | ADR decisão · NFR requisito não funcional · SEQ fluxo de sequência · GAP lacuna/débito/risco |
| `design` | Especificação de design | `re-design` | 150 | UI componente/padrão de UI |

**Item** = cabeçalho `##`–`####` começando pelo ID: `### RN-012 — Etapa só avança com aprovador`. O bloco vai até o
próximo cabeçalho de nível igual ou maior. Linhas opcionais lidas pelo índice: `- **Onde:** arq:linha, …` (evidência),
`- **Tabelas:** …`, `- **Módulos:** revamp-users, …` (entre módulos), `- **Tags:** …`; qualquer `XX-NNN` ou
`<módulo>#XX-NNN` no bloco vira referência. ID único no módulo (entre documentos) e **estável** (melhorar/refazer não
renumera; item que deixou de existir fica como "(removido)"). Referência global: `<módulo>#<ID>`.

**Ciclo**: sessão do Claude cria a revisão `draft` → `submit` (lint estrutural + cobertura do inventário) → `review`
→ revisor `approve` | `changes` (nota volta para a skill no modo melhorar) | `discard` → `publish` (só aprovada): grava
a seção `re-<tipo>` (versão nova, fonte `skill`, nota "revisão #n aprovada por X"), reconstrói o índice do documento,
funde as relações `INT` com `**Módulos:**` no grafo (evidência `re#INT-n`; as do extrator ficam), a revisão publicada
anterior vira `superseded`. Módulo **completo** = todos os tipos de `ReverseEngineeringRequiredDocs` publicados.

**Configuração** ("Skills Configurations", semeada por migração): `ReverseEngineeringApproverRoles` (`admin,gestor`),
`ReverseEngineeringRequiredDocs` (`funcional,arquitetura,visao,spec-arquitetura,design`),
`ReverseEngineeringGateStep` (`investigar-codigo`; vazio desliga), `ReverseEngineeringMinCoverage` (`0.9`),
`ReverseEngineeringTemplates` (`{}` = modelos do código; `{"funcional": "..."}` substitui).

## Fases

| Fase | O quê | Depende |
|---|---|---|
| B1 | Domínio no módulo Knowledge (schema `knowledge`): `ReverseModule` (chave = projeto, fontes repo/pasta/papel, apelidos do campo Module do card), `ReverseRevision`, `ReverseAsset` (bytea ≤ 10 MB), `ReverseIndexEntry`, `ReverseCardContext`; `ReverseDocTypes`/`ReverseItemKinds` com os modelos; `ReverseDocParser` (itens, metadados, referências) e `ReverseLint` (cabeçalhos obrigatórios, IDs válidos/únicos/removidos, item sem `Onde:`, segredo); migração `ReverseEngineering`; testes | — |
| B2 | `ReverseEngineeringApplication` + `ReverseEngineeringController`: módulos (lista com progresso, detalhe, fontes/apelidos), tipos/modelos, sessão (cria/retoma rascunho e devolve o pacote: modelo, publicado, sugestões, nota do revisor, itens dos módulos relacionados, anexos, fontes), revisões (salvar, lint, submit, review, publish, diff por item), anexos (link/arquivo), índice (`index/search`, `items/{m}/{id}`, `impact`), `for-card/{card}` (texto compacto + registra o card), `consulted`; aprovadores pela config | B1 |
| B3 | Base Solvace e MCP: espelho com `reverse/INDEX.md` e `reverse/<módulo>.tsv`, marca `RE n/m` no índice; MCP `prmake_base_search`, `prmake_base_get`, `prmake_base_module`, `prmake_base_impact` (texto enxuto; com `card` registra a consulta); trava da etapa `investigar-codigo` (MCP `prmake_advance` e PATCH da skill) quando o card tem módulo com engenharia completa e não consultou nem citou item/`lacuna:`; migração das chaves no "Skills Configurations" | B2 |
| E1 | Executor 1.0.9: `prmake_base_*` e `re.sh` contam como consulta à base; `send_usage` do script idem | — |
| S1 | Skill nova `engenharia-reversa`: `SKILL.md` (fluxo por documento, `tudo`, `melhorar`, `refazer`, `status`), `scripts/re.sh` (resolve módulo pelo repositório, start, inventario, check, submit, assets, find/get), `scripts/re_tool.py` (inventário determinístico .NET/ASP/Angular/SQL, cobertura do documento contra o inventário, lint local), `references/` (como escrever item, subagentes por área com faixas de ID, UI/UX com Figma) | B2 |
| S2 | `analisar-bug`: `contexto` traz o pacote `for-card` (itens do módulo pelo título/repro) e marca o card; MCP da base na lista de carregamento; regra "engenharia reversa primeiro" (código só para confirmar o `Onde:` citado; lacuna → sugestão na seção `re-*`); citar IDs na análise/advance. `base-solvace`: `kb.sh re find|get`, consulta pelo MCP, sugestão em `re-*` | B3, S1 |
| F1 | Front: página **Engenharia reversa** (`auth/reverse-engineering`, menu): módulos por área com progresso dos 6 documentos e selo "em revisão"; módulo com abas por documento (publicado com sumário de itens, revisão com diff por item/lint/cobertura/aprovar/pedir ajustes/descartar/publicar/editar, histórico, sugestões), UI/UX com anexos (Figma, protótipo, arquivos), fontes/apelidos, comando para rodar no Claude; visões **Revisões pendentes**, **Índice** (busca com filtros por tipo/módulo/documento e impacto) e **Integrações** (INT entre módulos) | B2 |
| F2 | Base Solvace: faixa "Engenharia reversa n/m" na ficha do projeto com link; itens `re-*` aparecem como seções | F1 |
| P1 | Andamento ao vivo (pedido no meio da feature): etapas/áreas/atividade/registro na revisão, `POST revisions/{id}/progress`, tempo real `reverse:<módulo>`, `re.sh etapa|atividade|log` e relato automático; painel na tela | B2, S1, F1 |
| T1 | Teste local (Postgres isolado + API local): sessão → submit → revisão → publicar → seção/índice/espelho/MCP; `for-card`; trava; tela no Chrome headless; `re_tool.py inventario/coverage` num módulo real | todas |

Ondas: **1** — B1, E1. **2** — B2. **3** — B3, S1, F1. **4** — S2, F2. **5** — T1.

Notas:
- Escrita do rascunho: qualquer usuário logado (é quem roda a skill); aprovar/publicar/editar fontes: papéis da config.
- Sugestões reaproveitam `ArchitectureSuggestion` com seção `re-<tipo>` (a análise já sabe sugerir) — aparecem na
  aba do documento e entram no pacote do modo melhorar.
- A seção publicada pode ter até 600 mil caracteres (documento profundo); o espelho leva o documento inteiro, mas a
  skill lê por item (`get`), não o documento.
