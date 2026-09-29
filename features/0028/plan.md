# Feature 0028 — Toda gravação das skills pelo PRMake

## Levantamento
- Já pelo PRMake: root cause (`POST Azure/card/{id}/rootcause`), resumo não técnico (`POST PullRequest/{card}/summary`), Ações DevOps (`actions/test-in-production|ready-for-qa|initial-estimate|zero-remaining`), registro do card, PRs, timeline, plano de execução.
- **Direto no Azure (PAT local)**: a classificação (`azure-fields.sh` da gerar-prmake, também usado pelo `devops classify` da analisar-bug) e o fallback `azure-comment.sh` do resumo.

## Decisões
- **Backend**: `GET Azure/actions/classifications` (opções; padrão = 14 combinações reais do estudo da 0027, sobrescritas pelo "AI Configurations" → `BugClassificationPresets`, pessoal/global) e `POST Azure/card/{id}/actions/classify` (`{preset}` ou os três valores) — grava pela integração do Azure do usuário e registra na Timeline, como as demais Ações DevOps.
- **gerar-prmake**: `azure-fields.sh` passa a chamar o PRMake (mesma interface de env + `CLASSIFICATION_PRESET`); sai o `azure-comment.sh` e o fallback direto no Azure; sem PAT do Azure na máquina.
- **analisar-bug**: `devops classify <opção>` e `devops classifications` pelo PRMake; regra explícita na SKILL.md — o Claude gera os textos e lê, quem grava é sempre o PRMake; faltou endpoint → parar e avisar.
