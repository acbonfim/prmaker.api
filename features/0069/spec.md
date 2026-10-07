# Feature 0069 — Correção sem testes travando e análise sem retomada cara

Análise de 2026-10-06 dos dois cards tratados pelo executor no dia (transcripts das sessões):

| | 75353 (script/chamado) | 75349 (código, edv-solvace-apps) |
|---|---|---|
| Análise (Opus) | 13,7 min · 38 respostas | 16 min de trabalho · 54 respostas (+15 min esperando o usuário) |
| Correção (Sonnet) | 5,9 min · 17 respostas | 24+ min · ~18 min só de jest |
| Entrada (input + cache) | 4,15 M | 5,83 M (97% lido do cache) |

1. **Testes da correção (tempo).** O `validar` do 75349 rodou o jest em duas pastas inteiras (`sign-in`, `user-locked`),
   estourou os 600 s do Bash e foi para segundo plano (10 min perdidos); repetiu com `--maxWorkers=1` (4 min), de novo
   com os dois specs (3,5 min; `user-new.spec` sozinho 141 s) e mais um no `auth.service`; depois do cherry-pick na
   branch de `development` ia testar de novo. O worktree do card nasce sem `node_modules` (o modelo criou o link na
   mão). O `edv-solvace-apps` já roda build + jest no PR (`pr-build-validation.yml`).
   → `scripts/test-changed.sh <worktree>`: roda **só os specs dos arquivos alterados** (o `.spec.ts` ao lado de cada
   `.ts` mudado + specs mudados), **uma vez**, `--coverage=false`, com limite (padrão 420 s, abaixo dos 600 s do Bash)
   que mata o jest inteiro. Estourou → `LENTO` (exit 124): registra no plano e segue para o PR — o CI do PR valida.
   Sem runner conhecido (não é jest) → exit 3 e a skill valida pelo build do repositório.
   → `prmake-plan.sh worktree` liga o `node_modules` do clone principal no worktree do card (macOS/Linux; só quando o
   `.gitignore` já ignora `node_modules`; o `git worktree remove --force` do executor apaga só o link).
   → `correcao.md` (passo 8): validação com o script, nunca pastas inteiras, nada de repetir com outros parâmetros e não
   retestar depois do cherry-pick nas branches do fluxo (só se resolveu conflito na mão — aí roda o script de novo, que
   pega só o que mudou).

2. **Cognito (tempo).** `cognito-query.sh pools efeso` estourou 120 s: a AWS respondia em 25–30 s por chamada naquele
   momento (hoje 1–3 s) e o script pagina a lista inteira de pools (191, 4 páginas) a cada `pools`/`user`/`groups`.
   → lista de pools em cache (`~/.prmake/cache/cognito-pools-<região>[-<profile>].tsv`, 24 h; nome não achado no cache
   → relista uma vez; `pools --refresh` força) e `--cli-connect-timeout 10 --cli-read-timeout 30` em toda chamada.
   `user` com o cache = 2 chamadas (get-user + grupos) em vez de 4 páginas + 2.

3. **Retomada da análise na mesma sessão (tokens).** O comentário do usuário no `propor-solucoes` retomou a sessão da
   análise com 106 mil de contexto: 17 respostas × ~115 mil = 1,94 M (43% da análise) para conferir banco e Cognito.
   A correção já roda em sessão nova (0049); a retomada da análise depois do `propor-solucoes` (checkpoint "RESUMO PARA
   A CORRECAO" gravado) também deveria.
   → **depende da 0068** (mexe no mesmo `ExecutionQueueApplication.cs`: `TryClaimAsync`/`PhaseOfAsync`): fica planejada
   (B1) para depois que a 0068 for mesclada.
