# Status 0095

- Branch: `feature/0095` (prform.api e prform-app), a partir de origin/master.
- Merge autorizado pelo usuário (2026-11-12, ausente): abrir PR nos dois repos e **mesclar os dois juntos** no fim.

| Fase | Estado | Notas |
|---|---|---|
| S1 — skill | ✅ | testado com sessão real no sandboxefeso (login, `go /incident/`, sessão reaproveitada troca card/task) |
| E1 — executor 1.4.0 | ✅ | testado com Chrome real: 403 sem origem/origem errada/token errado/Host forjado; preflight com Allow-Private-Network; MJPEG com 2 espectadores; conexão CDP fecha sem espectador |
| B1 — API `mine` | ✅ | 19 testes do "ao vivo" (1 novo) |
| F1 — front | ✅ | build prod ok; 90 specs de `work-items` (6 novos); Chrome 157: página https pública lê 127.0.0.1 só com a permissão de rede local concedida (sem ela `Failed to fetch`) |

## Log

- 2026-11-12 — implementado e testado; PRs abertos e mesclados juntos.

## Pendente

- Primeiro uso real: rodar uma sub-tarefa `env-reproduction`/`env-validation` pelo executor 1.4.0 e abrir "Ver tela ao vivo"
  (o Chrome pede a permissão de rede local uma vez).
