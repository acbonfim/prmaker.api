# Status — Feature 0071

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B1 contexto por item (`ReverseAnswerContext`, `GetAnswerContextAsync`) | ✅ | Claude | 8486f39 |
| B2 Pergunte com itens + seções (blocos BM25, vários por seção; itens viram resultados com o cabeçalho do item) | ✅ | Claude | 473f2ed |
| B3 prompt para leigo, resposta em bloco `<<<RESPOSTA`, "como fazer" também por regra | ✅ | Claude | 473f2ed |
| B4 velocidade: plano da IA em paralelo com o aquecimento do índice por item; seções só dos projetos prováveis e **sem os `re-*`** (98% do texto); resposta em cache 15 min; log de fases | ✅ | Claude | 473f2ed + próximo |
| T1 testes (corpus real do espelho + amostra versionada em `Fixtures/`) | ✅ 175 testes | Claude | 8486f39, 473f2ed |
| Q1 deploy + 1–2 perguntas reais | ⏳ depois do merge/deploy | Claude | |
| F1 front: etapas de progresso | ⏳ opcional | | |

## Achados (medidos)
- Requisição de 23/10 12:22 (`cime-pullrequest`, instância de 12:03): total 69,5 s = catálogo 1,4 s + IA plano 3,7 s +
  **busca ~56 s (SQL só 1,8 s)** + 6 trechos 0,6 s + IA resposta 7,3 s. O "Executed DbCommand" mede só até o 1º byte: a
  leitura das seções em fluxo (cross-region) não aparece — o gargalo é transferir/ler o texto das seções.
- No espelho local, **54,2 de 55,2 M caracteres das seções são documentos da ER (`re-*`)**; o resto (Guia, operação,
  legado, 020/030/090) soma ~1 M. Esses documentos já entram por item. A busca de seções do Pergunte agora pula os `re-*`
  (`SearchInProjectsAsync(..., withReverseDocs: false)`) e só olha os projetos prováveis.
- CPU da busca fria (1 vCPU, espelho inteiro em memória): ~4 s com JIT; quente 6–500 ms → não é CPU.
- Retrieval, pergunta "o que preciso para transferir um usuário para outra planta?" no `revamp-users` real: traz UC-065,
  RN-293, RN-294, RN-266, RN-267 (antes: só o bloco do UC-053).

## Produção (deploy do #129, 23/10 ~13:14)
| | 1ª pergunta (instância fria) | 2ª pergunta (quente) |
|---|---|---|
| total | 47 s (antes 69,5 s) | 22,7 s |
| plano IA (+ aquecimento do índice por item) | 28,2 s | 2,6 s |
| itens | 4,8 s (19) | 4,2 s (23) |
| seções (sem `re-*`) | 1,8 s | 0,8 s |
| resposta IA | ~11 s | ~14 s (≈2.650 tokens de saída para 450 palavras) |

Qualidade: a resposta da transferência agora traz Resumo, Quem pode, Antes de começar, Passo a passo (Usuários → Configuração
→ Cadastro de usuários → Transferir → planta → área → confirmar), O que acontece depois, Cuidados e Não confirmado (a divergência
entre fontes sobre quem pode).
Próximo: `AIGenerateOptions(Effort)` por chamada (`low` no plano e na resposta; retry sem override se a API recusar) e resposta ≤ 250 palavras.

## Pendente (próximas fases)
- Cold start do **índice por item** (carrega ~24 mil itens do banco por instância; 4–10 s medidos na 0070): persistir
  o índice normalizado / busca no Postgres (tsvector), ou `min_instances=1` + aquecimento na subida.
- Procedimentos TUT por tarefa (gerar no Claude Code, não na API).

## Log
- 2026-10-23 — investigação (spec.md), worktree `prform.api-0071` a partir da master (b7d1418), B1–B4 + T1.
