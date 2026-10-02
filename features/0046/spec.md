# 0046 — Scripts e análises do card salvos sempre (executor e terminal)

## Contexto
Card 75091: o Claude, rodando pelo executor (Claude Code em `dontAsk`), não conseguiu gravar o `.sql` nem a análise em
`~/.claude/cards/75091` — toda gravação ali foi negada (Write e também `cat >` pelo Bash). Sem o arquivo local, o
`sync` não tinha o que enviar: o script e o texto do chamado não apareceram nos arquivos do plano. O Claude pediu ao
usuário para rodar `prmake-skills.sh permissions`, que não resolveria.

Causa (testado com o próprio Claude Code): ele **protege `~/.claude`** — nega gravar ali mesmo com `Write` liberado e
com regra específica `Write(~/.claude/cards/**)`. Fora dessa pasta, grava normalmente.

## Objetivo
1. A pasta dos cards sai de `~/.claude/cards` para `~/.prmake/cards` (o card antigo é migrado sozinho).
2. O executor libera a pasta nova para o Claude (`--add-dir`) e se atualiza sozinho em todas as máquinas.
3. Quem usa o terminal recebe a liberação da pasta nova pelo instalador das skills (sem rodar comando).
4. O Claude grava arquivos direto nos arquivos do plano pelo MCP (`prmake_file`), mesmo se a gravação local falhar.
5. A skill manda todo script, análise e texto do chamado para os arquivos do plano e nunca trava a etapa por isso.
