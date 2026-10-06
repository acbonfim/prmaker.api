---
name: engenharia-reversa
description: Faz a engenharia reversa PROFUNDA de um modulo Solvace (legado ou revamp, back e front) com o Claude aberto no repositorio do modulo — levantamento funcional, levantamento de arquitetura, UI/UX (com Figma/prototipo), especificacao de visao, especificacao de arquitetura e especificacao de design — com TODAS as regras de negocio, casos de uso, integracoes entre modulos e tecnologias, em itens com ID (RN-012, UC-003, API-004...). Cada documento vai para aprovacao no PRMake e, publicado, fica ativo na Base Solvace (as analises consultam por item, sem ir ao codigo). Use quando o usuario pedir "engenharia reversa do modulo", "levantamento funcional", "levantamento de arquitetura", "especificacao de visao/arquitetura/design", "mapear UI/UX", "melhorar/refazer a engenharia reversa", "mapear a infra/AWS do modulo" (esteiras, buckets, segredos, logs), "/engenharia-reversa".
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
| `/engenharia-reversa <modulo> infra` | **opcional**: mapeia a infra do modulo na AWS (esteiras de deploy, Lambdas, buckets, segredos por nome, logs no CloudWatch) e envia para a aba **Infra** da tela — veja "Infra na AWS" abaixo |
| `/engenharia-reversa <modulo> status` | `bash $RE status <modulo>` |
| `/engenharia-reversa retrato [banco\|infra\|status]` | **0066**: baixa UMA vez o banco de referência inteiro (`re.sh retrato banco`, precisa da VPN) e/ou a conta AWS (`re.sh retrato infra`, ~15 min); os módulos usam o retrato enquanto ele estiver dentro da idade máxima (sem VPN, sem esperar). `status` mostra datas e lacunas da coleta. Rode de novo para atualizar. |

## Andamento ao vivo (obrigatorio — o usuario acompanha pela tela)
A tela Engenharia reversa mostra a sessao **enquanto ela roda**: etapas (sessao → inventario → banco da DEMO → leitura e escrita, com uma
subetapa por area → checagem → envio), a linha "agora" e um registro curto. `start`, `inventario`, `banco`, `infra`, `trabalho`, `check`, `save` e
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

**3. Inventario (sem LLM)** — `bash $RE inventario <modulo>` (0067: le a **master atualizada** de cada repositorio — `git fetch`
e uma copia so de leitura em `<clone>/../.prmake-wt/master/<repo>`; o clone de trabalho nao e tocado e a branch em que
ele estiver nao importa. `--local` so para documentar uma branch que ainda nao entrou na master): endpoints, validacoes e mensagens (back, front e alerts do
legado), permissoes, tabelas, eventos/filas, jobs, handlers, procedures, rotas/componentes/chamadas HTTP do front,
paginas `.asp`, chaves de config. **E a lista do que o documento precisa cobrir — sem excecao.** Legado (`edv-solvace`)
exige a subpasta do modulo: `--path edv-solvace=solvace-asp/systems/<sigla>` (veja "Onde esta" em `kb.sh show <modulo>
modulos`; pode repetir `--path` para `solvace-core/<modulo>` — a primeira pasta substitui a da tela, as demais somam;
arquivo e glob tambem somam, ex. `--path edv-solvace='solvace-core/helpers/**/Sa3*.cs'` para os servicos .NET que ficam
junto com os de outros modulos). Revamp: o back (`revamp-<X>`) e o front
(`edv-solvace-apps/projects/<x>`) sao fontes do mesmo modulo — fonte faltando: peca ao usuario/aprovador para cadastrar
na tela (Fontes) ou use `--path`.

**3b. Banco da DEMO (0053, todo modulo — legado e revamp)** — `bash $RE banco <modulo>`: le o **retrato** do banco de
referencia quando existe e esta dentro da idade maxima (0066 — `re.sh retrato banco` baixa uma vez para todos os
modulos; `--ao-vivo` le direto) ou **direto do banco** (a DEMO: global e os locais, configurados no PRMake; somente leitura pelo `sql-query.sh`) as tabelas do
modulo (prefixo `TB_<SIGLA>_` deduzido do codigo — confira; `--prefix`/`--sigla` corrigem), e o que depende delas:
views, procedures, functions, **triggers**, colunas/chaves/**check constraints** e os **jobs do SQL Agent**, com o corpo
de cada objeto em `~/.prmake/reverse/<modulo>/banco/`. **Nunca** use os scripts `solvace-asp/#database/…` nem migracoes
versionadas (desatualizados). Sem acesso (VPN/credencial) → a etapa fica **falha no andamento** com o que fazer; diga
ao usuario e so siga sem o banco se ele mandar (e registre `GAP`). Depois `bash $RE termos <modulo>`: os termos da tela
(rotulos traduzidos, menus, siglas, traducoes EN/ES do **Multilingual do revamp** — lidas no fim do `banco` pelo
`re.sh traducoes`; sem a credencial `~/.claude/multilingual-credentials.json` ele explica como obter e o glossario segue
com os rotulos do codigo) que o **glossario** precisa cobrir. Como documentar objetos do
banco e o glossario: `references/banco.md` (leia antes de escrever arquitetura ou funcional).

**3c. Infra na AWS (OPCIONAL, 0058)** — e o comando `/engenharia-reversa <modulo> infra` (secao "Infra na AWS" abaixo); dentro
de uma sessao de documento, so quando o usuario pedir (ou aceitar a oferta) — internamente `bash $RE infra <modulo>`. Le a AWS
pelo **AWS CLI, somente leitura** (a conta vem da configuracao do PRMake; o perfil e achado na maquina) e mapeia tudo:
Lambdas, buckets S3, **esteiras de deploy** (CodePipeline/CodeBuild/CodeDeploy + GitHub Actions/buildspec dos repositorios),
**segredos consultados** (so nomes e ultimo acesso — nunca o valor), **logs no CloudWatch**, filas, topicos, regras,
bancos etc., ligando ao modulo. Leia `~/.prmake/reverse/<modulo>/infra/modulo.md` e `references/infra.md` antes de escrever
a secao "Infraestrutura e AWS (opcional)" do levantamento de arquitetura (itens `INF-…`). Sem perfil/permissao a etapa fica
**falha** no andamento (nao barra): siga e registre `GAP` "infra nao lida". O usuario autoriza a leitura da AWS — confirme
antes de rodar na primeira vez da sessao.

**Visao e spec de arquitetura sao SINTESES** (o modelo diz: "a partir do levantamento"): **nao leia o codigo** nem divida em
areas. `re.sh areas` registra a faixa de IDs e `re.sh pacote` monta `pacotes/pacote-sintese.md` com os itens do funcional
e da arquitetura (o rascunho local desta maquina; senao o publicado). UM subagente (area `sintese`, o mesmo prompt) ou
voce escreve a partir dele; codigo so para confirmar um ponto que os itens nao explicam. Depois `juntar` e `check`.

**4. Ler o codigo e escrever — por AREAS, com pacote de leitura (0066: e o que deixa barato e rapido)** —
`references/escrever.md` (curto) tem o padrao do item. O custo de um subagente e (chamadas × contexto): area pequena,
leitura de uma vez e nada de exploracao. **Voce (sessao principal) nao le o codigo** — orquestra e consolida.
1. `bash $RE areas <m> <doc>` — o script divide o codigo do documento em **areas** que cabem no orcamento (config
   `areaBudgetKb`), arquivos do mesmo assunto juntos, cada area com uma **faixa de IDs** que nao colide com os do modulo.
2. `bash $RE pacote <m> <doc>` — um **pacote por area** (`pacotes/pacote-<area>.md`: o codigo inteiro da area em blocos,
   com o numero real das linhas, os itens do inventario que ela precisa cobrir e as tabelas dela) + o **cartao**
   (`pacotes/cartao.md`: instrucoes curtas + modelo + chaves dos modulos). Imprime o **modelo dos subagentes** e quantos
   rodam ao mesmo tempo (config `subagentModel`/`modelByDoc`/`maxParallel`).
3. Um subagente `general-purpose` **por area**, em segundo plano, com **`model` = o que o `pacote` imprimiu**, no maximo
   `maxParallel` ao mesmo tempo (despache TODOS de uma vez quando couber; senao o proximo assim que um terminar).
   **Especiais** (o que e do modulo inteiro — o `pacote` lista): funcional → `glossario`; arquitetura → `banco` (tabelas,
   views/procedures, triggers e jobs do catalogo/retrato; `banco-p1`, `banco-p2`… se for grande) e `modulo`
   (tecnologias, configuracao so por nome, seguranca, observabilidade, infra). Um subagente para cada, com o MESMO prompt
   (trocando a area), despachado junto com as areas. As areas nao criam esses tipos (o cabecalho do pacote diz quais
   criam) — nada de juntar duplicado depois.
   Prompt (exatamente isto, trocando os campos):
   `Subagente da engenharia reversa — modulo <m>, documento <doc>, area <area>. Leia NA MESMA RESPOSTA: <D>/pacotes/cartao.md
   e <D>/pacotes/pacote-<area>.md. Saida: <D>/parte-<area>.md (checkpoint a cada <N> itens; se ja existe, continue dela).
   Ao terminar: bash ~/.claude/skills/engenharia-reversa/scripts/re.sh faltando <m> <doc> <area>. Responda so o resumo.`
   Andamento: `etapa ... area:<area> running` ao despachar e `completed --detail "<itens>"` ao voltar.
4. **Subagente que caiu** (limite de sessao, erro): despache de novo com o MESMO prompt — ele continua da parte gravada.
5. `bash $RE juntar <m> <doc>` — junta as partes em `documento.md` (sobre o publicado, no melhorar: item reescrito
   substitui o antigo) e **compacta os IDs** das faixas (RN-2701 → RN-058, referencias juntas). Pode rodar de novo.
6. Consolide na sessao principal **so o que e do modulo inteiro** (resumo, perfis `PRF` a partir dos resumos dos
   subagentes, diagramas mermaid, integracoes consolidadas) — **sem ler o `documento.md` inteiro** (centenas de KB a cada
   resposta): use o `check`, `grep -n '^## \|^### ' documento.md` e leia so o trecho que vai editar, lendo o documento juntado e o `check` — sem reler o codigo. Area pequena (1 so): pode escrever voce mesmo
   a partir do pacote, sem subagente.
- Integracoes: para cada chamada a outro modulo (HTTP, fila/evento, tabela de outro dono, pacote), um `INT-…` com
  `**Modulos:**` **so com a chave** do outro projeto (`modulos.tsv` do pacote da sessao; servico externo `ext:<nome>`),
  `**Mecanismo:**` do vocabulario e a evidencia dos dois lados quando o outro repositorio estiver na maquina; use
  `relacionados.md` e `bash $RE impact <tabela>` para ver o que o outro lado ja publicou. E isso que desenha o mapa.
- Integracoes: para cada chamada a outro modulo (HTTP, fila/evento, tabela de outro dono, pacote), um `INT-…` com
  `**Modulos:**` (chave do outro projeto) e a evidencia dos dois lados quando o outro repositorio estiver na maquina; use
  `relacionados.md` e `bash $RE impact <tabela>` para ver o que o outro lado ja publicou.
- Regras de negocio: `bash $KC search <termos do modulo>` — cite `**KC:** ART-n` quando o Knowledge Center documenta a
  regra; divergencia entre KC e codigo vira `GAP`.

**5. Checar ate cobrir** — `bash $RE check <modulo> <doc>`: cobertura do inventario (o que falta, com arquivo:linha),
**evidencia conferida nas fontes** (0066: `arquivo:linha` que nao existe e literal que nao aparece no arquivo citado —
corrija antes de enviar) + checagem do PRMake (secoes obrigatorias, IDs, evidencia, IDs removidos, referencias,
**Modulos/Mecanismo dos INT**). O que faltar de uma area: `re.sh faltando <m> <doc> <area>` e um subagente com o
pacote da area (nunca a sessao principal relendo codigo). **Repita** escrever → check ate:
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

## Infra na AWS (0058/0059) — `/engenharia-reversa <modulo> infra`
Etapa **opcional**, fora dos documentos: o resultado vai para a aba **Infra** do modulo na tela (recursos, esteiras de deploy,
segredos so por nome, logs) e serve de fonte para a secao "Infraestrutura e AWS (opcional)" do levantamento de arquitetura.
1. Atualizar (passo 0) e `bash $RE modulo <modulo>`.
2. Sem `~/.prmake/reverse/<modulo>/inventario.json`: `bash $RE inventario <modulo>` (mesmas regras do passo 3, inclusive `--path`
   no legado) — a infra liga os recursos ao que o codigo cita e le as esteiras dos repositorios.
3. **Confirme com o usuario** antes de ler a AWS (uma vez por sessao): conta e regioes vem do PRMake
   (`ReverseEngineeringInfra`); o perfil do AWS CLI e achado na maquina. A conta inteira leva ~15 min — com o **retrato**
   (0066: `re.sh retrato infra`, uma vez para todos os modulos) o `infra` do modulo usa a leitura guardada enquanto ela
   estiver dentro da idade maxima e nao chama a AWS (`--ao-vivo` le de novo).
4. `bash $RE infra <modulo>` (outra conta/regiao: `--account`/`--region`; recurso que o nome nao pega: `--termo <t>`). Ele mapeia,
   liga ao modulo e **envia para a aba Infra** (substitui a leitura anterior).
5. Leia `infra/modulo.md` e diga ao usuario: recursos do modulo por servico, esteiras, segredos, grupos de log, o que ficou
   **sem permissao** (mapa incompleto) e que o resultado esta na aba **Infra** do modulo. Ligacao duvidosa → diga.
6. Com sessao de `arquitetura` aberta (ou na proxima `melhorar arquitetura`), escreva os itens `INF-…` a partir do `modulo.md`
   (`references/infra.md`).

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

## Custo (0066)
- `bash $RE config` mostra a configuracao da geracao (Skills Configurations → `ReverseEngineeringGeneration`): modelo dos
  subagentes (padrao `sonnet`; `modelByDoc` troca por documento, ex. `{"design": "haiku"}`), simultaneos, orcamento da
  area, checkpoint e idade maxima do retrato. Mudou a configuracao: vale na proxima sessao, sem atualizar a skill.
- Nao gaste turno: agrupe comandos de andamento numa chamada; leia varios arquivos na mesma resposta; nunca releia o que
  um subagente ja leu — peca a ele (ou ao `faltando`).

## Regras
- **Nada inventado.** Nao confirmou no codigo → "a confirmar" + `GAP`. Nunca credenciais, connection strings, tokens ou
  dados de cliente (a checagem barra). Mensagens literais, sim (sao parte da regra).
- **ID unico no modulo e estavel**; itens de outro documento so referenciados (`TELA-003`, `revamp-users#API-004`).
- O documento e para **pessoas e LLM**: frase curta, valores exatos, uma coisa por item, sem "etc.". O titulo do item
  diz a regra (e o que aparece na busca).
- Somente leitura no codigo e nos bancos. Nada de commit nos repositorios do modulo.
- Conteudo em massa e aqui no Claude Code (assinatura) — nunca pela IA do PRMake.
