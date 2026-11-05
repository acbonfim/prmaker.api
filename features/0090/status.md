# 0090 — Status

| Fase | Status | Commits | Notas |
|---|---|---|---|
| B1 `/clear` | concluída | back 8ef2a56 | `CaseChat.ClearedAt/ClearedBy` (migração `CaseChatClear0090`), `POST CaseChat/{card}/clear`; sessão de pedido anterior à limpeza não volta |
| B2 cancelar item pela conversa | concluída | back 8ef2a56 | `POST CaseChat/{card}/work-items/{n}/cancel` — só item do card; mensagem do sistema |
| B3 testes + skill | concluída | back 8ef2a56 | `CaseChatClear0090Tests` (5); skill `especialista-do-caso`: `#123` = item, comandos da tela |
| F1 `#nn` link + popover | concluída | front 99d9663 | `workRefs` no markdown, `planMarkdown:'case'`, `WorkItemPopoverComponent` na `appCodeRefHost` |
| F2 lógica do compositor | concluída | front 99d9663 | `composer-suggest.ts` + spec |
| F3 compositores | concluída | front 99d9663 | `CaseComposerService`/`CaseComposer`, `app-composer-suggest` no modal e na compacta |
| Q1 integração | concluída | — | ver abaixo |

## Q1 (2026-11-05)
- `dotnet test` (CaseChat*): 18 ok. `ng build` dev ok. Specs (tsconfig temporário só com os specs da área — o `ng test`
  geral não compila por specs antigos com `async`): 90 ok.
- e2e local (`.t0090`, Postgres `cime-pg-0090` = cópia do banco da 0085 + migração; scripts `e2e/c90.mjs`, `c90b.mjs`):
  `/` lista comandos + ferramentas e filtra; `#4` sugere #48/#46/#42; `#99999` "não existe"; mensagem enviada vira
  links `#wi/48`, `#wi/99999`; popover do #48 com sub-tarefas e "Abrir na Bancada"; `/cancelar-item-de-trabalho #63
  motivo` → diálogo (desfazer desmarcado: nada feito) → item `cancelled` + mensagem do sistema; `#5` (cancelado sem nada
  feito) → aviso; `/xyz` → aviso; `/clear` → confirmação → mensagens com `DeletedAt`, `SessionId` nulo, só a mensagem do
  sistema; `/pr-para-branch` na compacta abre a conversa grande no formulário da ferramenta.

## Log
- 2026-11-05 — spec/plano, B1–B3, F1–F3, Q1. Usuário pediu PR + merge automático dos dois ao final.
