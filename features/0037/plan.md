# Plano — 0037: pendências do usuário evidentes e banco sem bloqueio de permissão

## Diagnóstico (card 74669)

**1. O PRMake não distingue "trabalhando" de "esperando você".**
- A etapa 1 *Confirmar no banco* (executor `claude`) ficou **`running`** — "Em andamento •••" com cronômetro e
  "ao vivo" — por 8 min, quando na verdade estava parada esperando o usuário liberar uma permissão. A única pista
  era um `log warning` dentro da etapa expandida, que só troca o ícone (sem fundo/borda).
- A etapa 2 (executor `user`, pronta — sem dependência aberta) só tem o chip "Você". O botão *Concluir* fica
  escondido no detalhe expandido. Não há aviso no topo, nem contagem, nem destaque na linha.
- O único aviso "aguardando você" que existe é o de **perguntas abertas** (topo do painel). Fora do card aberto
  nada mostra pendência: o chip do card só conta perguntas/pausa, a lista de recentes não tem nada sobre o plano,
  o evento de tempo real só vai para quem está com o card aberto, não há título da aba/notificação.
- Backend: `waiting` existe só para pergunta, chamado (`blocksStep`) e PR sem merge. Não existe "aguardando o
  usuário" para uma etapa do Claude, e o resumo do plano (`ExecutionPlanSummaryResponse`) tem apenas total/concluídas.

**2. A permissão do banco não estava liberada no Claude Code.**
- A "autorização" que o usuário deu no PRMake não vale para o Claude Code: o auto mode barrou o
  `sql-query.sh --host prod`. O instalador (`_tooling/prmake-skills.sh` `ensure_permissions`) só libera
  `prmake-plan.sh devops:*`; o `sql-query.sh` e o `cognito-query.sh` (somente leitura — o próprio script barra
  escrita e faz rollback) não têm regra, e o `doctor` não confere permissões.
- `consultas.md` mostra o comando também com `echo ... |` (pipe), o que não casa com uma regra por prefixo.
- O problema só aparece no meio da análise; não há um teste de acesso (VPN + permissão) no começo.

**3. A skill fechou a análise sem o banco.**
- Nada proíbe concluir a análise/criar o plano de correção quando a consulta ao banco era necessária e não rodou.
  Ela transformou a verificação em etapa do plano de correção (`confirmar-dados`, chave inventada) e encerrou.
- Não há regra para "etapa do Claude travada por algo que só o usuário resolve": não marca, não avisa, e o vigia
  só acorda com mudança no PRMake — então nada a destrava.

## Solução

**Novo conceito: pendência do usuário.** Uma etapa está *pendente do usuário* quando:
- é do usuário (`executor=user`) e está pronta (`pending` sem dependência aberta, `running` ou `waiting`); ou
- é do Claude e está `waiting` com `waitingOn=user` (ex.: liberar permissão, ligar a VPN, credencial); ou
- tem pergunta aberta.

A tela mostra isso no topo do painel ("Aguardando você — N ações"), na linha da etapa (cor/ícone próprios e o
botão de ação na própria linha), no chip do card, na lista de recentes e no título da aba.

## Fases

| Fase | O quê | Depende de |
|---|---|---|
| **B1** | Domínio: `ExecutionStep.WaitingOn` (`user`/`external`, nulo) + `ActionRequired` (texto do que o usuário precisa fazer, máx. 2k). Pergunta → `waitingOn=user`; chamado/PR → `external`. `PATCH steps/{key}` aceita `status:"waiting", waitingOn:"user", actionRequired`. Novo `POST {id}/steps/{key}/resolve` (usuário: "Já resolvi") → volta a etapa para `running`, grava quem/quando (`StatusChangedBy`), loga e publica tempo real (o vigia acorda pela mudança de status). Migração `AddStepWaitingOn`. | — |
| **B2** | Pendências calculadas: `UserActions[]` na resposta do plano (`stepKey`, `type`: `question`/`user-step`/`unblock`, `title`, `text`) e `UserPendingCount` no resumo (`card/{n}`) e no `control` (para a skill). Novo `GET ExecutionPlan/pending` → planos ativos do usuário logado (criador) com pendência: card, título, nº de pendências, primeira pendência. Tempo real: publicar também no grupo do usuário (ou, se não houver grupo por usuário, reaproveitar o grupo de recentes) para atualizar a lista sem abrir o card. Timeline: "⚠️ Aguardando você: <ação>" ao entrar em `waitingOn=user`. | B1 |
| **F1** | Painel: aviso no topo "Aguardando você" (substitui/une o de perguntas) listando cada pendência com o botão certo (*Responder*, *Concluir*, *Já resolvi*). Linha da etapa: estado visual próprio "Aguardando você" (âmbar forte, ícone de mão/pessoa, sem "•••"/"ao vivo", cronômetro vira "parada há N min"), `actionRequired` como sub-linha, botão na linha. Etapa do usuário pronta: "Sua vez" + *Concluir* na linha. Seleção inicial abre a primeira pendência. `log warning` com fundo/borda âmbar. | B1, B2 |
| **F2** | Fora do painel: chip do card com tom `warn` e texto "Aguardando você (N)"; lista de recentes com selo de pendência (via `GET ExecutionPlan/pending`); contador no título da aba (`(N) PRMake`) e badge do app (`setAppBadge`) quando houver pendência. Notificação do navegador opcional (pedir permissão uma vez). | B2 |
| **S1** | Permissões: `ensure_permissions` v2 (novo marcador, roda uma vez) libera `sql-query.sh:*` e `cognito-query.sh:*` (somente leitura) nos 3 formatos de caminho. `doctor` confere as regras e diz como corrigir. `consultas.md`: rodar sempre com caminho literal, comando único, consulta via `-f arquivo.sql` (sem `echo |`, sem `&&`/`;`). | — |
| **S2** | Skill — pendências: novos subcomandos `prmake-plan.sh block <card> <step> "<o que o usuário precisa fazer>"` (PATCH `waiting`/`waitingOn=user`/`actionRequired` + `log warning`) e `unblock`. Regra no SKILL.md/`plano-execucao.md`: qualquer etapa do Claude travada por algo que só o usuário resolve (permissão negada, VPN, credencial, acesso) → `block` **na hora**, dizer no chat o mesmo texto e deixar o vigia; nunca deixar `running`. Quando o vigia acordar pelo *Já resolvi*, tentar de novo. | B1 |
| **S3** | Skill — banco obrigatório: (a) *preflight* no início da análise quando o card envolve dados (`sql-query.sh --host <h> -d <db> --ping` = `SELECT 1`), para estourar VPN/permissão antes de investigar; (b) a etapa `consultar-ambiente` não pode ser cancelada nem a análise concluída quando a hipótese depende de dados: sem acesso, a análise fica `block`eada esperando o usuário (não vira etapa do plano de correção); (c) a análise publicada diz explicitamente se o banco foi consultado. Bump da versão da skill (auto-update). | S1, S2 |
| **Q1** | Teste local (Postgres isolado + front 4200): etapa do Claude bloqueada → aviso no topo, linha, chip, recentes, título da aba; *Já resolvi* volta para `running` e o `watch` acorda; etapa do usuário pronta com *Concluir* na linha; pergunta aberta no mesmo aviso. Skill: `doctor` acusa regra ausente; `block`/`unblock` contra a API local. | F1, F2, S2, S3 |

**Ondas:** 1 = B1 ‖ S1 · 2 = B2 ‖ S2 · 3 = F1 ‖ F2 ‖ S3 · 4 = Q1.

## Decisões em aberto
- `GET pending` só do criador do plano ou de quem está atribuído no card? (proposta: criador — é quem roda a skill).
- Notificação do navegador (F2) entra agora ou fica para depois.
- Liberar a regra do `sql-query.sh` para todos os hosts (inclui prod) — proposta: sim, o script é somente
  leitura por construção (só SELECT/WITH, palavras proibidas, rollback forçado).
