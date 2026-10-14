# Feature 0069 — Plano de execução

> Spec: [`spec.md`](./spec.md) · Status/controle: [`status.md`](./status.md)
> Branches: `feature/0069` (S1/S2/T1, PR #114) e `feature/0069-ci` (B2/B3/F1, a partir dela) no `prform.api`;
> `feature/0069-ci` no `prform-app` (worktree `prform-app-0069`, `node_modules` do `prform-app-0019`).
> A 0068 (outro agente, `prform.api-0068`) mexe no executor e no `ExecutionQueueApplication.cs` — S1/S2 não tocam
> nesses arquivos; B1 espera a 0068 entrar na master.

| Fase | O quê | Arquivos | Depende de |
|---|---|---|---|
| S1 | Testes da correção: `test-changed.sh`, `node_modules` no worktree, regra no passo 8 | `skills/analisar-bug/scripts/test-changed.sh`, `prmake-plan.sh` (`worktree`), `references/correcao.md`, `references/catalogo-e-fechamento.md` | — |
| S2 | Cognito: cache da lista de pools + timeouts do AWS CLI | `skills/analisar-bug/scripts/cognito-query.sh`, `references/consultas.md` | — |
| T1 | Testes: `tools/SkillTests/test_test_changed.py` (specs escolhidos, limite mata o jest, sem runner, link do `node_modules` removido sem apagar o alvo) e `test_cognito_cache.py` (aws falso: cache, relista quando não acha, `--refresh`) | `tools/SkillTests/` | S1, S2 |
| B2 | CI dos PRs: GitHub (`GetPullRequestChecksAsync` + falhas da base), link com `Checks*` (migração `PullRequestChecks`), sincronização → marco + retomada `checks`, MCP `prmake_plan` | `Solvace.GitHub`, `Solvace.ExecutionPlans`, `ExecutionPlanIntegrations.cs`, `PrmakeMcpTools.cs` | — |
| B3 | `CorrectionLocalTests`/`CorrectionTestMaxSeconds` (migração `SeedCorrectionTests`) + `prmake-plan.sh test` + docs da falha de CI | `SkillsConfigurationKeys.cs`, `prform.infra/Migrations`, skill | S1 |
| F1 | Tela: CI no PR da etapa (badge + checks que falharam, "já falhava na base") | `prform-app` `execution-plan.*` | B2 |
| B2b | Ler o CI dos planos aguardando PR sem ninguém abrir o card (sinal de vida do executor) | `ExecutionQueueApplication.cs` | 0068 mesclada |
| B1 | Retomada da análise depois do `propor-solucoes` numa sessão nova (prompt com `contexto-correcao`/checkpoint, como a 0049) | `ExecutionQueueApplication.cs` (`PhaseOfAsync`, `FreshPrompt`) | 0068 mesclada |

## Desenho

- `test-changed.sh <worktree> [<base-ref>] [--max-seconds N]`: base = upstream da branch (o worktree nasce de
  `origin/<base>` com upstream) ou `origin/master`; arquivos = `git diff <base>...HEAD` + não commitados + não
  rastreados. Jest quando há `node_modules/jest/bin/jest.js`: `--runTestsByPath` com os specs, `--coverage=false
  --maxWorkers=2`; limite por um watchdog em `python3` (grupo de processos; `taskkill /T` no Windows). Saída curta
  (PASS/FAIL/resumo; falha → as últimas 80 linhas) e o log inteiro num arquivo temporário. Exit: 0 passou ou sem spec,
  1 falhou, 124 lento, 3 sem runner.
- `worktree`: depois de criar/reaproveitar, `ln -s <clone>/node_modules <wt>/node_modules` quando o clone tem
  `package.json` + `node_modules`, o worktree não tem e `git check-ignore node_modules` confirma. Windows/Git Bash: não
  liga (o `ln -s` copiaria a pasta) — o script de teste avisa.
