# Feature 0076 — Evidências do card lidas e guardadas no tratamento

> Spec: [`spec.md`](./spec.md) · Status: [`status.md`](./status.md) · Branch `feature/0076` (worktree `../prform.api-0076`).
> Só backend + skills (o front já mostra os arquivos do plano no diálogo de arquivos e as imagens com `anexo:`).

## 1. O que existe e vamos reusar
- `AzureService.GetCardFullAsync` já pede `$expand=all` (as `relations` vêm e são ignoradas) e lê os comentários
  (`/comments?api-version=7.1-preview.4`); `CreateClient()` usa o PAT da integração efetiva de quem chama.
- `ExecutionArtifact` (+ conteúdo `bytea`), `GetArtifactFileAsync`, `GetCardArtifactsAsync` (0075), `prmake_attachment`
  (imagem volta como imagem), `prmake-plan.sh attachment` (baixa e o Claude abre com Read — PDF incluso).
- Portões no `advance`/`PATCH step` (0063 engenharia reversa, 0075 citações) — mesmo padrão para as evidências.

## 2. Fases
**B1 — Azure: evidências e download** · `IAzureService.GetCardEvidenceAsync(id)` → relações `AttachedFile` (nome,
tamanho, autor, data, comentário) + `<img>`/`<a>` para `/_apis/wit/attachments/{guid}` nos campos HTML e nos comentários
(origem, autor/data do comentário), sem repetir, na ordem descrição → campos → discussion → anexos; `DownloadAttachmentAsync(url,
maxBytes)` só para o host da organização configurada em `/_apis/wit/attachments/`, com limite. Testes do parser (HTML real).

**B2 — Plano: importar, marcar leitura, portão** · `ExecutionArtifact.ExternalKey` (`devops:<guid>`) e `ReadAt` (executor
abriu o conteúdo); `ExecutionPlan.EvidenceCheckedAt/EvidenceError/EvidenceSkipReason`. `POST {id}/evidence/import`
(idempotente pela chave no card inteiro; nomes `evNN-<origem>-<arquivo>`; limites 10 MB/50 MB → "não importada"),
`GET card/{card}/evidence` (lista com importada/lida), `POST {id}/evidence/skip` (seguir sem, com motivo do usuário).
Leitura marcada no `GET content` com `X-Execution-Client: skill` e no `prmake_attachment`. Portão `EvidenceGateSteps`
(seed `montar-analise`): sem importação feita (nem motivo para pular) ou com evidência legível não aberta → recusa com a
lista. MCP `prmake_evidence(card, skip?)`. Migrações (colunas anuláveis) + seed. Testes.

**S1 — Skill** · `bug-fetch.sh`: discussion em `dados/discussion.txt` (autor, data, texto; `[imagem]` onde havia) e
marcação `[imagem]` nos repro steps; `contexto`/`contexto-correcao`: importa e lista as evidências + mostra a discussion;
`prmake-plan.sh evidencias <card> [--skip "motivo"]`; `evidence-text.py` (docx/xlsx/pptx/eml → texto, sem dependência);
regra e seção "Evidências do card" no modelo da análise; consultas.md (ler todas antes da causa raiz).

**Q1** — testes + e2e local (Postgres isolado; DevOps falso servindo um work item com imagem no repro, comentário com
imagem e anexo `.txt`). **Q2** — PR, merge, deploy (autorizado pelo usuário).
