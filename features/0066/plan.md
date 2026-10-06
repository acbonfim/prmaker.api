# Feature 0066 — Plano de execução

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branch (nos dois repos): `feature/0066` a partir de `master`. Worktrees: `prform.api-0066` e
> `solvace.prform.web/prform-app-0066` (com `node_modules` ligado ao `prform-app-0019`).

| Repo | Caminho | Sigla |
|---|---|---|
| Backend + skills | `prform.api` | `B*`, `S*` |
| Frontend | `../solvace.prform.web/prform-app` | `F*` |

## 1. O que já existe (e onde mexer)

- **Skill** `skills/engenharia-reversa`: `re.sh` (sessão, inventário, banco, infra, check, save/submit), `re_tool.py`
  (inventário, cobertura, juntar, termos…), `re_banco.py` (catálogo da DEMO por módulo, ao vivo pelo `sql-query.sh`),
  `re_infra.py` (AWS CLI: lê a conta inteira a cada módulo e liga ao módulo). Subagentes por área já existem, mas a
  área é escolhida à mão, o subagente explora o código com `grep`/`sed` e relê as instruções.
- **Integrações**: `ReverseEngineeringApplication.MergeIntegrations` (só ao publicar o `arquitetura`) grava
  `ArchitectureRelation` com `Evidence = re#INT-n`, destino = tokens do **Módulos** sem validar (`ReverseDocParser.Modules`)
  e tipo adivinhado pelo corpo (`RelationKind`). O mapa (`ArchitectureApplication.GetGraphAsync`), o "Usado por"
  (`WithUsedBy`), o card do projeto no espelho (`RenderProjectCard`) e o "Com quem conversa" do front leem
  `project.Relations`.
- **Configuração**: `PluginReverseSettingsProvider` (Skills Configurations) → `ReverseSettings` → `GET /ReverseEngineering/settings`
  (`ReverseSettingsResponse`); chave nova = constante em `SkillsConfigurationKeys` + migração idempotente (padrão da 0058).
- **Front**: `architecture.component` (página do módulo: Guia, Detalhes técnicos, `kb-connections`), `ecosystem-map`
  (Cytoscape), `reverse-engineering.service` (`search`, `items`).

## 2. Desenho

```
skill:  inventario ─► areas (orçamento, faixas de ID) ─► pacote por área (trechos + tabelas + cartão)
                                                           │
                       subagente (modelo da config) ◄──────┘  grava parte-<area>.md a cada ~10 itens
                       faltando/evidencia/check ◄── juntar ◄── partes
        retrato banco|infra (uma vez) ─► banco/infra do módulo usam o retrato (idade máxima)

backend: índice (INT publicados) ─► ReverseIntegrations.Resolve (chaves/apelidos/ext:, mecanismo) ─► relações efetivas
         = relações do extrator (sem re#) + INT resolvidos ─► mapa (edges.items/origin) · Usado por · card do espelho
```

## 3. Fases

| Fase | O que entrega | Repo | Depende de | Onda |
|---|---|---|---|---|
| B1 | `ReverseIntegrations` (domínio): lê `INT` (Módulos/Mecanismo/Contrato/Confirmar), resolve destinos e tipo | back | — | 1 |
| B2 | Relações efetivas no mapa, "Usado por", projeto e espelho; arestas com `items`/`origin`; tipos e externos novos; publicar não grava mais `re#` | back | B1 | 2 |
| B3 | Checagem: avisos de Módulos/Mecanismo; modelos (`modelo.md`) com o INT estruturado | back | B1 | 2 |
| B4 | Config `ReverseEngineeringGeneration` (padrão + migração + settings) | back | — | 1 |
| B5 | Testes do domínio/aplicação | back | B1–B4 | 3 |
| S1 | `re_tool.py areas`, `pacote`, `faltando`, `evidencia`, `cartao` | skill | — | 1 |
| S2 | `re_banco.py retrato` + leitura do retrato; `re_infra.py` com retrato; `re.sh retrato` | skill | — | 1 |
| S3 | `re.sh areas|pacote|faltando|evidencia|config`, `modulos.tsv` no `start`, evidência no `check` | skill | S1, B4 | 2 |
| S4 | `SKILL.md`, `references/subagente.md`, `escrever.md` (INT), `banco.md`/`infra.md` (retrato) | skill | S3 | 3 |
| S5 | Testes dos scripts (`tools/SkillTests`) | skill | S1–S3 | 3 |
| F1 | Mapa: itens/origem na aresta, tipos novos | front | B2 | 3 |
| F2 | "Por pergunta" na página do módulo | front | — | 2 |
| Q1 | Build/testes, e2e local (Postgres + API + front + Chrome headless), piloto de uma área | ambos | todas | 4 |
| Q2 | PRs, merge dos dois juntos (autorizado), acompanhar o deploy | ambos | Q1 | 5 |

## 4. Retrocompatibilidade (regras)

- Formato dos documentos, IDs, índice, espelho (`reverse/*.tsv`) e MCP sem mudança de contrato; campos novos só somam.
- `INT` antigo com **Módulos** em texto livre: o resolvedor tira a chave conhecida do texto (ex.: "legado-actionplan (a
  confirmar…)"); o que não resolve sai do mapa (vira aviso na checagem), não vira nó.
- Relações `re#` gravadas continuam no banco, só deixam de ser lidas (nada de migração de dados).
- Skill: comandos antigos iguais; sem retrato → leitura ao vivo como antes; sem config → padrões; `banco`/`infra`
  com `--ao-vivo` mantêm o caminho antigo.
