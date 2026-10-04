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
| Pedido | O que fazer |
|---|---|
| `/engenharia-reversa <doc>` | um documento do modulo da pasta atual (doc: `funcional`, `arquitetura`, `uiux`, `visao`, `spec-arquitetura`, `design`) |
| `/engenharia-reversa <modulo> <doc>` | idem, informando o modulo (chave da Base Solvace) |
| `/engenharia-reversa tudo` | todos, nesta ordem: `arquitetura` → `uiux` → `funcional` → `visao` → `spec-arquitetura` → `design` (cada um enviado separado) |
| `/engenharia-reversa melhorar <doc>` | parte do publicado + sugestoes pendentes + lacunas das analises + nota do revisor |
| `/engenharia-reversa refazer <doc>` | do zero, mantendo os IDs dos assuntos que continuam existindo |
| `/engenharia-reversa status` | `bash $RE status <modulo>` |

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
modulos`; pode repetir `--path` para `solvace-core/<modulo>`). Revamp: o back (`revamp-<X>`) e o front
(`edv-solvace-apps/projects/<x>`) sao fontes do mesmo modulo — fonte faltando: peca ao usuario/aprovador para cadastrar
na tela (Fontes) ou use `--path`.

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
- **melhorar**: o `documento.md` comeca igual ao publicado. Resolva primeiro a nota do revisor, depois as sugestoes
  (`sugestoes.md`: aprendizados, divergencias e lacunas que as analises de cards registraram) e o que o `check` mostrar
  como faltando. **Mantenha os IDs**; item que deixou de existir vira `### RN-012 — (removido) <motivo>` (analises antigas
  citam o ID). No `--summary`, liste o que mudou por ID.
- **refazer**: escreva do zero, mas reaproveite o ID de cada assunto que continua existindo (`publicado.md` e
  `bash $RE ids <modulo>` mostram os IDs atuais).

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
