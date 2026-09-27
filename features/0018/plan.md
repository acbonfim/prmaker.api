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
