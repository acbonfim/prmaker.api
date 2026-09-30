---
name: base-solvace
description: Base de conhecimento da Solvace para as analises — engenharia reversa de todos os projetos (legado edv-solvace, apps Angular, API de integracoes, modulos revamp, AWS, login, terceiros) e as regras de negocio do Knowledge Center, num espelho local barato de ler (~/.claude/solvace-kb). Use para consultar arquitetura/regra de negocio antes de vasculhar codigo, para sincronizar o Knowledge Center ("sincronizar knowledge", "kc sync") e para mapear/atualizar a engenharia reversa ("mapear arquitetura", "engenharia reversa do <projeto>", "atualizar a base solvace"). As skills analisar-bug, gerar-prmake e gerar-handover dependem desta.
---

# Base Solvace (engenharia reversa + Knowledge Center)

Scripts (`$KB`, `$KC`, `$ARCH` abaixo):
```bash
KB=~/.claude/skills/base-solvace/scripts/kb.sh      # espelho local: sync | index | show <projeto> [secao] | find <termo> | status | agendar
KC=~/.claude/skills/base-solvace/scripts/kc.sh      # Knowledge Center: search <termos> | article <n> | sync [--full] | check
ARCH=~/.claude/skills/base-solvace/scripts/arch.sh  # publicar (admin): list | project | section | get | stale
```

## Consultar (toda analise — e o que economiza tokens)
1. **Indice primeiro**: `bash $KB index` (uma linha por projeto; `⇄ d/u` = depende de d, usado por u). Ache o
   projeto/modulo pelas palavras-chave e abra **so** o que o caso pede: `bash $KB show <projeto>` (ficha: resumo,
   **depende de / usado por** com evidencia arquivo:linha) e `bash $KB show <projeto> <secao>`. So depois va ao
   codigo, direto nas pastas/arquivos apontados (nada de grep no repositorio inteiro).
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

## Mapear / atualizar a engenharia reversa (admin)
Leia `references/mapear.md` e `references/template-secoes.md` so quando for mapear ou atualizar.
