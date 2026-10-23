# Status — Feature 0072

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B1 | ✅ | Claude | ver log |
| B2 | ✅ | Claude | ver log |
| B3 | ✅ | Claude | ver log |
| F1 | ✅ | Claude | front `feature/0072` |
| F2 | ✅ | Claude | front `feature/0072` |
| F3 | ✅ | Claude | front `feature/0072` |
| Q1 | ⏳ | Claude | |

## Notas do back
- Contrato: `POST Architecture/ask` aceita `attachments: [{name, mediaType, data(base64 ou data URL)}]`; resposta ganha `attachmentSeen` (o que a IA entendeu) e `attachmentNotes` (avisos); `GET Architecture/ask/status` ganha `supportsAttachments` e `limits` (a tela não repete os números).
- Recusa de anexo / provedor sem suporte = 400 `{error}` (DomainException), motivo na tela. (A spec dizia 409; ficou 400 como as demais regras.)
- Tipo conferido pelo conteúdo (PNG/JPEG/GIF/WebP/PDF); texto por tipo ou extensão; binário recusado; texto > 100 KB cortado com aviso.
- Plano da IA recebe os anexos e devolve `attachments` (≤ 900 caracteres: erro literal, tela, módulo); a resposta é só texto com esse resumo + arquivos de texto (começo e fim). Sem cache com anexo. Nada gravado; log só com quantidade e KB.
- JSON dos blocos (imagem/PDF/texto) conferido contra o SDK. 195 testes do Knowledge passam.

## Notas do front
- `kb-ask-attach.ts`: `prepareAttachments` (confere tipo/tamanho/quantidade pelos limites da API, reduz imagem para lado maior ≤ 1.500 px em JPEG, corta texto, base64), `KbAttachDropDirective` (colar e arrastar/soltar no hero e na página da pergunta), `KbAttachToolsComponent` (clipe e microfone), `KbAttachChipsComponent` (miniaturas, avisos), `Dictation` (Web Speech API pt-BR).
- Recurso só aparece quando `ask/status.supportsAttachments`; o microfone aparece em navegador com ditado (Chrome/Edge), independente do provedor.
- Anexos valem para UMA pergunta (soltos depois do envio); a tela guarda só nome/tipo e mostra "O que a IA entendeu do anexo".
- Só anexo, sem texto: pergunta padrão "O que é isso e como resolvo?" (a mesma da API).
- 8 specs (`kb-ask-attach.spec.ts`) passam no Karma/ChromeHeadless com tsconfig temporário (os specs antigos do repo não compilam: `async` removido do Angular).
- NÃO testado no navegador de ponta a ponta (só build + specs): conferir na Q1.

## Log
- 2026-10-23 — spec/plano; worktrees a partir da master (back 70942f7, front 760d518). #130 (0071) mesclado e em produção.
- 2026-10-23 — B1–B3 prontos (ClaudeService com blocos de conteúdo, AskAttachmentRules, prompts, endpoint e status).
- 2026-10-23 — F1–F3 prontos no front; PRs abertos (back e front). Falta Q1 (deploy + 2 perguntas reais).
