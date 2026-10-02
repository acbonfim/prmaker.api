# Mapear / atualizar a engenharia reversa (quem roda: admin, com os repositórios na máquina)

Objetivo: cada projeto com o template de `template-secoes.md` e as **relações** (depende de / usado por), publicado no
PRMake (`arch.sh`). Economize tokens: o extrator (`mapear.py`) faz o grosso sem LLM; o LLM só revisa e escreve o que
exige leitura (regras, fluxos, armadilhas). Use subagente (Explore) para varreduras amplas.

## Parque inteiro (revamp + HubSpot) — o caminho normal
1. Clones de leitura numa pasta própria (nunca nas cópias de trabalho do usuário), ex. `~/repos/solvace/kb-mirror`:
   `gh repo list electradv --limit 300 --json name -q '.[].name' | grep -i '^revamp-'` → `git clone --depth 50` (ou
   `git -C <repo> pull --ff-only`), mais `edv-solvace-hubspotApi`. `revamp-wiki` (wiki mantida por LLM) entra junto:
   a descrição de cada módulo vem de lá.
2. Gere tudo (fatos → relações → seções):
   `python3 ~/.claude/skills/base-solvace/scripts/mapear.py tudo <kb-mirror> <saida> --curados <kb-revisada>`
   - `<kb-revisada>`: projetos já revisados à mão (ecossistema, legado, infra-aws, login, regras, HubSpot…). Mantêm
     nome/resumo/seções; só ganham as seções que faltam e as relações novas.
   - Sai `<saida>/_relacoes.json` (~150 relações, com evidência) e avisos (`_warnings`: ex. Lambda com o mesmo nome
     em dois repos). Donos de tabela pelo prefixo `TB_<SIGLA>_` (mapa em `PREFIX_OWNER`); tabelas globais (WCM/SYS/GLB)
     não geram relação; origens de CORS não são dependência.
3. Revise o que mudou (`git diff` da pasta de saída se ela for versionada) e ajuste os curados, não o gerado.
4. Publique: `bash $ARCH publicar-pasta <saida> [projeto ...]` (projeto, relações e seções; seção igual não cria versão).
5. `bash $KB sync` e confira `bash $KB show <projeto>`.

Passos avulsos: `mapear.py fatos <repo>` (JSON do repo), `mapear.py relacoes <pasta-de-fatos>`,
`mapear.py secoes <fatos.json> <relacoes.json> <saida>`.

## Mapear um projeto que não é repositório revamp (legado, apps, API de integrações)
1. `bash $ARCH list` — o que já existe. Chave = nome do repo em minúsculas (`edv-solvace`, `edv-solvace-apps`).
2. Estrutura primeiro (barato): árvore até 2–3 níveis, `README`/`CLAUDE.md`, soluções/projetos, `appsettings*.json`
   (só NOMES de chaves), Dockerfile/pipelines, migrações/DDL.
3. Depois o código dos pontos de entrada (controllers, telas, jobs) e das entidades principais.
4. Regras de negócio: `bash $KC search <modulo/tela/entidade>` — cite os ART-n na seção `regras-de-negocio`.
5. Escreva cada seção num arquivo em `$CARD_TMP/<chave>/` e publique:
   `bash $ARCH project <chave> --name "..." --kind legacy --repo <url> --summary-file resumo.md --keywords "a,b" --repo-dir <pasta> [--relations-file rel.json]`
   `bash $ARCH section <chave> visao-geral visao-geral.md --title "Visão geral" --order 10 --note "mapeamento inicial"`
   Relações: `[{"target":"revamp-users","kind":"event|queue|database|http|package|external|frontend|other","detail":"...","evidence":"arquivo:linha"}]`.

## Legado por módulo (`legado-*`, 0045)
O monólito `edv-solvace` é mapeado por módulo de negócio: projeto `legado-<modulo>` (kind `legacy`, `repoDir`
`edv-solvace`, `businessArea` igual ao do `revamp-<modulo>`, relação `kind: other` "versão nova (revamp) do mesmo
módulo" — é ela que faz o `kb.sh index` mostrar o par) com `020-modulos` (Onde está · Telas → arquivos · Fluxos · Como
achar rápido), `030-dados` e, se houver, `090-armadilhas`. O glossário sigla/pasta → projeto fica em
`edv-solvace/020-modulos`. Gerar em massa: subagentes aqui no Claude Code (um por grupo de módulos), cada um gravando
`<pasta>/<chave>/projeto.json` + `NNN-secao.md`; revisar e `bash $ARCH publicar-pasta <pasta>`. Nomes de tela: o
catálogo `operacao-plataforma/090-catalogo-modulos` e a `085-operacao` do revamp equivalente (telas da versão legada).

## Atualizar (incremental)
1. `bash $ARCH stale <chave> <pasta-do-repo>` — commits e pastas alteradas desde o commit mapeado.
2. Revamp: `git pull` no kb-mirror e rode o passo 2–4 do parque inteiro (idempotente).
3. Outros: releia só o que mudou; reescreva só as seções afetadas (`--note "atualizado até <commit>: ..."`) e regrave o
   projeto com `--repo-dir` para o commit novo valer.
4. Artigos do KC novos/alterados: `bash $KC sync` e revise a seção `regras-de-negocio` dos módulos citados.

## Regras
- Nunca credenciais/connection strings/dados de cliente. Só nomes de recursos e chaves (o extrator pula chaves de segredo).
- Nada inventado: o que não foi verificado vai como "a confirmar".
- Escrita só com api-key de admin; seção igual à publicada não cria versão.
