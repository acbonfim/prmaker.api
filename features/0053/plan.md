# Plano — Feature 0053 (banco da DEMO e glossário na engenharia reversa)

Branch `feature/0053` em `prform.api-0053` (API, skills) e, no início da execução, `prform-app-0053` (front).
**Não iniciar antes do fim do piloto SA3** (ver spec). Perguntas respondidas em 2026-10-04 (seção "Respostas" da spec).

| Fase | O quê | Depende |
|---|---|---|
| B1 | `ReverseItemKinds`: `SQL`, `TRG`; parser/lint aceitam `**Onde:** banco DEMO …` e `**Banco:**` como evidência; linha `**Sinônimos:**` nos `GLO` (índice guarda); seção obrigatória "Banco de dados…" no modelo de arquitetura e "Glossário" no funcional; lint exige evidência em `SQL`/`TRG`/`JOB`; testes | — |
| B2 | Configuração `ReverseEngineeringReferenceDatabase` (migração idempotente no "Skills Configurations") e devolução no pacote da sessão; `ReverseEngineeringGlossaryExclusions` (palavras genéricas de interface fora da cobertura de termos; cobertura do glossário = 100% da lista) | — |
| B3 | Busca com sinônimos: ao publicar/indexar, sinônimos dos `GLO` do módulo viram expansão de consulta (`ReverseSearch`, `prmake_base_search`, tela Índice) e entram na busca da Base Solvace/Pergunte; sugestões de apelidos/palavras-chave a partir do glossário (pendentes, aceitar/ignorar) | B1 |
| S1 | Skill: `re_tool.py catalogo` / `re.sh banco <módulo>` — pelo `sql-query.sh` (read-only) na DEMO — global e os 3 locais (global primeiro; objeto igual nos locais = um item; divergente = item + GAP): tabelas do módulo (prefixo + citadas), dependências, definições de views/procedures/functions/triggers, colunas/PK/FK/checks, jobs do `msdb`, objetos de outros módulos que usam as tabelas; grava `~/.prmake/reverse/<m>/banco/` + `catalogo.json`; mascara segredo; etapa "Banco de dados (DEMO)" no andamento ao vivo; sem acesso → etapa bloqueada com o que fazer | B2 |
| S2 | Inventário/cobertura com as categorias do banco e os termos (traduções `GetLanguageByName` + multilíngua da DEMO, `i18n/*.json`, menus `TB_WCM_MENU`, siglas); modo melhorar compara catálogo novo × anterior (lista do que mudou no banco); SKILL.md/`references/escrever.md` (como documentar objeto do banco e regra só no banco; glossário com sinônimos) | S1, B1 |
| F1 | Tela: categoria "Banco" na cobertura da revisão; aba **Glossário** (todos os módulos, sinônimos, busca); sugestões de apelidos/palavras-chave do glossário no módulo | B3 |
| F2 | Correção do sumário (spec §5): âncoras a cada renderização na Engenharia reversa (e nova tentativa no clique, rolar de novo após o mermaid); Base Solvace casa título por âncora/normalização dos dois lados (títulos com `_`, `*`, código, link); margem do topo e destaque | — |
| B4 | Sugestões fechadas na publicação (spec §6): decisões por sugestão no envio (`aplicada` + IDs / `recusada` + motivo) guardadas na revisão; publicar resolve (`applied`/`dismissed` com nota e revisão; Timeline do card de origem); descartar não resolve | B1 |
| S3 | `melhorar` com lista de trabalho (spec §6): diff dos commits gravados × atuais por fonte + catálogo do banco publicado × atual → itens da ER afetados (pela evidência); etapa no andamento ao vivo; `re.sh submit --sugestoes`; SKILL.md e painel "Como gerar" com o módulo explícito e as chaves de `<doc>` | S1, B4 |
| T1 | Teste local (harness da 0052) + leitura real da DEMO para o `legado-rca`: catálogo do SA3 (views `VW_SA3_*`, procedures, triggers, jobs), cobertura, item `RN` com evidência de banco, busca "RCA" achando itens de "A3" | todas |

Ondas: **1** — B1, B2, F2, B4. **2** — B3, S1. **3** — S2, S3, F1. **4** — T1.
