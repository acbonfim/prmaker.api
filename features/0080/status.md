# Status — Feature 0080

| Fase | Descrição | Status | Responsável | Commits | Notas |
|---|---|---|---|---|---|
| B1 | Domínio e banco do caso | concluída | Claude | 2ba28fe | `Case*` (schema execution), divergência no plano, `kind=specialist`, índices por pista; migração `CaseChat0080` testada no Postgres 18 isolado |
| B2 | Aplicação, API, fila e guarda | concluída | Claude | 2ba28fe, + ajustes | `CaseChatApplication`, `api/v1/CaseChat`, pista na fila (até 2 por máquina, executor 1.2.0+), token do caso + `CaseSpecialistMiddleware` |
| B3 | MCP, proposta → plano, divergência, config | concluída | Claude | 2ba28fe, eb5060f | `prmake_case_*`, proposta aceita = comentário-instrução (+ pedido já, se for o dono), log `divergence` trava a etapa `pr`, seed `SpecialistModel`/`SpecialistDeepModel` |
| E1 | Executor 1.2.0 (pista do especialista) | concluída | Claude | cd3d4c9 | `--mcp-config --strict-mcp-config` com o token do caso, `PRMAKE_TOKEN`/`PRMAKE_SPECIALIST`, perfil e guard próprios, 20 min por resposta |
| S1 | Skill especialista-do-caso | concluída | Claude | eb5060f | SKILL.md + `prmake-case.sh` (file/upload/context) |
| S2 | Ajustes na analisar-bug | concluída | Claude | eb5060f | comentário = instrução, "Proposta do Especialista aceita", `divergence` |
| F1 | Especialista do caso (front) | concluída | Claude | front 074569d, f213ff5 | modal, prévia na coluna da direita, status (novas/trabalhando), voz, menções |
| F2 | Tela do plano (M1–M7) | concluída | Claude | front 074569d, f213ff5 | cabeçalho com rótulos, jornada, "Em que pé está", decisões, divergência, etapas agrupadas, "Arquivos e textos" |
| F3 | Cancelar guiado | concluída | Claude | front 074569d | motivos + "Conversar primeiro" |
| Q1 | Integração e e2e | concluída (local) | Claude | | ver abaixo; falta validar com executor e Claude reais |

## Q1 — o que foi testado (2026-11-04, local)
Harness em `.t0080/` (fora do git): Postgres 18 isolado (55480) com as migrações do host e do Cime.Auth, auth falso,
API na 5083 (`RealTime__Mode=InProcess`), front de desenvolvimento na 4200, Chrome headless (puppeteer do `.t0077`),
executor **simulado com curl** (registro 1.2.0, next/start/heartbeat/finish). Cenário do 77881 em `e2e/seed-77881.sh`.
- Divergência pendente: etapa `pr` recusada (400) com a mensagem; alerta na tela com "Confirmar e seguir".
- Pista: mensagem → pedido `specialist` (modelo `sonnet`, token do caso); análise e especialista ativos ao mesmo tempo;
  executor com `MaxConcurrency=1` pega o do especialista.
- Guarda do token do caso: contexto/mensagens/resposta/pontos/proposta/ação OK; `prmake_steps`, `prmake_log`, nota no
  plano, PATCH de etapa → 403; outro card → 403; depois do `finish` → 401. Pessoa não responde como especialista (403).
- Regra de quem responde: 1 pessoa → sempre; 2 pessoas sem menção/pergunta → não; com @Especialista → sim, na máquina
  do dono do plano quando quem escreveu não tem executor. Daniel (sem permissão) não aceita proposta (403).
- Proposta aceita → comentário #1 do plano com a instrução ("NÃO execute"); ação confirmada → especialista chamado de
  novo, retomando a mesma sessão.
- Tela: cabeçalho, jornada, "Em que pé está", decisões, etapas agrupadas, prévia ao vivo; modal com avatares, passos,
  proposta, pontos, arquivos; @menção; "Ver o que muda"; cancelar guiado; celular (botão fixo, conversa em tela cheia);
  tempo real (na fila → atividade ao vivo → resposta sem recarregar). Capturas em `e2e/*.png`.

## Pendências
- **Executor e Claude reais**: rodar uma conversa com o `prmake-agent` 1.2.0 de verdade (deploy publica o executor) e
  conferir o `--mcp-config` com o token do caso e a skill `especialista-do-caso`.
- Texto em streaming (palavra a palavra) não foi feito: a tela mostra a atividade ao vivo e a resposta chega inteira.
- Ação "fechar o PR / apagar a branch" no cancelar guiado não foi feita (sem endpoint): o diálogo avisa os PRs abertos.
- O modal antigo `plan-chat-dialog`/`plan-notes` ficou no código (sem uso na tela do plano) — remover depois de validar.

## Log
- 2026-11-04 — spec, mockups e plano; decisões aceitas pelo usuário ("pode seguir com as propostas").
- 2026-11-04 — B1–B3, E1, S1–S2, F1–F3 e Q1 local. PRs ainda não abertos.
- 2026-11-04 — ajustes pedidos pelo usuário: (1) sem a espera de 2 min depois de comentário (0049) — o plano retoma na
  hora (`ExecutorNoteDelaySeconds` não é mais lida; pedido antigo "juntando comentários" começa já); (2) o aviso "sem
  sinal do Claude" virou ícone no cabeçalho com menu (continuar / copiar o comando); (3) a jornada lê os dois planos do
  par (decisões da análise, QA e fechamento da correção) mesmo vendo o outro. Testado no harness local.
