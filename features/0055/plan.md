# Plano — Feature 0055 (de onde a análise leu)

Branch `feature/0055` em `prform.api-0055` (API, skill, executor) e `prform-app-0055` (front).

| Fase | O quê | Depende |
|---|---|---|
| S1 | `usage_scan.py` (leitor do transcript fora do heredoc): origens `re`/`base`/`code-confirm`/`code-explore`/`code-search`, tokens estimados por resultado, arquivos explorados; `prmake-plan.sh usage` usa e resume a leitura; testes em `tools/SkillTests` | — |
| E1 | Executor (`TranscriptUsage.cs`) com as mesmas regras; `SessionUsage.Sources/ExploredFiles`; versão 1.0.10 (autoatualiza); paridade Python × C# nos mesmos transcripts | S1 |
| B1 | `ExecutionReadSource`/`ExecutionExploredFile` no JSON da sessão (com linha de base análise → correção); request/`RecordUsage`; `Usage.Sources/ReverseShare/ExploredFiles` no plano; relatório com `ReadPlans/AvgReverseShare/Sources`; migração só de modelo; projeto de testes `solvace.executionplans.tests` | — |
| F1 | Popover do consumo: tabela "De onde leu" (chamadas, tokens, %), arquivos explorados (lacuna?), "% lido da ER"; plano usa; executores com a coluna "Lido da ER" e a média por origem no popover do total | B1 |
| T1 | Harness: plano real, `usage` pelo script com sessão sintética, API (plano e relatório) e o popover na tela | todas |
