# Plano — Feature 0054 (ER como fonte; Base Solvace como leitura e aprendizado)

Branch `feature/0054` em `prform.api-0054` e `prform-app-0054`. Executada em 2026-10-04, depois da 0053 (ver `status.md`). Perguntas respondidas em 2026-10-04 (seção "Respostas" da spec).

| Fase | O quê | Depende |
|---|---|---|
| B1 | Substituição: chave `ReverseEngineeringSupersedes` (seção antiga → documentos da ER; migração idempotente); `ArchitectureSection` "substituída" calculada (todos os documentos que a cobrem publicados); `Exported`/índice/busca/Pergunte/MCP/`for-card` ignoram substituídas; ficha e índice compacto dizem a fonte do módulo (`ER` × base antiga); testes | 0053 |
| B2 | Documento `pratica` — visão prática não técnica (`re-pratica`, público human, exigido): só abre sessão com os demais obrigatórios publicados; tipos `TUT`/`FAQ`; modelo (o que é/legado×revamp, como chegar, como fazer, perguntas práticas, regras simples, configurar, testar, glossário); lint (fonte obrigatória por item/passo, termo técnico proibido, fonte removida = erro); pacote com o publicado + perguntas do "Pergunte" do módulo + lacunas + `GLO`; cobertura de perguntas na revisão; "desatualizado" quando um técnico é republicado; substitui `guia-*` no Simples e alimenta o Pergunte | B1 |
| B3 | Armadilhas: entidade `ReverseTrap` (módulo, itens, texto, cards, confirmado por), API (listar, criar, editar, converter sugestão em armadilha), `prmake_base_get`/`for-card` trazem as armadilhas dos itens; seção técnica "Armadilhas" do projeto gerada delas | B1 |
| B4 | Sugestões por item: `ArchitectureSuggestion` com `ItemId`; lacuna do Pergunte vira sugestão no documento certo; divergência KC × código (lista para produto); migração das armadilhas/sugestões antigas conforme a resposta da pergunta 1 | B3 |
| S1 | Skills: `engenharia-reversa` gera a visão prática (último do `tudo`, sem ler código; roda as perguntas reais e a bateria do módulo contra o documento antes do envio) e faz a migração automática das armadilhas/sugestões antigas para os itens; `analisar-bug` passo 9 grava aprendizado com o ID (sugestão ou armadilha); `base-solvace`: consulta diz a fonte do módulo, `kb.sh` ignora substituídas | B2, B3, B4 |
| F1 | Tela Engenharia reversa: aba Visão prática (bloqueada até os obrigatórios; revisão com "ver detalhe técnico" e cobertura de perguntas), aba Armadilhas, sugestões por item, converter sugestão em armadilha | B2, B3, B4 |
| F2 | Base Solvace: modo Simples lê a visão prática da ER (TUT/FAQ) (com links para o detalhe técnico); seções substituídas com faixa e link; Pergunte/busca sem substituídas | B1, B2 |
| T1 | Harness: módulo com ER completa (SA3 do piloto) — seções antigas fora do espelho/MCP/busca, visão prática publicada no Simples respondendo as perguntas reais do SA3 ("SA3 é legado ou revamp?", "como criar um RCA?"), armadilha aparecendo no `prmake_base_get` e no `for-card`, sugestão de card com ID entrando no `melhorar`; módulo sem ER inalterado | todas |

Ondas: **1** — B1. **2** — B2, B3. **3** — B4, S1, F1. **4** — F2. **5** — T1.
