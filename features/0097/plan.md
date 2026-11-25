# Plano 0097 — ER aberta, ancorada na master, índice técnico, histórico e geração barata

Branches `feature/0097` em `prform.api-0097` (back + skills) e `prform-app-0097` (front), a partir da origin/master.
Três frentes em paralelo (B, S, F); Q no fim. Contratos abaixo são a fonte da verdade entre as frentes.

## Decisões

- **Leitura aberta**: todo usuário autenticado lê a ER (tela, itens, busca, MCP, `for-card`, Pergunte, armadilhas).
  **Gerar** (sessões, rascunhos, andamento, envio, anexos, infra, índice técnico, marcar itens desatualizados) continua
  `admin`; **aprovar/publicar/configurar o módulo** continua com `ReverseEngineeringApproverRoles`.
- **Documentos exigidos** passam a ser `funcional, arquitetura, spec-arquitetura`. `design` e `visao` ficam
  **descontinuados**: o publicado continua legível e na busca, mas nenhuma sessão nova (`new|improve|redo`) é aceita.
  `pratica` deixa de ser exigida: é **sob demanda** — sugerida quando o módulo acumula perguntas sem resposta no Pergunte.
- **Índice técnico** (`tecnico`, seção `re-tecnico`) é gerado sem IA e publicado direto (sem revisão humana): fora do
  "completo", fora do Pergunte (público leigo), dentro da busca das análises com peso menor que RN/UC.
- **Reancoragem** de documento publicado = revisão `mode=reanchor` (só linhas `Onde:` mudam) que o aprovador publica
  como hoje; os itens cujo trecho citado mudou ficam marcados **desatualizados** no índice até a próxima publicação.

## Contratos (API)

- `PUT api/v1/ReverseEngineering/modules/{key}/technical-index` (admin) — body
  `{ content: string, commits: [{ repository, commit, branch }], generatedAt, counts: {KIND: n} }` → lint estrutural,
  grava a seção `re-tecnico` (fonte Skill), troca o índice do documento, registra uma revisão `published` com
  `mode=generated`. Resposta: `{ items, version, warnings[] }`.
- `POST api/v1/ReverseEngineering/modules/{key}/stale` (admin) — body
  `{ commit, items: [{ docType, itemId, reason }], replace: bool }` → marca `StaleSince/StaleReason/StaleCommit` nas
  entradas do índice (`replace=true` limpa as do módulo antes). Publicar um documento limpa as marcas dele.
- `ReverseModuleResponse`/`ReverseModuleSummaryResponse` ganham `openQuestions` (perguntas `open` do Pergunte para o
  módulo), `practicalSuggested` (sem prática publicada ou desatualizada **e** `openQuestions ≥
  ReverseEngineeringPracticalMinQuestions`, padrão 3), `staleItems` (contagem) e `technicalIndex`
  (`{ version, generatedAt, commits[], counts }` ou null). `ReverseDocTypeResponse` ganha `discontinued` e `generated`.
- Itens desatualizados: `ReverseIndexHit`/`ReverseItemResponse` ganham `stale` (`{ since, reason, commit }` ou null). O
  texto da busca do MCP e do `for-card` acrescenta ` (código mudou desde a geração — confirme no código)`.
- `ReverseCardContext` ganha `Query` (título + repro normalizados, ≤ 1000) e `ReadRefs` (refs realmente lidos por
  `prmake_base_get`/item do card — diferente de `Consulted`, que inclui o top-5 da busca).

## Tipos do índice técnico (`tecnico`)

IDs com prefixos que não colidem com os de hoje (conferir `ReverseItemKinds`): `END` endpoint/ação HTTP/página ajax ·
`ROT` rota/tela do front (rota → componente) e página `.asp` · `TAB` tabela · `SQL` procedure/view/function/trigger/job
do banco · `CHV` chave de configuração · `MSG` mensagem literal de validação/erro. Item:
`### END-0001 — GET /api/v1/kaizen/{id}` + `- **Onde:** \`<repo>/<caminho a partir da raiz>:linha\`` +
`- **Tabelas:**`/`- **Tags:**` quando houver + `- **Commit:** <repo>@<sha7>`. IDs estáveis: a skill reaproveita o ID da
mesma chave (tipo + símbolo) do publicado; novos pegam o próximo número.

## GAP do gerador

Lista única de padrões (domínio C# `ReverseGeneratorGaps` e `re_gaps.py` na skill, com teste de paridade): GAP cujo
título/corpo fala do processo de geração e não do sistema — "fora do pacote", "não está no pacote", "pacote não
traz/inclui", "sem figma", "figma não", "não lido nesta área", "fora desta área", "orçamento da área", "subagente",
"inventário não", "retrato não", "não foi possível ler" (sem citar código). Não entram no índice (publicação ignora; a
migração apaga os existentes); o lint avisa; o `juntar` remove; o cartão proíbe.

## Fases

| Fase | O quê | Onde |
|---|---|---|
| B1 | Leitura aberta: `HttpReverseAccess.CanRead` = autenticado; controller `[Authorize]` na classe e `[Authorize(Roles="admin")]` nas ações de geração; trava da investigação volta a valer para todos; ajustar testes 0074 | `HttpReverseAccess`, `ReverseEngineeringController`, MCP, `ArchitectureApplication` |
| B2 | Exigidos = funcional, arquitetura, spec-arquitetura (`DefaultRequired` + migração da config); `Discontinued` em design/visão (sessão recusada com mensagem clara); prática sob demanda (`openQuestions`, `practicalSuggested`, config `ReverseEngineeringPracticalMinQuestions` no catálogo) | `ReverseDocTypes`, `ReverseEngineeringApplication`, `PluginSettingsCatalog`, migração em `solvace.prform.infra` |
| B3 | GAP do gerador fora do índice (`ReverseGeneratorGaps`), aviso no lint, migração que apaga os existentes | domínio `Reverse/`, `ReplaceIndexAsync`, `ReverseLint`, migração knowledge |
| B4 | Índice técnico: tipo `tecnico` (`Generated`), endpoint `technical-index`, peso na busca (0,85), fora do Pergunte e do "completo", resumo no módulo | `ReverseDocTypes`, `ReverseItemKinds`, `ReverseSearch`, `ReverseAnswerContext`, controller |
| B5 | Histórico do card: `Query`/`ReadRefs`, `ReverseCardHistory` (similaridade por termos com IDF), uso no `ForCardAsync`/`RankForCardAsync` (campo genérico ou sem casamento → histórico; com casamento → soma no placar), linha "Pelo histórico (cards …)", `prmake_base_get` grava lidos; backfill do `Query` se houver título do card no banco | `ReverseCardContext`, `ReverseEngineeringApplication`, `PrmakeBaseMcpTools`, migração |
| B6 | Itens desatualizados: colunas `StaleSince/StaleReason/StaleCommit` no índice, endpoint `stale`, limpeza ao publicar, aviso na busca/`for-card`/MCP, `staleItems` no módulo; modo `reanchor` aceito | entidade do índice, repositório, controller, MCP |
| S1 | Master obrigatória: `--local` exige `--motivo`; `check` dá ERRO se uma fonte não está na origin/master (sem `--local --motivo`); cabeçalho com os SHAs da origin/master; `Onde:` com caminho completo a partir da raiz do repo (pacote e check), `check` acusa citação ambígua/sem prefixo e arquivo local do gerador | `re.sh`, `re_pacote.py`, `re_tool.py` |
| S2 | `re.sh reancorar <m> [doc\|todos] [--enviar]` (sem IA): mapeia linhas pelo `git diff` do commit gravado até a origin/master, completa caminho único, lista itens cujo trecho mudou; `--enviar` = revisão `reanchor` + `POST stale` | `re_ancora.py`, `re.sh` |
| S3 | `re.sh indice <m>\|--todos [--enviar]`: inventário (master) → `re_indice.py` → `PUT technical-index`; IDs estáveis | `re_indice.py`, `re.sh` |
| S4 | Documentos: `tudo` = funcional+arquitetura (passada única) → spec-arquitetura; design/visão recusados; prática sob demanda centrada nas perguntas sem resposta; GAP do gerador proibido no cartão e removido no `juntar` (`re_gaps.py`) | `SKILL.md`, `references/`, `re_pacote.py` |
| S5 | Custo/tempo: **passada única** (`re.sh lote`: um conjunto de áreas/pacotes para funcional+arquitetura, cada subagente lê o código uma vez e grava as duas partes; especiais glossário/banco/módulo na mesma leva); `re.sh fechar` (juntar+check+save dos dois com resumo compacto); **consolidação por subagente** (`pacote-consolidar.md`); spec-arquitetura logo após o `fechar` (rascunho local); sessão principal em Sonnet (só orquestra scripts); `re_custo.py`/`re.sh custo` medem tokens e tempo por documento a partir dos transcripts | `SKILL.md`, `re.sh`, `re_pacote.py`, `references/subagente.md`, `re_custo.py` |
| S6 | `analisar-bug`/`analisar-us`: linha do histórico no contexto, item "(código mudou…)" → confirmar no código | `skills/analisar-bug`, `skills/analisar-us` |
| F1 | Rota da ER sem `AdminGuard`; menu para todos; ações de geração/aprovação pelos flags da API; armadilhas na Base para todos | `authenticated.routes.ts`, side-menu, `reverse-engineering`, `architecture` |
| F2 | Documentos: 3 exigidos; design/visão "descontinuado" (só leitura se publicado; some se não); prática "sob demanda" com perguntas sem resposta e o comando; selo "Pergunte sem resposta: N" na lista | `reverse-engineering.component.*` |
| F3 | Aba **Índice técnico** (conteúdo, commit, data, contagem por tipo, comando para gerar) | `reverse-engineering`, serviço |
| F4 | Itens desatualizados: contagem no cabeçalho do módulo com lista; selo no popover do item (`re-item-popover`) | `reverse-engineering`, `code-ref/re-item-popover` |
| Q1 | Testes: `solvace.knowledge.tests`, `solvace.prform.tests`, `tools/SkillTests` (python), `ng build` | — |
| Q2 | Depois do deploy (com ok do usuário): `re.sh indice --todos --enviar` e `re.sh reancorar <m> todos --enviar` nos 34 módulos | produção |
| Q3 | Piloto medido (com ok do usuário — consome a assinatura): um módulo com `tudo`, `re.sh custo` antes × depois; meta ≤ 49 M de cache lido e ≤ 35 min por módulo, cobertura ≥ 90%, amostra de 20 itens ≥ 89% corretos | máquina do usuário |

## Estimativa da meta (S5)

Por módulo, hoje ~98 M / ~87 min (6 documentos + prática). Depois: funcional+arquitetura numa passada (o código é lido
uma vez: ~35% menos leitura dos subagentes; a sessão principal deixa de reler documentos e vira Sonnet) + spec (~3 M,
paralelo ao fim) e sem design/visão/prática obrigatória → estimado **~35–45 M (−55 a −65%)** e **~25–35 min (−60 a
−70%)**. A Q3 confirma; se não bater, o próximo passo é o pacote enxuto (o índice técnico substitui o "Apoio" repetido).
