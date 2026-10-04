# Plano — Feature 0056 (ajustes da engenharia reversa: tela da Base Solvace e ferramentas da skill)

Branch `feature/0056` em `prform.api-0056` e, na execução, `prform-app-0056`. **Não iniciada** — responder as
perguntas da spec antes.

| Fase | O quê | Depende |
|---|---|---|
| S1 | Termos curtos: `needles()`/`words()` aceitam sigla de 2 caracteres com dígito ou maiúscula (bordas de palavra); `kb.sh index` idem; testes | — |
| S2 | Termos em lacuna contam: `glossary_text` inclui `GAP` de termos fora do glossário (título ou `**Termos:**`); `references/banco.md` com o formato; revisão mostra "fora do glossário (GAP-…)" | — |
| S3 | `--path` com arquivo e glob em `resolve_sources`, `inventario` e `trabalho`; fontes do módulo na tela aceitam o mesmo formato | — |
| S4 | Traduções: diagnóstico na DEMO (VPN); fonte configurável (chave semeada por migração), leitura só dos termos do módulo em lotes, motivo claro quando faltar | resposta 2 |
| F1 | Base Solvace: árvore do Simples sem as substituídas (ou grupo "Histórico" recolhido, conforme a resposta 1); aba "Armadilhas" no Técnico gerada das armadilhas do módulo (link para os itens e para conferir na Engenharia reversa) | resposta 1 |
| T1 | Teste com o funcional do SA3 (`A3` coberto pelo `GLO-001`, termos em `GAP`), `--path` com os serviços do `helpers/`, `re.sh banco` na DEMO real, prints da tela | todas |
