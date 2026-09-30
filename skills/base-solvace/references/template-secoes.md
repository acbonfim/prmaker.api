# Template de seções da engenharia reversa (feature 0033)

Cada projeto (repositório ou visão transversal) tem as seções abaixo, nesta ordem e com estas chaves — assim a
análise sabe onde procurar sem abrir tudo. Seção que não se aplica: não crie. Markdown objetivo; diagramas em
```mermaid```. **Nunca** credenciais, senhas, tokens, connection strings ou dados de cliente — só nomes de recursos.

| Ordem | Chave | Título | O que responde (mire em 300–1500 palavras) |
|---|---|---|---|
| 010 | `visao-geral` | Visão geral | O que o projeto faz, para quem, tecnologia e versão, como sobe (host, porta, pipeline), pastas-chave em 1 linha cada |
| 020 | `modulos` | Módulos e fluxos | Módulos/áreas funcionais → pastas/classes de entrada (controllers, telas, jobs); os 3–5 fluxos principais passo a passo com os arquivos por onde passam |
| 030 | `dados` | Dados | Bancos/schemas, tabelas principais (colunas que importam, chaves, status/enums), onde cada entidade é gravada, convenções (soft delete, multi-tenant: global × local por site) |
| 040 | `integracoes` | Integrações | Com quem conversa (outros projetos Solvace, filas, APIs, e-mail, arquivos), por onde (HTTP/SQL/fila), contrato e ponto no código; diagrama mermaid |
| 050 | `infra` | Infra e AWS | Onde roda, serviços AWS usados (nomes), variáveis/segredos por NOME, logs (CloudWatch), pipeline, ambientes (dev/qa/prod/sandbox) |
| 060 | `autenticacao` | Login e permissões | Como autentica (Cognito/pool, JWT, claims), perfis/permissões e onde são checados |
| 070 | `jobs` | Jobs e rotinas | Agendamentos, workers, triggers, o que cada um faz, frequência, onde falha |
| 080 | `regras-de-negocio` | Regras de negócio | Regras que o código aplica (validações, cálculos, estados) + **artigos do Knowledge Center** relacionados (ART-n, título, 1–2 linhas); cite o arquivo onde a regra está |
| 090 | `armadilhas` | Armadilhas e bugs conhecidos | O que costuma quebrar, causas raiz já vistas (cards), configurações que confundem, dicas de diagnóstico (queries/logs úteis) |

## Resumo do índice (`--summary-file`, até 2000 caracteres, vai inteiro para o INDEX.md)
3–6 linhas: responsabilidade, tecnologia, pastas de entrada, tabelas/schemas principais, com quem integra. Escreva
para alguém decidir em 10 segundos se o bug está neste projeto.

## Palavras-chave (`--keywords`)
Nomes que aparecem em cards e telas: módulo (ActionPlan, "Plano de Ação"), telas, entidades, siglas (BOS, CIL, MOC),
tabelas (`TB_WCM_USER`), serviços (Cognito). PT e EN. 10–30 termos.

## Visões transversais (projetos sem repositório)
- `ecossistema` (kind `ecosystem`): mapa de todos os projetos e como se ligam (mermaid), legado × revamp, fluxo de
  uma requisição do usuário até o banco, multi-tenant (ambiente/site), onde fica cada cliente.
- `infra-aws` (`infra`), `login` (`auth`), `terceiros` (`third-party`: e-mail, tradução, armazenamento, BI...).
- `regras-de-negocio` (`business-rules`): o mapa do Knowledge Center por módulo/categoria com os ART-n.
