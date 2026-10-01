# Status — Feature 0032

| Fase | Descrição | Status |
|---|---|---|
| B1 | Upload sem duplicar anexo; Timeline das perguntas com o texto das opções | ✅ concluída |
| B2 | `Suggestions`/`Help` em FieldSettings e em Minhas integrações | ✅ concluída |
| S1 | Skill: sync/pull/ask + SKILL.md | ✅ concluída |
| F1 | Plano: esconder cópias, opções visíveis; placeholder da Timeline | ✅ concluída |
| F2 | Minhas integrações (pick list + layout) | ✅ concluída |
| F3 | Editor de plugins (admin) | ✅ concluída |
| Q1 | Builds, teste local, PRs back + front | ✅ concluída (PRs #46 back e #27 front mesclados em 2026-09-30) |

## Notas
- Dados do card 74775: as 6 cópias (#11–#13 análise, #28–#30 correção) foram apagadas em produção em 2026-09-30,
  com autorização do usuário (DELETE do arquivo no plano); ficaram só os anexos #5–#7.

## Teste local (2026-09-30, `.t0032/`: Postgres isolado 55434, auth falso, API 5083, front 4200, Chrome headless)
- Upload da skill com o conteúdo de um anexo de comentário devolve o anexo (sem arquivo novo); `sync` envia 0,
  `pull` não traz anexo de comentário; cópia legada fica oculta na tela (Anexos = 1).
- Timeline: pergunta e resposta com o texto das opções; opções visíveis no plano; placeholder curto no celular.
- Editor de plugins: salvar o "Skills Configurations" sem mexer deixa o plugin idêntico (diff vazio); sugestão nova
  gravada em `fieldSettings`, `personalFields` e campo oculto preservados.
- Minhas integrações: pick list com valor global + sugestões (segredo sem sugestão), ajuda por campo, fixos recolhidos.
