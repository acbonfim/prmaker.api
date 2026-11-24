# Plano 0101 — Padrões de banco e de aplicação

Branches `feature/0101` em `prform.api-0101` (back + skills) e `prform-app-0101` (front), a partir da origin/master
(0098 já mesclada). Contratos abaixo são a fonte da verdade entre as frentes.

## Decisões

- **Padrão ≠ regra.** Fica no módulo `Solvace.Governance` (schema `governance`), em tabela própria, com a mesma escala de
  efeitos da 0098 (`monitor` · `warn` · `block`) e o mesmo modo global (off/monitor/enforce). Achados entram na trilha da
  0098 (categoria `standard`, `rules` = IDs dos padrões).
- **Tudo começa em `monitor`.** Itens da wiki marcados **(NEW)** entram como `proposed` (aguardam o DBA); os demais como
  `active`. Nada barra até o admin subir o efeito item a item, olhando na Auditoria quanto cada um dispararia.
- **A fonte continua na origem.** A wiki é importada pelo PRMake com a integração DevOps de quem pede (como as outras
  leituras do card) — nada de PAT local na skill. Reimportar gera rascunho com a diferença por item (novo, mudou, saiu);
  o admin aprova. A cópia lida em 2026-11-24 fica em `features/0101/wiki/` para teste e para a carga inicial.
- **Estrutura de aplicação vem da engenharia reversa.** Um gerador sem IA lê os documentos publicados (arquitetura,
  especificação de arquitetura, índice técnico) e propõe padrões por módulo (camadas/pastas, onde ficam endpoints,
  repositórios, migrações, nomes) que **referenciam** os itens da ER (`ref: módulo#ITEM`), sem copiar o texto.
- **Conferência automática é opcional por item** e de três tipos: `regex` (sobre SQL/DDL/código gravado pela skill ou
  editado no Claude Code), `path` (glob de onde o arquivo deve/não deve ficar) e `sql` (consulta de validação da wiki,
  rodada sob demanda pela skill, somente leitura, no banco de um ambiente — o resultado volta como achados).
- **Escopo da wiki respeitado**: regras de nome valem para objetos novos e módulos em migração; `dbo`/legado fica fora
  (G8, S5, T3, P4, V5) — o conferidor ignora `dbo.` e `__EFMigrationsHistory`.

## Contratos (API) — `api/v1/Governance/standards`

```ts
type StandardArea = 'database' | 'application';
type StandardStatus = 'proposed' | 'active' | 'deprecated';
interface Standard {
  id: string; key: string;              // 'db-fk7', 'app-revamp-actionplan-layers'
  code: string;                         // 'FK7' (ID da origem)
  area: StandardArea; topic: string;    // table|column|schema|pk|fk|index|unique-index|default|check|view|procedure|query|general | layers|folders|naming|api|data-access|migrations
  title: string; textPt: string; textEn: string | null;
  examples: { good: string[]; bad: string[] };
  appliesTo: { platforms: string[]; repositories: string[]; modules: string[]; objects: ('new'|'migrated'|'legacy')[] };
  source: { kind: 'wiki'|'reverse-engineering'|'manual'; ref: string; version: string | null; url: string | null };
  status: StandardStatus; effect: 'monitor'|'warn'|'block';
  check: { kind: 'none'|'regex'|'path'|'sql'; target: 'sql'|'migration'|'code'|'path'|null; pattern: string|null; mustMatch: boolean; sql: string|null } ;
  updatedAt: string | null; updatedBy: string | null;
}
interface StandardFinding { key: string; code: string; title: string; effect: string; line: number | null; excerpt: string; hint: string }
```

| Método | Rota | Quem | O quê |
|---|---|---|---|
| GET | `standards` | autenticado | `?area&status&topic&repository&module&search` → `Standard[]` |
| GET | `standards/for-card/{card}` | autenticado | markdown só com os padrões que valem para o card (repositórios/módulos do card) — o `prmake_rules` acrescenta e o MCP `prmake_standards` devolve |
| POST | `standards/check` | autenticado | `{ text, target, path?, repository?, card? }` → `{ decision, findings: StandardFinding[] }` (aplica efeito/modo; grava na trilha quando acha) |
| PUT | `standards/{id}` | admin | altera (efeito, estado, aplica-se, conferência, textos) |
| POST | `standards` / DELETE `standards/{id}` | admin | padrão manual / apaga (só manual) |
| POST | `standards/import/wiki` | admin | `{ url }` → lê pela integração DevOps de quem pede (página + subpáginas), devolve rascunho `{ id, added[], changed[], removed[], version }` |
| POST | `standards/import/reverse-engineering` | admin | `{ modules?: string[] }` → rascunho com os padrões de aplicação propostos a partir da ER publicada |
| POST | `standards/import/{draftId}/apply` | admin | `{ accept: string[] (keys) }` → grava; evento `governance` com o antes/depois |

## Fases

| Fase | O quê | Onde | Depende |
|---|---|---|---|
| B1 | Entidade `GovernanceStandard` + `StandardDraft`, migração, repositório, aplicação (CRUD, filtros, aplica-se ao card) | `Solvace.Governance` | — |
| B2 | Parser da wiki (markdown das páginas → itens: tabelas `| ID | EN | PT | Exemplo |`, (NEW)/(CHANGED), padrão de nome por objeto, scripts de validação por página) — puro e testado com `features/0101/wiki/` | domínio | B1 |
| B3 | Conferidor (`StandardChecker`): regex/padrões de nome sobre DDL (CREATE TABLE/INDEX/VIEW/PROCEDURE, ALTER … ADD CONSTRAINT, EF `HasName`/`HasDatabaseName`, `OnDelete(Cascade)`), `SELECT *`, `MONEY`, `sp_`, `TB_`, nomes sem schema, ignora `dbo`; `path` para estrutura | domínio | B1 |
| B4 | Importação: wiki pela integração DevOps (`IAzureService`, novo método de leitura de página de wiki com subpáginas) e ER (documentos publicados); rascunho com diferença; aplicar | host + Azure | B2 |
| B5 | Pontos de aplicação: `standards/check`, trava ao gravar script/arquivo do plano/item (`prmake_file`, `prmake_work_item_file`, PR do item) com o efeito do padrão; `for-card` + `prmake_rules` + MCP `prmake_standards` | host | B3 |
| B6 | Carga inicial: importar a wiki (página 236) e a ER dos módulos publicados em produção — **precisa de ok do usuário** (grava em produção) | produção | B4, F2 |
| S1 | Skills (analisar-bug/us, executar-item, gerar-prmake): ler os padrões do card antes de escrever SQL/migração/código; conferir com `standards/check` antes de gravar; script de validação da wiki sob demanda (somente leitura) | `skills/` | B5 |
| S2 | Hook de governança (0098): `PostToolUse` de Write/Edit em `.sql`/migrações → `standards/check` (aviso volta ao Claude); `PreToolUse` com efeito block | `skills/_tooling/prmake-governance.py` | B5 |
| F1 | Aba "Padrões" na Governança: lista por área/assunto/estado, filtros, detalhe EN/PT com exemplos, editar efeito/estado/aplica-se/conferência, testar conferência num texto | front | contrato |
| F2 | Importar wiki / ER: rascunho com diferença por item (novo/mudou/saiu), aceitar em lote, histórico | front | F1 |
| F3 | Aderência: achados por padrão/repositório/card (a partir da trilha), atalho da Auditoria | front | F1 |
| Q1 | Testes: parser com a cópia da wiki (103 itens, 28 NEW), conferidor (exemplos certo/errado da própria wiki viram casos de teste), escopo `dbo`, efeitos/modo, for-card; skills; specs do front | | B*, S*, F* |
| Q2 | e2e local + A/B da Bancada (como na 0098) | | Q1 |
