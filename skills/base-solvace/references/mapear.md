# Mapear / atualizar a engenharia reversa (quem roda: admin, com os repositórios na máquina)

Objetivo: cada projeto com o template de `template-secoes.md`, publicado no PRMake (`arch.sh`). Economize tokens:
um projeto por vez, leia estrutura antes de código, use subagente (Explore) para varreduras amplas e traga só o
resumo para o contexto principal.

## Mapear um projeto do zero
1. `bash $ARCH list` — o que já existe. Escolha a chave (nome do repo em minúsculas: `edv-solvace`, `revamp-actionplan`).
2. Estrutura primeiro (barato): árvore de pastas até 2–3 níveis, `README`/`CLAUDE.md`, arquivos de solução/projeto,
   `appsettings*.json` (só NOMES de chaves), Dockerfile/pipelines, migrações/DDL. Material da Solvace que ajuda:
   `revamp_separado/revamp-KnowledgeCenter/docs/claude-global/` (revamp/architecture.md, revamp/context, source/).
3. Depois o código dos pontos de entrada (controllers, telas, jobs) e das entidades principais.
4. Regras de negócio: `bash $KC search <modulo/tela/entidade>` — cite os ART-n na seção `regras-de-negocio`.
5. Escreva cada seção num arquivo em `$CARD_TMP/<chave>/` e publique:
   `bash $ARCH project <chave> --name "..." --kind revamp --repo <url> --summary-file resumo.md --keywords "a,b" --repo-dir <pasta>`
   `bash $ARCH section <chave> visao-geral visao-geral.md --title "Visão geral" --order 10 --note "mapeamento inicial"`
6. `bash $KB sync` — confira o INDEX.md e a seção publicada.

## Atualizar (incremental)
1. `bash $ARCH stale <chave> <pasta-do-repo>` — commits e pastas alteradas desde o commit mapeado.
2. Releia só o que mudou; reescreva só as seções afetadas (`--note "atualizado até <commit>: ..."`).
3. Regrave o projeto com `--repo-dir` para o commit novo valer.
4. Artigos do KC novos/alterados: `bash $KC sync` e revise a seção `regras-de-negocio` dos módulos citados.

## Regras
- Nunca credenciais/connection strings/dados de cliente. Só nomes de recursos e chaves.
- Nada inventado: o que não foi verificado vai como "a confirmar".
- Escrita só com api-key de admin; seção igual à publicada não cria versão.
