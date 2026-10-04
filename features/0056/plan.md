# Plano — Feature 0056 (ajustes da engenharia reversa: tela da Base Solvace e ferramentas da skill)

Branch `feature/0056` em `prform.api-0056` e, na execução, `prform-app-0056`. **Não iniciada.** Respostas 1 e 2 dadas;
falta o host/credencial do Multilingual da DEMO (pergunta aberta da spec) para o S4.

| Fase | O quê | Depende |
|---|---|---|
| S1 | Termos curtos: `needles()`/`words()` aceitam sigla de 2 caracteres com dígito ou maiúscula (bordas de palavra); `kb.sh index` idem; testes | — |
| S2 | Termos em lacuna contam: `glossary_text` inclui `GAP` de termos fora do glossário (título ou `**Termos:**`); `references/banco.md` com o formato; revisão mostra "fora do glossário (GAP-…)" | — |
| S3 | `--path` com arquivo e glob em `resolve_sources`, `inventario` e `trabalho`; fontes do módulo na tela aceitam o mesmo formato | — |
| S4 | Traduções do **Multilingual (revamp, PostgreSQL)**: leitura somente leitura com credencial local (`multilingual-credentials.json`, modelo do KC), ambiente/host/database/schema configuráveis (chave semeada por migração), só os termos do módulo (`term_key_application` + termos do código) em lotes, pt/en/es como sinônimos sugeridos, sai o `TB_WCM_LANGUAGE`, motivo claro sem acesso; `banco.md`/SKILL.md | host/credencial |
| F1 | Base Solvace: grupo **"Histórico" recolhido** na árvore do Simples com as seções substituídas (fora da busca simples); aba "Armadilhas" no Técnico gerada das armadilhas do módulo (link para os itens e para conferir na Engenharia reversa) | — |
| T1 | Teste com o funcional do SA3 (`A3` coberto pelo `GLO-001`, termos em `GAP`), `--path` com os serviços do `helpers/`, `re.sh banco` na DEMO real com as traduções do Multilingual, prints da tela | todas |
