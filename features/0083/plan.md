# Plano — Feature 0083

Back (`prform.api`, branch `feature/0083`): só a migração de rótulos/ajudas. Front (`prform-app`, branch `feature/0083`):
o guia, o passo a passo nas integrações e o menu.

| Fase | Descrição | Depende |
|---|---|---|
| B1 | Migração `SeedPersonalFieldHelp0083` (DefaultContext): `Label`/`Help` dos campos pessoais (PAT, Token, ApiKey, WorkspaceId, WebhookUrl, campos do DevOps) — só onde falta, preservando o admin | — |
| F1 | `setup-guide/integration-guides.ts`: passo a passo por integração (links montados dos campos fixos do plugin) | — |
| F2 | `SetupGuideService` (status: integrações, executores/doctor, marcação manual; primeiro acesso por usuário/versão) + `SetupGuideDialogComponent` ("Primeiros passos") | F1 |
| F3 | "Minhas integrações": `focus` na integração pedida + bloco "Como obter" por integração | F1 |
| F4 | Menu do usuário ("Primeiros passos" + progresso, `data-tour="user-menu"`), auto-abertura no primeiro acesso, balão do tour ao fechar | F2 |
| Q1 | Build (API + front) + e2e local com capturas | B1, F1–F4 |
