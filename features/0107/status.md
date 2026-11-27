# Status 0107

- Branch: `feature/0107` (prform.api-0107 e prform-app-0107), a partir de origin/master.
- Merge: aguardando o ok do usuário.

| Fase | Estado | Notas |
|---|---|---|
| B1 — escolha | ✅ | `CaseChoice` em `CaseMessage.ChoiceJson` (jsonb `Choice`): modos `buttons` (até 6), `single`/`multi` (até 12), `select` (até 50); ids normalizados; estilos primary/danger/neutral; `recommended`; `allowOther`; uma resposta só; escolha nova fecha as abertas |
| B2 — API/MCP | ✅ | `prmake_case_reply(choice)` (erro de validação volta para o especialista); `POST CaseChat/{card}/messages/{id}/choice` → resposta gravada na escolha + mensagem da pessoa em resposta à escolha + especialista chamado sempre; `choiceAnswer` em `prmake_case_messages`; linha `[escolha ...]` no contexto |
| B3 — skills | ✅ | especialista-do-caso regra 17 (opções/"posso seguir?" = escolha clicável, botões com o que vai acontecer, encerra a vez, clique = autorização) e 18 (termos); gestão regra 14; instruções do MCP |
| B4 — Multilingual | ✅ | `mlg.sh search/exists/check` (psycopg do venv da base-solvace ou `psql`; só leitura); regra "Texto que o usuário final lê" na analisar-bug (análise já sugere os termos, como opções da pergunta), analisar-us, executar-item (regra 10 + `code`), especialista. Testado no Multilingual de dev: acha os 2 termos do 79047 no topo; "Acesso negado." **não** está cadastrado (o especialista tinha afirmado pelo grep) |
| F1 — front | ✅ | `app-case-choice` (botões, rádio, checklist, lista suspensa, outra resposta, respondida/substituída) na conversa compacta e na grande |
| Q1 — testes | ✅ | executionplans 474 (11 novos) · prform 721 · specs da conversa 31 (4 novas) · ng build ok |
| Q2 — e2e | ✅ | `.t0107/e2e_choice.py` 0 falhas (reply com escolha, recusas, substituída, clique → mensagem + especialista, uma resposta, choiceAnswer, contexto); telas em `.t0107/shots/` |

## Decisões

- O clique vira **mensagem da pessoa** (o histórico fica legível e o especialista lê como qualquer mensagem) e chama o
  especialista mesmo com várias pessoas na conversa.
- A ação continua com o especialista (o clique é a autorização); a tela não executa nada sozinha.
- Só a escolha mais nova vale um clique: evita "Criar e executar" numa sugestão velha.
- Termos: a fonte é o Multilingual (não o grep do código); termo novo só com aprovação do produto.

## Log

- 2026-11-27 — feature implementada e testada (unidade + e2e local + telas + Multilingual de dev). PRs abertos.
