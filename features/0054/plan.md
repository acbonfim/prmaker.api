# Plano — Feature 0054 (ER como fonte; Base Solvace como leitura e aprendizado)

Branch `feature/0054` em `prform.api-0054` e, na execução, `prform-app-0054`. **Não iniciar antes da 0053**
(rebasear em `master` depois que a 0053 entrar). Responder as perguntas em aberto da spec antes de B2.

| Fase | O quê | Depende |
|---|---|---|
| B1 | Substituição: chave `ReverseEngineeringSupersedes` (seção antiga → documentos da ER; migração idempotente); `ArchitectureSection` "substituída" calculada (todos os documentos que a cobrem publicados); `Exported`/índice/busca/Pergunte/MCP/`for-card` ignoram substituídas; ficha e índice compacto dizem a fonte do módulo (`ER` × base antiga); testes | 0053 |
| B2 | Documento `guia` (`re-guia`, público human): tipo, modelo, lint (fonte por parágrafo, termos técnicos proibidos), pacote da sessão só com o publicado + perguntas do "Pergunte" do módulo + `GLO`; marca "desatualizado" quando um documento técnico é republicado; substitui `guia-*` no modo Simples | B1 |
| B3 | Armadilhas: entidade `ReverseTrap` (módulo, itens, texto, cards, confirmado por), API (listar, criar, editar, converter sugestão em armadilha), `prmake_base_get`/`for-card` trazem as armadilhas dos itens; seção técnica "Armadilhas" do projeto gerada delas | B1 |
| B4 | Sugestões por item: `ArchitectureSuggestion` com `ItemId`; lacuna do Pergunte vira sugestão no documento certo; divergência KC × código (lista para produto); migração das armadilhas/sugestões antigas conforme a resposta da pergunta 1 | B3 |
| S1 | Skills: `engenharia-reversa` gera o `guia` (último do `tudo`) sem ler código; `analisar-bug` passo 9 grava aprendizado com o ID (sugestão ou armadilha); `base-solvace`: consulta diz a fonte do módulo, `kb.sh` ignora substituídas | B2, B3, B4 |
| F1 | Tela Engenharia reversa: aba Guia (revisão com "ver detalhe técnico"), aba Armadilhas, sugestões por item, converter sugestão em armadilha | B2, B3, B4 |
| F2 | Base Solvace: modo Simples lê o `guia` da ER (com links para o detalhe técnico); seções substituídas com faixa e link; Pergunte/busca sem substituídas | B1, B2 |
| T1 | Harness: módulo com ER completa (SA3 do piloto) — seções antigas fora do espelho/MCP/busca, guia publicado no Simples, armadilha aparecendo no `prmake_base_get` e no `for-card`, sugestão de card com ID entrando no `melhorar`; módulo sem ER inalterado | todas |

Ondas: **1** — B1. **2** — B2, B3. **3** — B4, S1, F1. **4** — F2. **5** — T1.
