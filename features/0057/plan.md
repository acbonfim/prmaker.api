# Plano — Feature 0057

Branch `feature/0057` em `prform.api-0057` (executor) e `prform-app-0057` (front).

| Fase | O quê | Depende |
|---|---|---|
| E1 | `Runner.cs`: `NextAsync(0)` + `NextPollEvery` = 10 s quando a fila está vazia; versão 1.0.11 | — |
| F1 | `re-revision.component.ts`: `untracked(() => this.load(id))` no effect | — |
| T1 | Medir depois do deploy: `billable_instance_time` da `cime-pullrequest` e GETs de `revisions/{id}` nos logs | E1, F1 |
