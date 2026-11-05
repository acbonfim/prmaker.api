# 0090 — Plano

Branch `feature/0090` nos dois repos (worktrees `prform.api-0090` e `prform-app-0090`).

| Fase | O quê | Depende |
|---|---|---|
| B1 | `CaseChat.ClearedAt/ClearedBy` + migração `CaseChatClear0090`; `CaseChatApplication.ClearAsync` (para a resposta, `DeletedAt` em massa, propostas/ações pendentes, sessão do especialista zerada, filtro por data em arquivos/pontos/propostas/ações na tela e no contexto, mensagem do sistema, tempo real `cleared`); `POST CaseChat/{card}/clear` | — |
| B2 | `CaseChatApplication.CancelWorkItemAsync` (só item do card; cancela com/sem desfazer ou só desfaz) + mensagem do sistema; `POST CaseChat/{card}/work-items/{n}/cancel`; contexto do especialista explica `#nn` | — |
| B3 | Testes (domínio `CaseChat.Clear`, aplicação com repositório em memória se houver) + skill `especialista-do-caso` (`#nn` = item, conversa limpa) | B1, B2 |
| F1 | Markdown `workRefs` (`#nn` → `a.wi-ref`), `planMarkdown:'case'`; popover do item na `appCodeRefHost` | — |
| F2 | Lógica pura do compositor (`composer-suggest.ts`: gatilhos `/` e `#`, filtro, aplicar) + spec | — |
| F3 | `CaseComposerService` (ferramentas e itens do card em cache, `/clear`, cancelar item) + lista de sugestões nos dois compositores; ferramenta escolhida abre o painel (compacto → modal com `tool`) | F2, B1, B2 |
| Q1 | Build/test dos dois lados; e2e local (harness) | todas |
