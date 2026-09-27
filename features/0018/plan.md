# Feature 0018 — Cold start, migrações no pipeline, limpeza de imagens e bloqueio de robôs

> Origem: análise de custos/uso de 2026-09-27 (pedido do usuário). Branch `feature/0018` no backend (`../prform.api-0018`) e no front (`../solvace.prform.web/prform-app-0018`).

## Motivação (dados de 26–27/09)
- **Cold start**: ~8–11 s na API principal e ~7 s na auth (mínimo 0 instâncias). O startup faz as migrações (3 contextos + lock no banco remoto) antes de abrir a porta, e o JIT compila tudo na hora.
- **Artifact Registry** cresce a cada deploy (46 imagens, 0,95 GB).
- **Robôs** da AWS us-west-2 (Oregon) carregam o front várias vezes por dia (07–11 h UTC), com IPs que se revezam e user-agents de Windows/Mac/iPhone. Só o front: nunca chamam a API/auth. O usuário não reconhece.

## Fases
- **B1 — ReadyToRun**: publish com `-p:PublishReadyToRun=true` e RID do alvo (`linux-x64` no CI; `TARGETARCH` do Docker) nas duas APIs → menos JIT no startup.
- **B2 — Migrações fora do startup**: modo `--migrate` nas duas APIs (auth: migrações + seed, dentro do lock), que roda e sai; a API não migra mais ao subir. No pipeline, antes do `gcloud run deploy`, um **Cloud Run Job** (`<serviço>-migrate`) com a mesma imagem, a conta de runtime e o secret JSON montado executa `--migrate` (`--wait`). Falha → o deploy não acontece (mesma garantia de antes).
- **I1 — Limpeza do Artifact Registry**: política que mantém as **5 versões mais recentes** de cada imagem e apaga o resto (repo `cime` no Terraform do backend; repo `web` no Terraform do front). Rollback do Cloud Run passa a alcançar só os 5 últimos deploys.
- **F1 — Robôs no front**: `robots.txt` negando tudo + `X-Robots-Tag: noindex`; nginx com o IP real do cliente (último item do `X-Forwarded-For`, que o Google acrescenta) e `deny` para as faixas **EC2 da AWS em us-west-2**, geradas no build a partir do `ip-ranges.json` oficial. O bloqueio devolve 403 (a requisição ainda chega ao Cloud Run; economiza egress). Cloud Armor barraria antes, mas custa ~US$ 18/mês.
- **T1 — Testes locais**: imagem com R2R sobe e responde; `--migrate` migra um banco vazio e sai; API sobe sem migrar; nginx bloqueia uma faixa simulada e libera o resto; `robots.txt`.
- **Q1 — Deploy e medição**: comparar os cold starts (métrica `startup_latencies`) antes e depois.

## Status (2026-09-27)
| Fase | Status | Notas |
|---|---|---|
| B1 ReadyToRun | ✅ | O restore também precisa de `-p:PublishReadyToRun=true` (senão NETSDK1094: sem o crossgen do RID). Imagens +34 MB. |
| B2 Migrações no pipeline | ✅ | `--migrate` nas duas APIs; job `<serviço>-migrate` no `deploy.yml` antes do deploy. **Achado**: com exceção não tratada o processo .NET ficou vivo (estado R) em vez de sair → o modo `--migrate` captura, loga como crítico e faz `Environment.Exit(1)` (sai em 1 s com o banco fora do ar). Banco vazio: auth e API migram e saem 0 (4 schemas + papéis). |
| I1 Limpeza do AR | ✅ aplicada (repos `cime` e `web`) | Terraform nos dois repos: KEEP 5 mais recentes + DELETE o resto. Planos: só isso + o ajuste cosmético do `scaling`. |
| F1 Robôs | ✅ | 164 faixas EC2 us-west-2 no build; os **20/20** IPs de robô vistos nos logs estão cobertos. Teste local do nginx: robô 403 (página e assets), usuário 200, header forjado não burla (vale o último IP), `robots.txt` + `X-Robots-Tag`. Produção: o Cloud Run acrescenta o IP real no fim do `X-Forwarded-For` (header forjado com IP de robô → 200). |
| T1 Startup | ✅ | Local (banco na mesma máquina): 1ª resposta **1,58–2,11 s → 0,86–0,99 s** (~45%). Em produção, a checagem de migração falava com o banco remoto via TLS; o ganho deve ser maior. |
| Q1 Deploy + medição | ✅ | 2026-09-27: backend PR #25 (jobs `cime-pullrequest-migrate`/`cime-auth-migrate` ok, no-op) → `cime-auth-00028-q59`, `cime-pullrequest-00030-psb`; front PR #15 → `cime-web-00016-9x7`. Rollback: `cime-auth-00027-mg9`, `cime-pullrequest-00029-f24`, `cime-web-00015-k5j`. |

## Q1 — medição em produção (`startup_latencies`, média por revisão)
| Serviço | Antes (rev. anterior) | Depois (0018) |
|---|---|---|
| cime-pullrequest | 8,8 s (00029, 24 starts); 10–17 s nas revisões 00019–00028 | **5,0 s** (1 start) |
| cime-auth | 6,8 s (00027, 17 starts); 6–11 s nas revisões 00017–00026 | **2,9 s** (1 start) |

Amostra da 0018 ainda de 1 start por serviço (o deploy); reconferir com mais cold starts.

Pendências de acompanhamento:
- 403 dos robôs no `cime-web`: ainda nenhum (janela deles 07–11 UTC; deploy às ~22 UTC de 27/09).
- Limpeza do AR é assíncrona (até ~1 dia): em 27/09 23:45 UTC o repo `cime` ainda tinha 24+24 imagens (1002 MB) e o `web` 15 (39 MB).
