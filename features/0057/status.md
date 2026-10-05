# Status — Feature 0057

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| E1 | ✅ implementada (build ok) | Claude | ver PR |
| F1 | ✅ implementada (`ng build` ok) | Claude | front, ver PR |
| T1 | ⏳ depois do deploy | — | — |

## Notas
- Executores antigos (1.0.8/1.0.10) continuam em long-poll até autoatualizar para a 1.0.11 (a imagem nova publica o agente).
- Pós-deploy esperado: `cime-pullrequest` < ~2 h/dia cobradas com executores ligados; sem GETs repetidos de `revisions/{id}`.
- O servidor mantém o suporte a `wait` > 0 (compatibilidade); não foi alterado.
