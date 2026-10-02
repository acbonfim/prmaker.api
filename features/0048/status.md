# Status — Feature 0048

Branch `feature/0048` em `prform.api-0048` (API, ferramenta das skills, skills, executor) e `prform-app-0048` (front).

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B1 | ✅ concluída | Claude | `e25070a` |
| S1 | ✅ concluída | Claude | `66ea035` |
| E1 | ✅ concluída (executor 1.0.7) | Claude | `dbbc680` |
| F1 | ✅ concluída | Claude | front `546bd0f` |
| S2 | ✅ concluída | Claude | `6db2101` |
| S3 | ✅ concluída | Claude | `d6e3957` |
| T1 | ✅ concluída (exceto Windows real — pendente) | Claude | — |

## Notas de handoff
- Desvios do plano (spec/plan atualizados): `agent install` não grava `--workspace` (o executor calcula pelo mapa a
  cada execução); repositório dentro de repositório conta (o `revamp_separado` do autor é um repositório com os módulos
  dentro); `missing` fica gravado no `repos.json`; macOS sem `Documents`/`Desktop`/`Downloads` na busca padrão (TCC).
- `BranchStrategy` reescrito pela migração B1 volta como `jsonb_pretty` (chaves reordenadas pelo Postgres, mesmo
  conteúdo) — só quando a migração muda algo.
- Windows: `repos scan`/`path` não foram rodados num Git Bash real (caminhos gravados com `cygpath -m`; executor acha o
  bash do Git por `CLAUDE_CODE_GIT_BASH_PATH`/Program Files, nunca o do WSL).

## Log
- 2026-10-02 — spec, plano e status criados. Decisões: regra própria `edv-solvace-api` → `integration-api` (base
  perguntada: `master`/`release-version`); sem confirmação obrigatória dos repositórios achados pela busca.
- 2026-10-02 — B1: migração `SeedIntegrationApiBranchStrategy` (bloco `DO`, só o que falta, JSON inválido intocado).
  Testada no Postgres isolado (55448): seed → regra logo após `edv-solvace-apps` + fluxos; config editada pelo admin
  (regra `EDV-solvace-api` própria, `producao.integration-api` próprio) preservada; `Down` remove só o semeado.
- 2026-10-02 — S1: `prmake-skills.sh repos` (show/scan/set/unset/path/summary), migração `.repos-v1` em segundo plano
  no `update`, aviso de pendência só quando muda, `install`/`agent install` com a busca, `status`/`doctor` com o resumo;
  trava da ferramenta só é solta por quem a criou. Testes (home falsa + API falsa): ambíguo, worktree, kb-mirror,
  node_modules, profundidade, Documents (mac), manual preservado, pasta movida, variável por cima, exit 0/2/3, sem rede
  não marca `.repos-v1`, interativo (pty) escolhe ambíguo e valida o remote do que faltava. Na máquina do autor: 17
  repositórios em 2 s, ambíguos reais `edv-solvace-api` (`core` × `edv-solvace-api`) e `revamp-Users`
  (`revamp_separado` × `revamp_separado/revamp-Users`).
- 2026-10-02 — E1: `RepoMap` (JsonDocument, sem reflexão — binário trimmed), workspace = pasta comum do mapa (nunca a
  home), `--add-dir` dos repositórios fora dele, busca na partida se faltar o mapa, `repoMap` no report (até 150 itens;
  limite de 64 KB do PRMake), doctor "Mapa de repositórios", janitor dos worktrees ao lado dos repositórios do mapa,
  `Shell.Bash()` (Git Bash no Windows). Teste com a API local + Claude falso: executor 1.0.7 montou o mapa sozinho,
  pedido do card 70048 rodou em `~/repos/solvace` com `--add-dir ~/edv-solvace-apps` e `~/dev/l1/l2/l3/ok4`;
  `repoMap` e o doctor chegaram em `ExecutionWorker/mine`.
- 2026-10-02 — F1: "Meus executores" com "Repositórios (N)" (ambíguos, sem clone, pasta que sumiu, comando copiável;
  caminho cortado à esquerda com `<bdi>`). Conferido no Chrome headless (1400 px e 420 px).
- 2026-10-02 — S2/S3: `revamp-repos.sh` pelo mapa (where com exit 2/3, grep por tipo — `integration-api` conta como
  legado), `branches` imprime `pasta=` e sai com 4 se fora do mapa/ambíguo, `worktree`/`arch.sh stale` aceitam o nome
  do repositório; textos da `analisar-bug` (pergunta e `repos set`, nunca seguir sem o código) e README.
- 2026-10-02 — T1: instalação completa pelo `install.sh` da API local numa home nova → skills + hook + mapa (6
  repositórios, 1 ambíguo) + `.repos-v1`, sem perguntar.
- 2026-10-02 — PRs abertos (sem merge): back acbonfim/prmaker.api#78, front acbonfim/prmakerweb#45.
