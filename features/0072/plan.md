# Plano — 0072

| Fase | Repo | O quê | Depende |
|---|---|---|---|
| B1 | back | `AIAttachment` + `IAIService.GenerateContentAsync(prompt, attachments, ct)` (padrão: erro "provedor não suporta anexos"); `ClaudeService` monta blocos de conteúdo (image/document/text); `PluginAIService` repassa; `SupportsAttachments` por provedor | — |
| B2 | back | Pergunte: `POST Architecture/ask` aceita anexos (JSON com base64), validação (tipo, tamanho, quantidade), plano com anexos devolve `descricao`, busca usa pergunta + descrição, resposta só texto, sem cache, nada gravado; `ask/status.supportsAttachments`; log sem conteúdo | B1 |
| B3 | back | Testes: validação, prompt do plano com descrição, parse do plano com `descricao`, sem cache com anexo, provedor sem suporte | B2 |
| F1 | front | `ArchitectureService.ask(question, attachments)`; modelo de anexo; redimensionar imagem (canvas) e ler texto/PDF como base64 | B2 (contrato) |
| F2 | front | Campo do Pergunte (barra e herói): arrastar/soltar, colar (`paste`), botão de anexo, miniaturas removíveis, limites e mensagens | F1 |
| F3 | front | Ditado por voz (Web Speech API pt-BR), botão de microfone, esconde sem suporte do navegador | F2 |
| Q1 | ambos | Deploy; 1 pergunta com print real e 1 com `.log` (custa API — só avulsas) | B3, F3 |

Ondas: B1→B2→B3 em série; F1 pode começar com o contrato combinado enquanto B2 anda; F3 é independente de B*.
Branches `feature/0072` nos dois repos (worktrees `prform.api-0072`, `prform-app-0072`; `node_modules` do front → `prform-app-0019`).
