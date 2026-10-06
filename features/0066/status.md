# Feature 0066 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0066` em `prform.api` (backend + skills) e `solvace.prform.web/prform-app` (frontend).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Status | Responsável | Commits |
|---|---|---|---|---|---|
| B1 | `ReverseIntegrations` | back | 🟡 | Claude (sessão principal) | |
| B2 | Relações efetivas no mapa/projeto/espelho | back | ⬜ | Claude (sessão principal) | |
| B3 | Checagem + modelos do INT | back | ⬜ | Claude (sessão principal) | |
| B4 | Config `ReverseEngineeringGeneration` | back | ⬜ | Claude (sessão principal) | |
| B5 | Testes | back | ⬜ | Claude (sessão principal) | |
| S1 | `re_tool.py` areas/pacote/faltando/evidencia/cartao | skill | ⬜ | Claude (sessão principal) | |
| S2 | Retrato do banco e da AWS | skill | ⬜ | Claude (sessão principal) | |
| S3 | `re.sh` comandos novos | skill | ⬜ | Claude (sessão principal) | |
| S4 | SKILL.md e referências | skill | ⬜ | Claude (sessão principal) | |
| S5 | Testes dos scripts | skill | ⬜ | Claude (sessão principal) | |
| F1 | Mapa com itens/origem | front | ⬜ | Claude (sessão principal) | |
| F2 | "Por pergunta" | front | ⬜ | Claude (sessão principal) | |
| Q1 | Build, e2e local, piloto | ambos | ⬜ | Claude (sessão principal) | |
| Q2 | PRs, merge e deploy | ambos | ⬜ | Claude (autorizado pelo usuário) | |

## Decisões

| # | Decisão | Motivo |
|---|---|---|
| D1 | Integrações da ER calculadas do índice na leitura (não gravadas no projeto) | corrige os já publicados sem republicar; uma fonte só |
| D2 | Retrato local (`~/.prmake/reverse/_retrato`), não central | escopo; quem gera tem o acesso; central fica para depois |
| D3 | Modelo dos subagentes por configuração, padrão `sonnet` (Haiku só se o admin configurar) | "mesma qualidade" — o barato vem da estrutura; Haiku sem medição é risco |
| D4 | Avisos (não erros) para INT sem chave/mecanismo | documentos antigos continuam publicáveis |
| D5 | Merge dos dois PRs juntos ao final | autorizado pelo usuário em 2026-10-06 ("ao final pode mergear e publicar") |

## Notas de handoff

## Log

- 2026-10-06 — spec/plan/status criados a partir da análise da sessão (card 75294, sessões do `legado-checklist`).
