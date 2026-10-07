# Status — Feature 0069

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| S1 | ✅ | Claude | 6d19648 (PR #114) |
| S2 | ✅ | Claude | 6d19648 (PR #114) |
| T1 | ✅ | Claude | 6d19648 (PR #114) |
| B2 | ✅ | Claude | e6fc857 (PR #115) |
| B3 | ✅ | Claude | e6fc857 (PR #115) |
| F1 | ✅ | Claude | front 07fbe6f (prmakerweb #61) |
| B2b | ⏳ depois da 0068 | — | — |
| B1 | ⏳ depois da 0068 | — | — |

## Notas
- Origem: cards 75349 e 75353 (06/10/2026), análise dos transcripts do executor.
- S2 conferido na máquina real: `pools sandboxefeso` 6 s (lista e grava o cache), `pool-id sandboxefeso` em seguida 0,02 s.
- T1: `python3 -m unittest discover -s tools/SkillTests` → 42 testes OK (9 novos: `test_test_changed.py`, `test_cognito_cache.py`).
- `test-changed.sh` num worktree real do edv-solvace-apps (origin/master, mudança em `auth.service.ts` e
  `user-new.component.ts`): 2 specs, 23 testes, **8 s**. No executor do 75349 os mesmos specs levaram 121–215 s.
- Medição (06/10, `user-new.component.spec.ts`, worktree novo): padrão frio 24 s / quente 5 s; ts-jest com
  `isolatedModules` frio 18 s / quente 4 s → não compensa mexer na config do repositório.
- A correção do 75349 terminou em 64 min (16:50→17:55), ~55 min de jest: master ~20 min, development ~23 min (pasta
  `user-locked` inteira, 9 suítes; `user-rejected.spec` 715 s), qa ~11 min. Os PRs foram só para development (#13612) e
  qa (#13613) — os dois rodam build + jest no CI (`pr-build-validation.yml`).
- Máquina: 16 GB, ~10 GB de swap em uso; cada worker do jest até 2 GB, junto com outras sessões do Claude, Rider,
  Chrome e Teams — provável motivo de 24 s virarem 141–715 s (hipótese, não reproduzida).
- B1 só depois da 0068 na master.
- B2/B3: build ok; migrações `PullRequestChecks` (execution) e `SeedCorrectionTests` (prform) aplicadas do zero num
  Postgres 18 isolado (porta 55469) — seed `changed`/`420` sem apagar as chaves existentes.
- Regra das falhas da base simulada com `gh api` nos PRs reais do 75349: #13612 (development) falha só `users`, que a
  base já tem → pendente, sem retomar; #13613 (qa) 5 falhas, todas da base → idem.
- F1: `ng build --configuration development` ok. Não testado ponta a ponta (sincronização com GitHub falso + tela no
  navegador) — fica para o teste depois do deploy num card real com PR aberto.
- SkillTests: 44 OK (`prmake-plan.sh test`: off e limite configurado).
