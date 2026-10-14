# Feature 0070 — Performance: endpoints abaixo de 300 ms, carga sob demanda e listas virtualizadas

Origem: queixa de 2026-10-07 (telas da Base Solvace e da engenharia reversa lentas, memória do navegador crescendo) +
análise do código (master `095d3b0` / front `59dc74e`) e dos logs de requests do Cloud Run de 05–07/10.

## Medição (logs do Cloud Run, GET, 05/10 14h → 07/10 14h, sem o `ExecutionQueue/next`)

| Rota | chamadas | p50 | p95 | máx | resposta |
|---|---:|---:|---:|---:|---:|
| `Architecture/projects` | 10 | 12,4 s | 20,7 s | 20,7 s | 0,8–1,1 MB |
| `Architecture/search` | 4 | 12,9 s | 16,1 s | — | 2 KB |
| `ReverseEngineering/modules/{key}` | 91 | 8,5 s | 22,5 s | 32,6 s | 58 KB |
| `Architecture/export` / `export/manifest` | 4 / 8 | 7,7 s / 5,1 s | — | 14,8 s | 8 MB / 2 KB |
| `ReverseEngineering/for-card/{n}` | 3 | 7,5 s | — | 8,4 s | 6 KB |
| `ReverseEngineering/modules/{key}/docs/{doc}` | 77 | 2,2 s | 12,2 s | 24,1 s | **944 KB** |
| `Architecture/projects/{key}/sections/{s}` | 9 | 2,0 s | 12,9 s | — | 2 KB |
| `ReverseEngineering/modules` | **252** | 1,4 s | 15,8 s | 32,0 s | 162 KB |
| `Home/cards` / `Home/cards/by-numbers` | 27 / 119 | 1,4 s / 0,9 s | 8,7 s / 4,7 s | 12,6 s | — |
| `ReverseEngineering/index/search` | 15 | 1,0 s | 11,2 s | — | 549 KB |
| `ReverseEngineering/revisions/{id}` | **384** | 0,8 s | 10,6 s | 25,2 s | 97 KB |
| `ExecutionPlan/card/{n}/current` / `ExecutionQueue/card/{n}` | 146 / 171 | 0,56 s / 0,45 s | 2,3 s / 2,5 s | 9,7 s | — |
| `ReverseEngineering/doc-types` | 22 | 0,48 s | 1,5 s | 2,2 s | 32 KB |
| `ExecutionPlan/pending` | **841** | 0,20 s | 0,92 s | 29,1 s | — |
| piso: `Skills/config`, `ExecutionWorker/me`, `/mcp` (sem x-api-key) | — | 166 / 243 / **76 ms** | — | — | — |

Por módulo (`modules/{key}`, p50): `legado-moc` 14 s, `edv-solvace-apps` 12 s (máx 30 s), `legado-soc` 10,5 s,
`legado-digitalobeya` 8,6 s; módulos pequenos 2–3 s.

- **OOM**: 50+ `Memory limit of 512 MiB exceeded` em 3 dias (picos de 13–20 por hora em 06/10 18–19h e 07/10 03h e
  13h), **inclusive depois do #119** (07/10 14:06). Cada OOM derruba a instância: a requisição seguinte cai num cold
  start, com todos os caches em memória vazios.
- **Cold starts**: 116–138 instâncias novas por dia (scale-to-zero desde a 0068 + OOM). Toda lógica que depende de
  cache estático em memória (`ReverseSearch`, `ReverseRelations`) paga a carga inteira várias vezes por hora.
- **Piso por requisição**: `/mcp` (sem o handler de x-api-key) responde em ~76 ms; as rotas com x-api-key não ficam
  abaixo de ~150–250 ms nem quando não fazem nada (ver B1).

## Diagnóstico (causas no código)

**Infra/transversal**
1. **Uma chamada HTTP ao `cime-auth` por requisição** — `XApiKeyAuthenticationHandler` chama
   `GET {Auth}/user/is-user-active` a cada request (`Cime.BuildingBlocks.Security/XApiKeyAuthenticationHandler.cs:159`).
   O `cime-auth` também é scale-to-zero e consulta o banco: soma ida/volta + cold start do auth a todas as rotas.
2. **Banco longe**: Cloud Run em `us-central1`, PostgreSQL no MonsterASP (Salt Lake City) — cada ida ao banco custa
   dezenas de ms; rotas com 6–10 consultas em série já passam de 300 ms só de rede.
3. **Sem compressão de resposta** — `Program.cs` não registra `ResponseCompression`; documentos de 1 MB e o export de
   8 MB trafegam crus.
4. **Caches só em memória da instância** e instância de 512 MiB que morre por OOM — o mesmo dado é recarregado do
   zero dezenas de vezes por dia.

**Base Solvace / engenharia reversa** (`Solvace.Knowledge`)
5. `GET Architecture/projects/{key}/sections/{s}` (e `versions`) usa `FindAsync` → `GetProjectsAsync`, que carrega
   **todos os projetos com o texto de todas as seções** (documentos da ER de até 5 milhões de caracteres) para devolver
   uma seção (`ArchitectureApplication.cs:138` e `:613`). Mesmo padrão em `ArchitectureApplication.cs:392` (export/índice).
6. `GET ReverseEngineering/modules` e `modules/{key}`: `ReverseSearch.EntriesAsync` carrega o índice inteiro da ER
   (texto de todos os itens de todos os módulos + cópia normalizada para a busca) só para **contar** itens
   (`ReverseEngineeringApplication.cs:81` e `:174`); `GetSuggestionsAsync` traz até 500 sugestões completas para
   contar pendentes; `GetTrapsAsync` traz as armadilhas inteiras para contar; `ReverseRelations.ApplyAsync` resolve as
   integrações de todos os módulos.
7. Marca do índice (`GetIndexStampAsync`): `COUNT(*)` + `MAX(UpdatedAt)` em `ReverseIndexEntries` a cada chamada, sem
   índice em `UpdatedAt`; `GetIndexEntriesByKindAsync("INT")` sem índice em `Kind`.
8. `GET modules/{key}/docs/{doc}`: `GetProjectAsync` carrega **todas as seções do módulo com texto**, faz o parse do
   documento a cada requisição (os itens já estão no `ReverseIndexEntries`) e devolve o documento inteiro (944 KB em
   média), todas as revisões e as sugestões, numa resposta só (`ReverseEngineeringApplication.cs:275`).
9. `GET doc-types` devolve o **modelo completo** de cada tipo (texto da skill, 32 KB) para a tela, que só precisa de
   chave/título/cabeçalhos; `GetSettingsAsync` passa pelo resolvedor de plugin a cada chamada.
10. `GET Architecture/projects`: continua com 0,8–1,1 MB e OOM após o #119 (relações resolvidas + "usado por" +
    seções substituídas de todos os projetos).

**Plano de execução**
11. `GET ExecutionPlan/pending` (841 chamadas em 2 dias — o navegador consulta a cada ~1 min por aba): carrega os planos
    ativos do usuário com **todas as etapas** (`Include(Steps)`) e as perguntas, sem índice em
    `(CreatedByUserId, Status)` (`ExecutionPlanRepository.cs:44`). Planos ativos esquecidos acumulam.
12. `ExecutionPlan/card/{n}/current` e `ExecutionQueue/card/{n}` (~150 cada): plano completo a cada abertura do card.

**Front**
13. **Recarga da lista inteira a cada evento**: na tela da ER, todo evento de andamento (`reverseRevision`) agenda
    `GET modules` em 4 s (`reverse-engineering.component.ts:180`) e, quando o status muda, `GET modules/{key}` + o
    documento — durante uma geração a lista (pesada) é rebaixada a cada 4 s. As 252 chamadas de `modules` vêm daí.
    As "duas chamadas" (xhr + fetch) no DevTools: uma é o service worker repassando a mesma requisição — conferir;
    se houver duas idas ao servidor, é bug.
14. `app-lazy-markdown` desenha o documento sob demanda mas **nunca desmonta** o que já foi desenhado e recebe o texto
    inteiro de uma vez (string de milhões de caracteres + DOM crescente → memória sobe enquanto se rola).
15. A seção da Base Solvace é desenhada inteira com `planMarkdown` (`architecture.component.html:586`), sem pedaços.
16. Timeline do card: todas as entradas de uma vez, cada uma em markdown, sem paginação
    (`card-timeline.component.html:96`); comentários do plano (`plan-notes`) e chats desenham tudo ao expandir;
    andamento (`logs`) guarda até 3.000 linhas e desenha todas.

## Objetivo
- **p95 ≤ 300 ms** com a instância quente para todas as leituras de tela (exceções explícitas: `export`, busca com
  IA, `ask/*`, chamadas a Azure/GitHub — ficam documentadas com a meta delas).
- **Nenhum OOM** com 512 MiB (ou decisão explícita de subir a memória, com custo medido).
- Cold start não pode custar mais que ~1 s extra nas telas (sem carga de índice inteiro na primeira chamada).
- Memória do navegador estável: abrir/rolar documentos da ER, chats e timeline não pode crescer sem limite.
- **Retrocompatível**: skills (`re.sh`, `arch.sh`, analisar-bug), MCP `prmake_base_*`, executor e espelho local
  continuam funcionando — mudanças de contrato entram como parâmetros/rotas novas; os padrões atuais ficam.

## Escopo

### A. Medição (primeiro — guia o resto)
1. Middleware de tempo: cabeçalho `Server-Timing` (total, banco, auth) e log estruturado de requisições acima de
   300 ms com a rota, nº de consultas EF e bytes da resposta (interceptor do EF por requisição).
2. Script de medição dos logs do Cloud Run por rota (p50/p95/máx/bytes, OOM e cold starts por hora) — o mesmo usado
   nesta análise — em `deploy/` para repetir antes/depois.
3. Varredura de **todos** os GET do `prform.api` (não só os listados): tabela rota → consultas, bytes, p95, ação
   (registrar no `plan.md`).

### B. Backend — transversal
1. **Auth sem ida ao `cime-auth` por requisição**: cache em memória do "usuário ativo" por usuário (TTL curto,
   configurável, ex.: 2 min) ou leitura direta no schema `auth` pelo `AuthenticationContext` (read-only) com cache.
   Desativar um usuário passa a valer em até o TTL (documentar).
2. **Compressão** (Brotli/Gzip) para JSON, markdown e texto.
3. **ETag/304** nas leituras de conteúdo grande (seção, documento, revisão): o `ContentHash` da seção já existe — o
   navegador não baixa de novo o que não mudou.
4. Regra de ouro (documentar no CLAUDE.md): leitura de tela usa projeção (`Select`) sem `Content`; contagens com
   `COUNT/GROUP BY` no banco; nada de carregar entidade inteira para contar; no máximo ~3 idas ao banco por rota.
5. **Caches que sobrevivem ao cold start**: o que hoje é calculado do índice inteiro (contagem de itens por
   módulo/documento/tipo, integrações resolvidas) é gravado na publicação (tabela/colunas de resumo) e lido pronto;
   a marca do índice vira uma linha única de versão (incrementada ao publicar) em vez de `COUNT + MAX`.
6. Índices: `ReverseIndexEntries(Kind)`, `ReverseIndexEntries(UpdatedAt)` (se a marca continuar), `ExecutionPlans
   (CreatedByUserId, Status)`; revisar os planos de execução das consultas da varredura A3 (`EXPLAIN ANALYZE` no banco
   local com volume parecido).

### C. Backend — Base Solvace e engenharia reversa
1. **Seção por chave**: `GET Architecture/projects/{key}/sections/{s}` e `versions*` buscam só a seção pedida
   (consulta por projeto+seção). Mesmo ajuste em todo uso de `GetProjectsAsync`/`FindAsync` que não precisa do texto.
2. **Lista de módulos leve**: `GET ReverseEngineering/modules` só com projeções e contagens agregadas no banco (ou o
   resumo gravado em B5); nada do índice em memória. Novo `GET modules/{key}/summary` (a linha da lista de um módulo)
   para o front atualizar só o módulo que mudou.
3. **Página do módulo em partes**: `GET modules/{key}` devolve só a cabeça (fontes, documentos com estado, contagens).
   O que é secundário vira carga sob demanda ao abrir o bloco: relações/"usado por", ativos, termos sugeridos,
   armadilhas, seções substituídas, itens por tipo (rotas novas `modules/{key}/relations`, `/assets`, `/terms`…; o
   `modules/{key}` atual continua aceitando `?full=true` para quem precisar do formato antigo).
4. **Documento em partes**:
   - `GET modules/{key}/docs/{doc}?content=false` — cabeça + sumário (itens com ID/título/nível e a âncora/posição de
     cada seção do documento), sem o texto. Itens lidos do `ReverseIndexEntries`, sem parse.
   - `GET modules/{key}/docs/{doc}/parts?from=&to=` (ou por seção `## `) — o texto de um pedaço do documento.
   - `GET modules/{key}/docs/{doc}/items/{id}` — o texto de um item (já existe algo equivalente no índice; reaproveitar).
   - Revisões e sugestões em rotas próprias (paginadas), não no documento.
   - Padrão atual (`content` presente) continua para a skill e o MCP.
5. `GET doc-types?template=false` (a tela) sem o modelo; a skill continua recebendo o modelo. Settings/doc-types com
   cache de instância (o plugin muda raramente) + ETag.
6. `GET Architecture/projects`: lista só com cabeça de projeto (sem relações resolvidas nem seções substituídas); o
   detalhe vem em `projects/{key}`. Investigar e eliminar o OOM restante (heap dump/contagem de alocação na rota).
7. `Architecture/search`, `index/search`, `for-card`: busca sem carregar o índice inteiro por instância — avaliar
   busca no Postgres (`tsvector` + `unaccent`, ou `pg_trgm`) nos campos já normalizados; resultado paginado e sem o
   corpo do item (o corpo vem sob demanda pelo C4).
8. `Architecture/export`: gerar o pacote na publicação (ou em segundo plano) e guardar pronto; o `manifest` lê o hash
   gravado em vez de recalcular.

### D. Backend — plano de execução e home
1. `ExecutionPlan/pending`: consulta projetada (sem `Include(Steps)` completo — só os campos que o cálculo de
   pendência usa), índice B6, e encerramento/limpeza de planos ativos esquecidos. Avaliar trocar o polling de 1 min
   pelo evento do relay (já existe a infraestrutura de tempo real) com polling longo só de reserva.
2. `ExecutionPlan/card/{n}/current` e `ExecutionQueue/card/{n}`: cabeça do plano + etapas leves; logs, arquivos,
   notas e uso sob demanda (rotas já existem para parte disso).
3. `Home/cards` e `Home/cards/by-numbers`: medir e aplicar as mesmas regras (A3 decide o que fazer).

### E. Front — carga sob demanda e virtualização
1. **Atualização incremental na tela da ER**: o evento de tempo real já traz a cabeça da revisão — aplicar no módulo
   da lista em memória; quando faltar dado, `GET modules/{key}/summary` só daquele módulo. Acabar com a recarga da
   lista inteira a cada 4 s. Confirmar a duplicidade xhr/fetch (service worker) e corrigir se for real.
2. **Documento virtualizado** (ER publicada, revisão e seção da Base Solvace): evoluir o `app-lazy-markdown` para
   *windowing* — pedaços longe da área visível são **desmontados** (ficam só como espaço reservado com a altura medida)
   e o texto de cada pedaço é **buscado sob demanda** (C4) em vez de vir o documento inteiro. Manter: âncoras
   `item-XX-000`, `reveal(id)` (busca o pedaço do item), mermaid por pedaço, sumário lateral (vem da cabeça C4).
   Como a busca do navegador (Ctrl+F) deixa de achar o que não está desenhado, a tela ganha busca dentro do documento
   (pelo sumário/índice).
3. **Seção da Base Solvace** usa o mesmo componente (hoje desenha tudo com `planMarkdown`).
4. **Listas longas com `cdk-virtual-scroll-viewport`** (o `@angular/cdk` já é dependência):
   - andamento (`logs`) do plano — linhas de altura fixa, encaixe direto;
   - resultados do índice/busca da ER, glossário, armadilhas, revisões pendentes, lista de módulos quando passar de
     algumas dezenas;
   - timeline do card, comentários do plano e chats: altura variável → *autosize* (ou windowing próprio como em E2),
     com paginação por cursor no backend (`before=<id>&limit=`) e "carregar anteriores" ao chegar ao topo; markdown
     desenhado só para o que está visível; imagens/anexos com `loading="lazy"` e liberadas (`revokeObjectURL`) ao sair.
5. Medir memória (heap snapshot do Chrome) antes/depois: abrir `edv-solvace-apps` → funcional, rolar até o fim e voltar;
   abrir um card com timeline longa; registrar no `status.md`.

### F. Infra (decisões com custo medido)
1. Memória da `cime-pullrequest`: manter 512 MiB se B/C eliminarem o OOM; senão, avaliar 1 GiB (custo por vCPU-s/GiB-s
   com `cpu_idle`).
2. `min_instances`: só se, depois de B5/C, o cold start ainda pesar nas telas — com custo estimado (a 0068 deixou o
   serviço dentro da cota grátis; não reintroduzir custo 24 h sem decisão).
3. Distância Cloud Run ↔ banco: registrar o RTT medido (A1); se for o gargalo depois das reduções de idas ao banco,
   abrir demanda separada (mover banco ou região).

## Fora de escopo
Reescrever a geração da engenharia reversa (0066); mudar o formato dos documentos publicados; trocar o banco de lugar
(F3 só mede); o `ExecutionQueue/next` (tratado na 0068).

## Critérios de aceite
- Tabela da A2 refeita após o deploy: p95 ≤ 300 ms (instância quente) em todas as rotas de leitura de tela listadas
  acima; `modules/{key}` de `edv-solvace-apps` e `docs/funcional` de `legado-rca` abaixo de 300 ms.
- Zero `Memory limit … exceeded` em 48 h de uso normal.
- `GET ReverseEngineering/modules` não é chamado periodicamente durante uma geração (só o resumo do módulo que mudou).
- Documento da ER de `edv-solvace-apps`: primeira pintura sem baixar o documento inteiro; memória do navegador volta
  ao patamar inicial (± margem) depois de rolar até o fim e voltar ao topo.
- Skills (`re.sh`, `arch.sh`, analisar-bug), MCP `prmake_base_*`, executor e espelho local funcionam sem alteração
  (testes existentes + um ciclo real: sessão → publicação → busca pelo MCP).
