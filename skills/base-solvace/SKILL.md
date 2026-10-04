---
name: base-solvace
description: Base de conhecimento da Solvace para as analises — engenharia reversa de todos os projetos (legado edv-solvace, apps Angular, API de integracoes, modulos revamp, AWS, login, terceiros) e as regras de negocio do Knowledge Center, num espelho local barato de ler (~/.claude/solvace-kb). Use para consultar arquitetura/regra de negocio antes de vasculhar codigo, para sincronizar o Knowledge Center ("sincronizar knowledge", "kc sync") e para mapear/atualizar a engenharia reversa ("mapear arquitetura", "engenharia reversa do <projeto>", "atualizar a base solvace"). As skills analisar-bug, gerar-prmake e gerar-handover dependem desta.
---

# Base Solvace (engenharia reversa + Knowledge Center)

Scripts (`$KB`, `$KC`, `$ARCH` abaixo):
```bash
KB=~/.claude/skills/base-solvace/scripts/kb.sh      # espelho local: sync | index [termos] [--full] | show <projeto> [secao] | find <termo> | status | agendar
KC=~/.claude/skills/base-solvace/scripts/kc.sh      # Knowledge Center: search <termos> | article <n> | sync [--full] | check
ARCH=~/.claude/skills/base-solvace/scripts/arch.sh  # publicar (admin): list | project | section | get | stale | guia | lacunas | resolver; learn <card> (todos)
```

## Engenharia reversa por modulo (0052) — consulte PRIMEIRO
Modulos com engenharia reversa publicada (documentos `re-funcional`, `re-arquitetura`, `re-uiux`, `re-visao`,
`re-spec-arquitetura`, `re-design`, aprovados no PRMake) tem cada regra, caso de uso, tela, endpoint, tabela e integracao
como **item com ID** (`revamp-kaizen#RN-012`). Leia por item, nunca o documento inteiro:
- MCP (preferido; com `card` registra a consulta): `prmake_base_search(query, module, kinds, card)` → referencias;
  `prmake_base_get(refs, card)` → so o texto; `prmake_base_impact(tabela|item|modulo)` → quem mais usa;
  `prmake_base_module(module)` → ficha do modulo e IDs.
- Offline (espelho): `bash $KB re find <termos> [--module m] [--kind RN]` e `bash $KB re get <modulo>#<ID> [--card N]`;
  o `kb.sh index` marca `RE n/6` nos projetos que tem.
- Gerar/melhorar a engenharia reversa de um modulo: skill `engenharia-reversa` (Claude aberto no repositorio do modulo;
  a tela Engenharia reversa do PRMake mostra o passo a passo e o andamento ao vivo). Divergencia/lacuna achada numa
  analise: `arch.sh suggest <modulo> re-<doc> nota.md --kind divergence|gap --card <card>` citando o ID.

## Consultar (toda analise — e o que economiza tokens)
1. **Indice filtrado primeiro**: `bash $KB index <modulo tela termos>` traz so os projetos/artigos que casam (linha
   completa, com as secoes; `⇄ d/u` = depende de d, usado por u). Sem termos, `bash $KB index` e o indice compacto
   (uma linha curta por projeto); `--full` (~20 KB) nao e para analise — tudo que entra no contexto e relido a cada
   resposta. Na `analisar-bug`, o `contexto` ja mostra os projetos do card. Abra **so** o que o caso pede:
   `bash $KB show <projeto>` (ficha: resumo,
   **depende de / usado por** com evidencia arquivo:linha) e `bash $KB show <projeto> <secao>`. So depois va ao
   codigo, direto nas pastas/arquivos apontados (nada de grep no repositorio inteiro).
   **Dois mundos (0045)**: cada modulo tem o legado (`legado-<modulo>`: `edv-solvace` — `solvace-asp/systems/<sigla>`
   e `solvace-core/<modulo>`, telas → `.asp`/controller → service/SP → tabelas) e o revamp (`revamp-<modulo>`). O
   `index` mostra o par do outro mundo (↳). Nao sabe o mundo ou a sigla: `bash $KB show edv-solvace modulos`
   (glossario sigla/pasta → projeto). Base sem o caso → busque so na pasta do modulo e proponha a lacuna
   (`arch.sh suggest <projeto> modulos lacuna.md --kind gap --card <card>`).
2. **Impacto entre modulos**: mexeu em tabela `TB_<SIGLA>_*`, fila, topico SNS ou evento? Veja o "Usado por" da
   ficha do dono (e `bash $KB show ecossistema integracoes` para os hubs: Users, Notification, Post, CommentManager,
   MasterData) e cite os modulos afetados na analise.
3. **Regra de negocio**: antes de perguntar ao usuario ou concluir o comportamento "esperado", consulte o Knowledge
   Center: `bash $KC search <modulo tela termo>` e `bash $KC article <n>`. **Cite o ART-n** na analise, no RCA e no
   handover quando usar. Sem artigo sobre a regra: diga isso (lacuna de documentacao) — nao invente.
4. A base pode estar atras do codigo: cada secao traz o commit de origem; se o que o codigo mostra diverge, vale o
   codigo — e registre a divergencia (sugestao de atualizacao da secao).

## Knowledge Center — garantias (fixas)
- **Filtro de dados de teste sempre ativo**: so artigos publicados, nao removidos, de categoria ativa, sem cara de
  teste ("teste", "qa", "title-", "probe", "editado"...) e com texto util. O piso esta no codigo (script e PRMake);
  o plugin "Knowledge Center Configurations" so acrescenta exclusoes. Conteudo de teste nunca sai da maquina.
- **Mantido atualizado pelas skills**: `bash $KC sync --quiet` le so o que mudou desde a marca d'agua (carga
  completa periodica) e envia ao PRMake; depois `bash $KB sync --quiet`. Roda sozinho em segundo plano no inicio
  de cada sessao (hook do prmake-skills, no maximo a cada 30 min) e, opcionalmente, a cada 2 h com
  `bash $KB agendar install`. `kb.sh index` avisa quando o espelho passou de 24 h sem conferir. Sem credencial local
  (~/.claude/knowledgecenter-credentials.json) o sync do KC e pulado e a base do PRMake vale.
- **Ambiente** (dev hoje): definido no plugin; trocar para prod = mudar `Environment` no plugin + credencial `prod`
  no arquivo local — a proxima sincronizacao faz a carga completa. Nada muda nas skills.
- Sessao com o banco sempre somente leitura. Nunca imprima a credencial.

## Guia, aprender com um card e lacunas (0038)
> **Custo (0042):** `guia`, `learn` e as telas de IA do PRMake (Pergunte, Analisar a fundo, Gerar guia, Aprender com
> card, Gerar com IA) rodam no servidor com a **chave de API do perfil de quem chama** (créditos pagos, não a assinatura
> do Claude Code) — o `arch.sh` avisa antes e mostra os tokens e o custo estimado no fim; na tela, "Meu consumo de IA".
> Conteúdo **em massa** (guias de vários projetos, seções, procedimentos): escreva aqui no Claude Code (subagentes) e só
> publique com `section`/`publicar-pasta`. Chamar a IA do PRMake mais de 1–2 vezes seguidas → peça ok ao usuário antes,
> com a estimativa (Pergunte ≈ US$ 0,01; Analisar a fundo/Guia/Aprender ≈ US$ 0,03–0,05 com Haiku 4.5).
- **Guia** (seções `guia-*`, público `human`): a mesma base em linguagem simples para QA/gestores, só na tela do
  PRMake — **não** vem no espelho nem no índice (a análise não lê). Template e tom: `references/template-secoes.md`.
  Escrever aqui (preferido, sem custo de API) seguindo o template, ou um projeto pontual com a IA do PRMake:
  `bash $ARCH guia <chave> <pasta>` (admin, custa créditos) → revisar → publicar.
- **Aprender com um card** que não virou sugestão sozinho: `bash $ARCH learn <card>` (o PRMake junta DevOps, PR/RCA,
  Timeline e planos; a IA propõe) → `--send 1,2|all` envia para a fila. Na tela: botão "Aprender com um card".
- **Lacunas** (`gap`): perguntas do "Pergunte" que a base não cobre. Admin: `bash $ARCH lacunas` → analise o código
  apontado → publique a seção (`section`, com `--audience human` se for do Guia) → `bash $ARCH resolver <id> applied`.

## Operação e configuração (0040)
Perguntas de "como habilitar / dar acesso / onde configura / por que não aparece": projeto `operacao-plataforma` (vale
para todos os módulos) e a seção `operacao` + Guia `guia-como-configurar` de cada módulo. Mapear (catálogo do menu,
papéis e parâmetros, sem LLM) e **resolver as perguntas sem resposta do "Pergunte"** (`bash $ARCH perguntas`):
`references/operacao.md`.

## Mapear / atualizar a engenharia reversa (admin)
Leia `references/mapear.md` e `references/template-secoes.md` so quando for mapear ou atualizar.
