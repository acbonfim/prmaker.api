# Plano 0107 — Escolhas clicáveis + termos do Multilingual

| Fase | O que | Depende |
|---|---|---|
| B1 | `CaseChoice` (buttons/single/multi/select, outra resposta, uma resposta, fechada pela nova) em JSON na `CaseMessage` (coluna `Choice`, migração `CaseChoice0107`) | — |
| B2 | `prmake_case_reply`/`POST reply` com `choice`; `POST CaseChat/{card}/messages/{id}/choice` (mensagem da pessoa + especialista sempre chamado); `choiceAnswer` no `prmake_case_messages`; escolha no contexto | B1 |
| B3 | Skills: especialista-do-caso (regras 17 e 18), especialista-de-gestao (14), instruções do MCP | B2 |
| B4 | Multilingual (pedido 2 do usuário): `base-solvace/scripts/mlg.sh` (`search` com sinônimos, `exists`, `check` do diff) + regra nas skills analisar-bug, analisar-us, executar-item-de-trabalho, especialista-do-caso | — |
| F1 | `app-case-choice` na conversa compacta e na grande; `answerChoice` no serviço | B2 |
| Q1 | testes de unidade (back e front) | B*, F1 |
| Q2 | e2e local (`.t0107/e2e_choice.py`) + telas; `mlg.sh` contra o Multilingual de dev | Q1 |
