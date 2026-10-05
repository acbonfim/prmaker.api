---
name: engenharia-reversa
description: Faz a engenharia reversa PROFUNDA de um modulo Solvace (legado ou revamp, back e front) com o Claude aberto no repositorio do modulo — levantamento funcional, levantamento de arquitetura, UI/UX (com Figma/prototipo), especificacao de visao, especificacao de arquitetura e especificacao de design — com TODAS as regras de negocio, casos de uso, integracoes entre modulos e tecnologias, em itens com ID (RN-012, UC-003, API-004...). Cada documento vai para aprovacao no PRMake e, publicado, fica ativo na Base Solvace (as analises consultam por item, sem ir ao codigo). Use quando o usuario pedir "engenharia reversa do modulo", "levantamento funcional", "levantamento de arquitetura", "especificacao de visao/arquitetura/design", "mapear UI/UX", "melhorar/refazer a engenharia reversa", "/engenharia-reversa".
model: opus
---

# engenharia-reversa

Gera os documentos da engenharia reversa de **um modulo** (projeto da Base Solvace: `revamp-<modulo>`, `legado-<modulo>`)
em nivel que **dispensa abrir o codigo** depois: quem le (pessoa ou LLM) acha a regra, o caso de uso, a tela, o endpoint,
a tabela ou a integracao pelo ID e entende sem ir ao fonte. Instalada e atualizada pelo PRMake (fonte:
`skills/engenharia-reversa` no repositorio do PRMake). Token: `PRMAKE_TOKEN` ou `~/.claude/prmake-token.txt`.

```bash
RE=~/.claude/skills/engenharia-reversa/scripts/re.sh     # sessao, inventario, check, submit, find/get/impact, anexos
KB=~/.claude/skills/base-solvace/scripts/kb.sh           # base antiga (secoes 020/030/090...) e espelho
KC=~/.claude/skills/base-solvace/scripts/kc.sh           # Knowledge Center (regras documentadas, ART-n)
```

## Comandos (o que o usuario digita)
`<doc>` = `funcional` · `arquitetura` · `uiux` · `visao` · `spec-arquitetura` · `design` · `pratica` (0054: visão prática,
nao tecnica — so depois dos demais exigidos publicados). Sempre com o **modulo** (chave
da Base Solvace, ex. `legado-rca`) — no legado a pasta do `edv-solvace` serve a dezenas de modulos.

| Pedido | O que fazer |
|---|---|
| `/engenharia-reversa <modulo> <doc>` | um documento do modulo (sem o modulo: `re.sh modulo` pela pasta; ambiguo → pergunte) |
| `/engenharia-reversa <modulo> tudo` | todos, nesta ordem: `arquitetura` → `uiux` → `funcional` → `visao` → `spec-arquitetura` → `design` (cada um enviado separado); a `pratica` so depois que esses forem **publicados** (aprovacao humana) — avise o usuario e pare |
| `/engenharia-reversa <modulo> melhorar <doc>` | parte do publicado + o que mudou no codigo e no banco desde ele (`re.sh trabalho`) + sugestoes pendentes + nota do revisor |
| `/engenharia-reversa <modulo> refazer <doc>` | do zero, mantendo os IDs dos assuntos que continuam existindo |
| `/engenharia-reversa <modulo> status` | `bash $RE status <modulo>` |

## Andamento ao vivo (obrigatorio — o usuario acompanha pela tela)
A tela Engenharia reversa mostra a sessao **enquanto ela roda**: etapas (sessao → inventario → banco da DEMO → leitura e escrita, com uma
subetapa por area → checagem → envio), a linha "agora" e um registro curto. `start`, `inventario`, `banco`, `trabalho`, `check`, `save` e
`submit` ja reportam sozinhos; o resto e voce:
- Antes de ler/escrever: `bash $RE etapa <m> <doc> leitura running --detail "N areas: A, B, C"`.
- Cada area: `bash $RE etapa <m> <doc> area:<nome> running --title "Area: <Nome>"` ao comecar (inclusive ao despachar o
  subagente dela) e `... area:<nome> completed --detail "12 RN, 4 UC, 3 TELA"` ao terminar.
- Mudou de assunto (arquivo/fluxo novo): `bash $RE atividade <m> <doc> "Lendo KaizenWorkflowService — regras de etapa"`
  (curto, sem comando nem segredo). Achado/decisao relevante: `bash $RE log <m> <doc> "..." progress|warning`.
- **Grave o rascunho a cada area concluida** (`bash $RE save <m> <doc>`): a tela mostra o documento crescendo.
- Terminou a leitura: `bash $RE etapa <m> <doc> leitura completed --detail "<itens por tipo>"`.
Agrupe numa chamada de Bash (`... && ...`) para nao gastar turnos so com andamento.

## Fluxo de um documento
**0. Atualizar** — `bash ~/.claude/skills/.prmake/prmake-skills.sh update --quiet engenharia-reversa 2>/dev/null || true`
(se atualizou, releia esta SKILL.md).

**1. Modulo** — `bash $RE modulo [<chave>]` (pela pasta atual: o repositorio e as fontes cadastradas). Exit 2/3: pergunte ao
usuario a chave (a tela Engenharia reversa lista). Depois `bash $RE status <modulo>`.

**2. Sessao** — `bash $RE start <modulo> <doc> [new|improve|redo]` (sem modo: `improve` se ja ha publicado, senao `new`).
Grava o pacote em `~/.prmake/reverse/<modulo>/<doc>/` e imprime o resumo. **Leia, nesta ordem**: `nota-revisor.md` (se
houver, e a prioridade), `modelo.md` (o que o documento DEVE ter — secoes `##` obrigatorias e o formato de cada item),
`sugestoes.md` (o que as analises e as pessoas apontaram), `ids-outros-documentos.tsv` (IDs ja usados no modulo —
referencie, nao redefina), `relacionados.md` (itens dos modulos com quem este conversa), `anexos.md` (Figma/prototipos).
O ponto de partida antigo: `bash $KB show <modulo> <secao>` das secoes listadas. Retomada (`RETOMADA`): continue o
`documento.md` local.

**3. Inventario (sem LLM)** — `bash $RE inventario <modulo>`: endpoints, validacoes e mensagens (back, front e alerts do
legado), permissoes, tabelas, eventos/filas, jobs, handlers, procedures, rotas/componentes/chamadas HTTP do front,
paginas `.asp`, chaves de config. **E a lista do que o documento precisa cobrir — sem excecao.** Legado (`edv-solvace`)
exige a subpasta do modulo: `--path edv-solvace=solvace-asp/systems/<sigla>` (veja "Onde esta" em `kb.sh show <modulo>
modulos`; pode repetir `--path` para `solvace-core/<modulo>` — a primeira pasta substitui a da tela, as demais somam;
arquivo e glob tambem somam, ex. `--path edv-solvace='solvace-core/helpers/**/Sa3*.cs'` para os servicos .NET que ficam
junto com os de outros modulos). Revamp: o back (`revamp-<X>`) e o front
(`edv-solvace-apps/projects/<x>`) sao fontes do mesmo modulo — fonte faltando: peca ao usuario/aprovador para cadastrar
na tela (Fontes) ou use `--path`.

**3b. Banco da DEMO (0053, todo modulo — legado e revamp)** — `bash $RE banco <modulo>`: le **direto do banco de
referencia** (a DEMO: global e os locais, configurados no PRMake; somente leitura pelo `sql-query.sh`) as tabelas do
modulo (prefixo `TB_<SIGLA>_` deduzido do codigo — confira; `--prefix`/`--sigla` corrigem), e o que depende delas:
views, procedures, functions, **triggers**, colunas/chaves/**check constraints** e os **jobs do SQL Agent**, com o corpo
de cada objeto em `~/.prmake/reverse/<modulo>/banco/`. **Nunca** use os scripts `solvace-asp/#database/…` nem migracoes
versionadas (desatualizados). Sem acesso (VPN/credencial) → a etapa fica **falha no andamento** com o que fazer; diga
ao usuario e so siga sem o banco se ele mandar (e registre `GAP`). Depois `bash $RE termos <modulo>`: os termos da tela
(rotulos traduzidos, menus, siglas, traducoes EN/ES do **Multilingual do revamp** — lidas no fim do `banco` pelo
`re.sh traducoes`; sem a credencial `~/.claude/multilingual-credentials.json` ele explica como obter e o glossario segue
com os rotulos do codigo) que o **glossario** precisa cobrir. Como documentar objetos do
banco e o glossario: `references/banco.md` (leia antes de escrever arquitetura ou funcional).

**4. Ler o codigo e escrever** — `references/escrever.md` (leia **inteiro** antes de escrever; e curto). Resumo:
- Leia o codigo **de verdade** (controllers → services → repositorios/SP → tabelas; telas → componentes → servicos HTTP).
  Profundidade > velocidade: cada regra com condicao, valores e mensagem **literais** e o `**Onde:** arquivo:linha`.
- Modulo grande (inventario > ~150 itens): divida por area funcional e use **subagentes** (`general-purpose`), um por
  area, cada um com uma **faixa de IDs** (A: RN-001..099, B: RN-100..199...), escrevendo `parte-<area>.md` na pasta do
  documento com as mesmas secoes `##` do modelo; junte com `python3 ~/.claude/skills/engenharia-reversa/scripts/re_tool.py
  juntar documento.md parte-*.md` (avisa ID repetido). O contexto principal fica limpo: cada subagente volta so o resumo.
- Integracoes: para cada chamada a outro modulo (HTTP, fila/evento, tabela de outro dono, pacote), um `INT-…` com
  `**Modulos:**` (chave do outro projeto) e a evidencia dos dois lados quando o outro repositorio estiver na maquina; use
  `relacionados.md` e `bash $RE impact <tabela>` para ver o que o outro lado ja publicou.
- Regras de negocio: `bash $KC search <termos do modulo>` — cite `**KC:** ART-n` quando o Knowledge Center documenta a
  regra; divergencia entre KC e codigo vira `GAP`.

**5. Checar ate cobrir** — `bash $RE check <modulo> <doc>`: cobertura do inventario (o que falta, com arquivo:linha) +
checagem do PRMake (secoes obrigatorias, IDs, evidencia, IDs removidos, referencias). **Repita** escrever → check ate:
sem erros e cobertura ≥ a minima (padrao 90%). O que ficar de fora de proposito (codigo morto, infraestrutura) entra em
`## Lacunas` como `GAP` com o motivo — assim conta como coberto e o revisor sabe.

**6. Enviar** — `bash $RE submit <modulo> <doc> --summary "o que cobre / o que mudou (itens novos, corrigidos)"`. Vai para
revisao no PRMake (tela **Engenharia reversa** → modulo → documento). **Nunca** aprova nem publica — e uma pessoa (papel
aprovador) que aprova e publica; so entao fica ativo na Base Solvace. Diga ao usuario: modulo, documento, revisao #n,
itens por tipo, cobertura, avisos e onde aprovar. Rascunho intermediario (sem enviar): `bash $RE save <modulo> <doc>`.

## Melhorar e refazer
- **melhorar**: o `documento.md` comeca igual ao publicado. Rode `bash $RE inventario`, `bash $RE banco` e
  **`bash $RE trabalho <modulo> <doc>`**: a lista do que mudou no codigo (commits gravados na versao publicada × agora)
  e no banco (catalogo publicado × atual) e os **itens publicados afetados** (`trabalho.md`) — e a pauta da sessao.
  Resolva primeiro a nota do revisor, depois a lista de trabalho, as sugestoes (`sugestoes.md`: aprendizados,
  divergencias e lacunas das analises) e o que o `check` mostrar como faltando. **Mantenha os IDs**; item que deixou de
  existir vira `### RN-012 — (removido) <motivo>` (analises antigas citam o ID). No `--summary`, liste o que mudou por ID.
- **Decida cada sugestao do pacote** e envie com `--sugestoes decisoes.json`:
  `[{"suggestionId": "<id>", "decision": "aplicada", "items": ["RN-012"], "note": "o que mudou"},
  {"suggestionId": "<id>", "decision": "recusada", "note": "motivo"}]` — ao publicar, a fila resolve sozinha (aplicada /
  recusada, e o card de origem fica sabendo pela Timeline). Sugestao sem decisao continua pendente.
- **refazer**: escreva do zero, mas reaproveite o ID de cada assunto que continua existindo (`publicado.md` e
  `bash $RE ids <modulo>` mostram os IDs atuais).

## Visao pratica (0054) — `/engenharia-reversa <modulo> pratica`
O guia do sistema para quem usa (QA, suporte, gestores, clientes): o que e e onde fica (**legado ou revamp**), como
chegar em cada tela, **como fazer** cada tarefa (`### TUT-001 — Como criar um A3`) e as **perguntas praticas**
(`### FAQ-001 — O A3 e legado ou revamp?`). Regras duras (a checagem barra):
- Escreva **so a partir de `publicados/*.md`** (o que foi aprovado) — **nao leia codigo nem banco**. O que a engenharia
  reversa nao cobre nao entra aqui: registre a lacuna no documento tecnico certo
  (`arch.sh suggest <modulo> re-<doc> lacuna.md --kind gap --item <ID>`).
- **Toda** frase, passo e resposta termina com a fonte: `<!-- fonte: RN-012, TELA-003 -->` (IDs publicados; de outro
  modulo: `revamp-users#FN-002`). Fonte inexistente ou de item removido = erro.
- **Sem termo tecnico**: nada de tabela (`TB_…`), objeto de banco, arquivo (`.asp`, `.cs`…), rota `/api/…`, classe
  (`…Controller`). Nome de tela e de menu como o usuario ve.
- **FAQ das perguntas reais**: `perguntas.md` traz o que as pessoas perguntaram no Pergunte sobre o modulo (as mais
  frequentes primeiro) — responda todas que a engenharia reversa permite; `bash $RE perguntas <modulo>` mostra quais
  ficaram sem resposta (o revisor ve essa cobertura). Inclua tambem as perguntas obvias de quem chega: legado ou
  revamp?, onde fica?, quem pode?, por que nao aparece?, quem recebe o e-mail?
- Republicado um documento tecnico, a visao pratica fica "desatualizada" na tela: rode `melhorar` dela.

## Armadilhas (0054) — o que ja deu errado, ligado aos itens
A engenharia reversa descreve "como o sistema e"; armadilhas sao "o que ja deu errado" (sintoma, causa raiz, como
diagnosticar, cards). Ficam ligadas aos itens (`RN-020`) e as analises recebem as duas.
- **Migracao (automatica, uma vez por modulo)**: se o `start` disser `MIGRACAO PENDENTE`, leia `armadilhas-antigas.md`
  e `sugestoes.md` (as sem item) e monte `migracao.json` ligando **cada** armadilha antiga ao item mais provavel
  (`{"traps":[{"title","text","items":["RN-020"],"cards":["75067"]}], "suggestions":[{"id","itemId":"RN-020","sectionKey":"re-funcional"}]}`)
  → `bash $RE migrar <modulo> migracao.json`. Fica "a conferir" ate um aprovador conferir na tela; a secao antiga sai
  do espelho.
- Armadilha nova (visto ao ler o codigo/banco): `bash $RE armadilha <modulo> --titulo "..." --texto-file t.md --itens RN-020`.
- Nao escreva armadilha dentro dos documentos tecnicos.

## UI/UX (Figma e prototipos)
O documento `uiux` mapeia **cada tela** do front ao banco (tela → componente → servico HTTP → API → tabela). Anexos do
modulo (links do Figma/prototipo, imagens, PDFs) vem na sessao (`anexos.md`, arquivos em `anexos/` — abra as imagens com
Read). Com o MCP do Figma conectado nesta sessao, leia os frames pelos links; sem ele, use as imagens exportadas. O
usuario pode anexar pela tela ou aqui: `bash $RE link <modulo> <url> "<titulo>" --screens TELA-001` /
`bash $RE upload <modulo> <arquivo> "<titulo>"`. Divergencia prototipo × implementado = `GAP`.

## Regras
- **Nada inventado.** Nao confirmou no codigo → "a confirmar" + `GAP`. Nunca credenciais, connection strings, tokens ou
  dados de cliente (a checagem barra). Mensagens literais, sim (sao parte da regra).
- **ID unico no modulo e estavel**; itens de outro documento so referenciados (`TELA-003`, `revamp-users#API-004`).
- O documento e para **pessoas e LLM**: frase curta, valores exatos, uma coisa por item, sem "etc.". O titulo do item
  diz a regra (e o que aparece na busca).
- Somente leitura no codigo e nos bancos. Nada de commit nos repositorios do modulo.
- Conteudo em massa e aqui no Claude Code (assinatura) — nunca pela IA do PRMake.
