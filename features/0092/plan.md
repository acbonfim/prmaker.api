# 0092 — Plano

Branch `feature/0092` nos dois repos (worktrees `prform.api-0092` e `prform-app-0092`). Contrato em `contract.md`.

| Fase | O quê | Depende |
|---|---|---|
| B1 | `ValidationEnvironments` (chave + catálogo de configurações), `WorkTaskKind.EnvValidation`, `WorkItemType.Validation`, `WorkItemFileKind.Evidence`; renderizador: `repeat`/`{each}`/`{eachLabel}`, opções `{validationEnvironments}`, input `environments`; migração semeando chave, tipo de sub-tarefa, tipo de item e ferramenta `validar-em-ambiente`; MCP `prmake_env_validation` (+ liberado ao especialista); testes | — |
| B2 | Skills: `prmake-skills.sh env-credentials` (+ doctor); skill nova `validar-ambiente` (env.sh + env_browser.py, venv Playwright, guarda de somente leitura); `executar-item-de-trabalho` (guia do `env-validation`); `especialista-do-caso` (tool + `/validar-em-ambiente`); `analisar-bug`/`analisar-us` (item de validação padrão na entrega) | B1 |
| F1 | Input `environments` (chips + outro), `repeat` na prévia das ferramentas, grupo/ícone da ferramenta, tipo `evidence` nos arquivos, galeria de prints na sub-tarefa | contrato |
| Q1 | Build/test dos dois lados, PRs, merge juntos e deploy | todas |
