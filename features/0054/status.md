# Status — Feature 0054

Branch `feature/0054` em `prform.api-0054` (API, skills) e `prform-app-0054` (front). **Implementada em 2026-10-04.**

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B1 | ✅ concluída | Claude | 2212aba |
| B2 | ✅ concluída | Claude | 2212aba |
| B3 | ✅ concluída | Claude | 2212aba |
| B4 | ✅ concluída | Claude | 2212aba + ffee9a9 (sugestões por documento) |
| S1 | ✅ concluída | Claude | 7e362b6 + ffee9a9 (armadilhas no `get`, fonte no índice) |
| F1 | ✅ concluída | Claude | front ba94bf1, 3159717 |
| F2 | ✅ concluída | Claude | front ba94bf1, 3159717 |
| T1 | ✅ local (harness, SA3 do piloto) | Claude | — (harness fora do git em `.t0054/`) |

## Decisões
- Substituição calculada na leitura (não grava estado): seção antiga cujo mapa aponta só para documentos publicados sai
  do espelho, índice, busca, Pergunte e MCP; continua no projeto com a faixa "Histórico". As chaves do mapa são as
  chaves reais das seções (`modulos`, `dados`, `armadilhas` — o número `010-` é só a ordem no nome do arquivo do espelho).
- `@armadilhas` no mapa = a seção antiga de armadilhas sai quando o módulo registra a migração (`re.sh migrar`); o
  espelho passa a ter `090-armadilhas.md` gerado das armadilhas novas (com `**Itens:**`).
- Visão prática (`pratica`) é `Derived`: a API recusa a sessão até os demais exigidos estarem publicados; a trava da
  análise (`for-card`/gate) usa só os técnicos. Fica "desatualizada" quando um técnico é republicado depois dela.
- Cobertura da revisão da visão prática = perguntas reais do Pergunte sobre o módulo respondidas por FAQ/TUT.
- Cada sessão recebe e resolve só as sugestões do próprio documento (mais as antigas sem documento da ER); lacuna do
  Pergunte num módulo com ER vira sugestão `gap` em `re-pratica`.
- Armadilhas: vindas de análise/migração ficam "a conferir"; quem aprova confere, remove ou converte uma sugestão.

## T1 (local)
Postgres isolado (55454), API 5083, front 4200, auth e IA falsas; funcional real do piloto SA3 (176 itens) +
arquitetura mínima; exigidos no teste: funcional, arquitetura, pratica.
- Funcional publicado → só `regras-de-negocio` substituída; com a arquitetura, `dados` também; `visao-geral` fica (falta
  a visão); Kaizen (sem ER) inalterado na busca e no espelho.
- Visão prática recusada antes dos exigidos ("publique antes funcional, arquitetura"); lint pegou termo técnico
  (`TB_SA3_A3`), passo sem fonte e fonte inexistente (`RN-999`); versão correta: 3/3 perguntas reais, publicada pela
  tela; Base Solvace Simples abre nela e o Guia antigo vira histórico; republicar o funcional → "Desatualizada".
- Migração: 2 armadilhas antigas ligadas a `UC-003`/`RN-021` e `INT-001` ("a conferir"), sugestão sem item ligada ao
  `RN-017`; seção antiga sai da busca; `for-card`, MCP, `re.sh get` e `kb.sh re get` trazem a armadilha com o item.
- Tela: conferir armadilha, "virar armadilha" (card 75091 → `RN-021`), divergência KC na aba Revisões, faixa de fonte.
- Pergunte (IA falsa) sem resposta → sugestão `gap` em `re-pratica`, no pacote do próximo `melhorar pratica`.
- Achados corrigidos no T1: pacote da prática trazia sugestões do funcional (e a publicação as fecharia); `get` sem
  armadilhas; índice compacto sem a fonte; rótulo "inventário" na cobertura de perguntas; total "/6" fixo na Base.

## Log
- 2026-10-04 — spec e plano escritos a pedido do usuário (ER como fonte; Base Solvace como leitura e aprendizado; guia
  Simples gerado da ER; armadilhas ligadas aos itens). Worktree `prform.api-0054` (branch `feature/0054` a partir de
  `origin/master` 15151d6). Perguntas em aberto na spec.
- 2026-10-04 — respostas do usuário: migração automática, visão prática exigida, substituídas visíveis, transversais ganham ER depois; guia virou o 7º documento "Visão prática (não técnica)" (depende dos obrigatórios; TUT/FAQ; fonte obrigatória; validação por perguntas reais).
- 2026-10-04 — executada a pedido ("faça tudo e pode mergear no final", depois da 0053): B1–B4, S1, F1, F2 e T1 local.
