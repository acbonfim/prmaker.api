# 0098 — Governança do PRMake: regras, guardrails, hooks, telemetria e métricas

Origem: pedido do usuário (2026-11-24, sessão do Claude Code):

> Quero que o PRMake seja a ferramenta que controla todo o fluxo da aplicação. Ela deve ter governança, regras,
> guardrails, hooks, telemetria e métricas importantes. Tudo isso deve ter telas para o perfil de administrador,
> separadas por usuários, cards, tarefas etc. Deve ter a tela de governança e de configuração dessas regras e
> guardrails. Todas as skills devem respeitar essas regras; todo tratamento por dentro do PRMake (tela ou skills) deve
> respeitar as regras de governança e estar transparente para o administrador. Deve ser possível ver quando o usuário
> iniciou uma tarefa, quando fez uma análise ou protótipo inicial, quanto tempo levou cada ação. Deve ser possível
> configurar todos os hooks e as configurações específicas de governança.

## O que entregar

1. **Trilha de auditoria única** — toda ação relevante, venha de onde vier (tela, API, MCP, skill pelo executor ou
   terminal, hook do Claude Code), vira um evento de governança com: quem, de onde (origem), card, plano/fase, item de
   trabalho/sub-tarefa, ação, início, fim, duração, resultado (ok/erro/barrado/avisado) e as regras que dispararam.
2. **Regras e guardrails configuráveis** — o administrador liga/desliga, muda o efeito (monitorar, avisar, bloquear,
   exigir aprovação) e o escopo (papel, pessoa, tipo de card, origem) de cada regra. As mesmas regras valem:
   - no servidor (endpoints e ferramentas do MCP — quem grava é sempre o PRMake);
   - nas skills (hook `PreToolUse` do Claude Code consulta o PRMake antes de cada comando/ferramenta);
   - nas telas (ação barrada mostra o motivo).
   Modo global: desligado · monitorar (só registra) · aplicar.
3. **Hooks configuráveis** — os hooks do Claude Code instalados com as skills (SessionStart, UserPromptSubmit,
   PreToolUse, PostToolUse, Stop…) e os gatilhos do servidor (análise concluída, item iniciado, PR aberto, regra
   barrou…) com ações configuráveis (registrar, avaliar regras, avisar na Timeline/Teams, webhook).
4. **Telemetria e métricas** — linha do tempo de cada card (início da tarefa, primeira análise/protótipo, correção, PR,
   QA, fechamento) com o tempo de cada ação e o tempo entre marcos; agregados por usuário, por card e por tipo de
   tarefa (quantidade, duração mediana/p90, barradas, erros, custo em tokens quando houver).
5. **Telas de administrador** — área "Governança": visão geral, usuários, cards, tarefas, auditoria (com filtros),
   regras e guardrails, hooks e configurações.

## Fora do escopo desta feature

- Substituir a telemetria de navegação da 0079 (continua; a governança lê dela quando fizer sentido).
- Bloquear ações já em andamento: as regras valem para a próxima ação.
