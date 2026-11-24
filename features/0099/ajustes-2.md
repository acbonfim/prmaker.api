# 0099 — Ajustes 2 (2026-11-24): papel management, Mesa para PO/gestor, acompanhamento e histórias no PRMake

Pedido do usuário (transcrito):

> A tela de gestão vai ser usada por uma pessoa que não tem GitHub na máquina, não tem código, não tem acesso à base de
> dados do cliente. Então eles vão usar engenharia reversa e tools do PRMake pra retornar resultados; pode sim usar os
> recursos de validar no ambiente e reproduzir bug no ambiente, mas limitado a ferramentas que um PO e gestor de
> problemas pode ter. Inclusive ele pode ver (caso tenha) o status de PRs, pipeline de deploy e em quais ambientes ele
> está mergeado e deployado. Isso precisa estar claro pra eles no dia a dia; eles podem ter um épico pra monitorar esse
> tipo de coisa. Deve ser possível personalizar quais colunas exibir e qual lane e manter isso salvo no épico.
>
> A configuração inicial de uma máquina que não tenha o cargo de support não deve exigir tantas coisas: apenas o Claude
> Code instalado e as integrações pertinentes como DevOps, provedor de IA opcional, skills do PRMake, executor atualizado.
>
> Vamos criar um papel management; ele (a Mesa) vai ser visível apenas para admin ou quem tiver esse papel. Management
> não acessa Bancada. Remove as ações de levar para Bancada (abrir na Bancada) para quem não tiver o perfil de
> desenvolvedor ou admin.
>
> Deve ser possível criar histórias/bugs dentro do PRMake como uma nova aba onde tem board, itens etc. Essas
> histórias/bugs podem ser convertidas para card (caso ainda não tenha sido feita); após ser feita, o botão de criar card
> a partir da história/bug desaparece/desativa.
>
> Perfil management acessa engenharia reversa e Base Solvace.

(Colunas/lane salvas no épico já entraram nos ajustes 1: `boardFilters.hiddenColumns` + `lanes`.)

## Regras de acesso

| Papel | Mesa (`auth/desk`) | Bancada (`auth/workbench`) | "Abrir na Bancada" | Base Solvace / Engenharia reversa |
|---|---|---|---|---|
| admin | sim | sim | sim | sim |
| management (sem papel de dev) | sim | **não** (rota redireciona para a Mesa) | **não** | sim |
| user / support (dev) | não | sim | sim (onde existir) | sim |
| gestor (sem outro papel) | não | sim (como hoje) | sim | sim |

- "Perfil de desenvolvedor" = `user` ou `support`. Quem tem `management` **e** papel de dev tem os dois mundos.
- Management sem papel de dev: o menu mostra Mesa de gestão, Base Solvace, Engenharia reversa (e Minhas integrações /
  executores como hoje); `auth/dashboard` e `auth/workbench` redirecionam para `auth/desk`; ações que levam à Bancada
  somem (painel do card, aba Cards, popover `#n`, home, menu de pendências, guia).
- Back: `EpicsController` (escrita) e `DevOpsController` exigem `admin` ou `management` — exceto `GET Epics/by-card/{card}`
  (chips da Bancada) e o token do caso/executor (MCP e REST do especialista/executor continuam valendo como hoje).
  `PluginConfigurationController` passa a aceitar `management`. Opção `management` no catálogo de
  `ReverseEngineeringApproverRoles`. Papel criado de forma idempotente no Cime.Auth (seed que garante cada papel).
- Front: `RoleService` único (lê os papéis da api-key: claim longa ou `role`, string ou array) + `roleGuard` (data
  `roles`/`deny`) substituindo as cópias de `readIsAdmin`; `management` no fallback da gestão de usuários e na lista de
  papéis da governança.

## Primeiros passos por papel
- Management sem papel de dev: passos = **Claude Code** (instalado + login), **integrações** (Azure DevOps obrigatório;
  provedor de IA opcional; demais ocultos), **skills do PRMake** (+ api-key), **executor** online e atualizado. Sem bancos,
  sem repositórios, sem git/AWS/NuGet. Checks do doctor fora do perfil aparecem como "não necessário para o seu perfil"
  (ou ocultos), nunca como erro.
- Executor: pedidos de épico (`E…`, especialista e itens de gestão) rodam num diretório neutro (`~/.prmake/desk`), sem
  exigir mapa de repositórios/workspace; o doctor não marca como falha o que só falta para o perfil de dev (o servidor
  sabe os papéis do dono do executor).

## Especialista e tarefas para PO / gestor de problemas
- Contexto do caso traz o **perfil de quem pediu** (papéis). Com management sem papel de dev a skill
  `especialista-de-gestao` (e `tarefas-de-gestao`) **não** usa git, gh, código, banco do cliente nem AWS CLI; usa Base
  Solvace (`prmake_base_search/get/impact`), engenharia reversa, ferramentas do PRMake, status de PR pelo PRMake, retrato
  de deploy já gravado (`prmake_aws_status`), `validar-ambiente`/`reproduzir bug` (navegador com a senha da máquina) e as
  ferramentas de DevOps/épico/histórias.
- MCP novo `prmake_epic_monitor(epic)` — o acompanhamento abaixo em texto.

## Acompanhamento (monitorar entregas)
- `GET Epics/{n}/monitor` → por card do épico: `{card, title, type, state, column, assignedTo, prs:[{repository, number,
  url, title, status: open|merged|closed, isDraft, targetBranch?, mergedAt?}], environments:[{name, status:
  deployed|pending|failed|unknown, at?}] | null, awsCollectedAt?, updatedAt}`. PRs do registro do PRMake (sem forçar
  GitHub; usa o cache de status), ambientes do `AwsCardSnapshots` (sem retrato → null). Uma chamada para o épico inteiro
  (lotes internos), sem N+1.
- Front: aba **Cards** ganha a visão "Acompanhamento" (padrão para management): por card, estado/coluna, PRs com status e
  branch de destino, ambientes como selos (✓ implantado / ⏳ aguardando / ✕ falhou / — sem retrato) e "retrato de <data>".
  Resumo no topo: "N cards · X com PR mesclado · Y em produção". Legenda clara.

## Histórias e bugs no PRMake (aba nova "Histórias")
- Entidade `EpicStory` (schema `execution`, migração `EpicStories0099`): Id, Number (sequência global — `#n` resolve
  `{kind:"story"}`), EpicId, Type (`user-story|bug`), Title (≤ 300), Description (md), AcceptanceCriteria (md),
  ReproSteps (md), Priority (int?), Severity?, Tags (jsonb), Fields (jsonb `{ref: valor}` — área, iteração, responsável…),
  Status (`draft|ready|converted|discarded`), CardId?, ConvertedAt?, ConvertedByName?, CreatedBy/Name, CreatedAt,
  UpdatedAt, Version (xmin).
- REST: `GET Epics/{n}/stories`, `POST Epics/{n}/stories`, `GET|PUT|DELETE EpicStories/{number}` (DELETE = descartar),
  `POST EpicStories/{number}/convert {team?, board?, column?, lane?}` → cria o card no DevOps (tipo "User Story"/"Bug";
  mapeamento de campos configurável `DeskStoryFieldMap` no Skills Configurations, seed:
  `{"user-story":{"type":"User Story","description":"System.Description","acceptanceCriteria":"Microsoft.VSTS.Common.AcceptanceCriteria"},"bug":{"type":"Bug","description":"System.Description","reproSteps":"Microsoft.VSTS.TCM.ReproSteps"}}`;
  markdown → HTML), põe o card no épico (`created`), marca `converted` + `cardId`. Já convertida → 409
  `{code:"alreadyConverted", cardId}`. Convertida não é editável (409 `notEditable`). Tempo real `epicsUpdated`
  action `stories`.
- MCP: `prmake_epic_stories(epic)`, `prmake_epic_story_save(epic, number?, type, title, description?, acceptanceCriteria?,
  reproSteps?, priority?, severity?, tags?, fields?, status?)`, `prmake_epic_story_convert(number, column?, lane?)`
  (acting). O especialista de gestão escreve rascunhos e só converte quando a pessoa pede.
- Front: aba **Histórias** no épico (entre Board e Itens): lista (ícone US/Bug, #n, título, status, card convertido com
  link), editor (tipo, título, descrição, critérios de aceite **ou** passos de reprodução em markdown com prévia,
  prioridade, severidade no Bug, tags com sugestões, área/iteração/responsável pelos combos), "Converter em card" —
  some depois da conversão e vira "Card #123 ↗" (abre o painel do card).

## Fases

| Fase | O quê | Dono | Worktree / branch |
|---|---|---|---|
| R2-BA | Papel/acesso no back, Cime.Auth seed, executor neutro + doctor por perfil, monitor (REST+MCP), skills (persona) | agente | `prform.api-0099b` / `fix/0099-r2-acesso` |
| R2-BB | Histórias (domínio, migração, REST, MCP, conversão, seed `DeskStoryFieldMap`, contexto) | agente | `prform.api-0099c` / `fix/0099-r2-historias` |
| R2-FA | RoleService/guard, menu, redirecionamentos, "Abrir na Bancada" por papel, primeiros passos por papel, acompanhamento na aba Cards | agente | `prform-app-0099b` / `fix/0099-r2-acesso` |
| R2-FB | Aba Histórias (`app-epic-stories` + editor) | agente | `prform-app-0099c` / `fix/0099-r2-historias` |
| R2-Q | Mesclar, build, e2e | Claude | `fix/0099-mesa-ajustes` |
