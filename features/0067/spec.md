# Feature 0067 — Análise e engenharia reversa leem a master atualizada

As skills liam o código como ele estivesse no clone de trabalho: a branch atual (ex.: o `edv-solvace` estava em
`hotfix/73821`, com 6 arquivos alterados, durante a engenharia reversa do SOC) e sem `fetch`. A regra documentada ou
analisada podia ser a de uma branch de hotfix ou de dias atrás.

- `skills/analisar-bug/scripts/master-wt.sh <pasta>`: `git fetch origin master` e devolve a mesma pasta numa cópia **só
  de leitura** em `origin/master` (`<clone>/../.prmake-wt/master/<repo>`, a convenção das correções; a busca de
  repositórios já ignora `.prmake-wt`). Nunca mexe no clone de trabalho. Sem rede: usa a `origin/master` local, com aviso;
  sem git/sem master: lê a pasta como está, com aviso. Uma sessão por vez atualiza a cópia (trava).
- `analisar-bug`: `revamp-repos.sh master <repo>` (ler código na análise sempre por ele) e `grep <padrão> <repo>` na
  cópia da master; SKILL.md e `consultas.md` orientam. A correção continua no worktree do card (de `origin/<base>`).
- `engenharia-reversa`: as fontes do inventário (e daí áreas, pacotes, evidência) vêm da cópia da master; `fontes.tsv`
  registra `commit@origin/master`; `--local`/`RE_LOCAL=1` lê o clone como está (só para documentar uma branch que ainda
  não entrou na master).
- Sempre `master` (decisão do usuário, 2026-10-06); `PRMAKE_MASTER_BRANCH` troca se um dia for preciso.

Testado: `tools/SkillTests/test_master_wt.py` (clone em hotfix com alteração não commitada, master andando duas vezes) e
na máquina real (edv-solvace: 1 GB, ~7 s na primeira vez, ~2,5 s nas seguintes; clone intocado).
