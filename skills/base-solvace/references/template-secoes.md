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
| 085 | `operacao` | Configuração e operação | Telas de configuração do módulo (menu, rota, quem vê), papéis e o que liberam, parâmetros por planta, cadastros base, notificações configuráveis, versões nova × legada, diagnóstico "não aparece" — gerada do catálogo (`operacao.py`) e completada pelo código (ver `references/operacao.md`) |
| 090 | `armadilhas` | Armadilhas e bugs conhecidos | O que costuma quebrar, causas raiz já vistas (cards), configurações que confundem, dicas de diagnóstico (queries/logs úteis) |

## Guia para pessoas (público `human`, 0038)
Além das seções técnicas (lidas pelas skills), cada projeto pode ter o **Guia**: as mesmas informações em linguagem
simples para QA, gestores e suporte. Fica **só na tela** do PRMake (e na busca/"Pergunte") — não vai para o espelho
`~/.claude/solvace-kb`, não pesa no índice da análise e não muda o hash. Chave `guia-*` = público `human` automático.

| Ordem | Chave | Título | O que responde |
|---|---|---|---|
| 510 | `guia-o-que-e` | O que é e para que serve | o que faz para o negócio, quem usa, principais telas, um exemplo do dia a dia |
| 520 | `guia-como-funciona` | Como funciona, passo a passo | fluxos do ponto de vista do usuário, estados/status, o que acontece sozinho |
| 530 | `guia-regras` | Regras de negócio | quem pode o quê, prazos, aprovações, validações — com ART-n |
| 545 | `guia-como-configurar` | Como configurar e dar acesso | habilitar na planta, quem configura, onde ficam os cadastros/parâmetros/notificações, como dar acesso, o que conferir quando não aparece |
| 540 | `guia-conexoes` | Com quem conversa | o que dispara cada comunicação, quem fica escutando, na hora × segundo plano (fila, evento, Lambda, rotina), e-mails, o que acontece se falhar |
| 550 | `guia-como-testar` | Como testar | cenários para QA, onde conferir o resultado, pré-requisitos, cuidados |
| 560 | `guia-perguntas` | Perguntas frequentes | só se houver material |
| 570 | `guia-glossario` | Glossário | só no `ecossistema` |

Como escrever: frases curtas, listas e passo a passo; sem nomes de tabelas, classes, endpoints ou caminhos (cite a tela
pelo nome que o usuário vê); termo técnico inevitável explicado numa frase; mermaid pequeno com rótulos em português só
se ajudar; não invente ("a confirmar"). Também: `displayName` (nome que o usuário usa, ex. "Plano de Ação"), `tagline`
(uma frase) e `businessArea` (área que junta legado + revamp + front do mesmo módulo) no `projeto.json`.
Escreva o Guia aqui no Claude Code (subagentes, sem custo de API) e publique com `publicar-pasta` ou `section --audience human`.
`arch.sh guia <chave> <pasta>` usa a IA do PRMake com a chave de API do perfil (créditos pagos; o custo aparece no fim):
só para um projeto pontual — nunca em lote sem o ok do usuário.

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
