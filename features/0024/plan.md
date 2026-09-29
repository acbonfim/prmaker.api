# Feature 0024 — Da análise à correção: plano de correção dinâmico, perguntas, chamados, PRs por repositório, Timeline completa e skills pelo PRMake

> Spec: [`spec.md`](./spec.md) · Status: [`status.md`](./status.md)
> Branch: `feature/0024` no backend (`../prform.api-0024`) e no front (`../solvace.prform.web/prform-app-0024`, criado ao começar o front), a partir de `master` (já com a 0023). Skills em `~/.claude/skills` — a partir desta feature com fonte no repositório (ver 2.8).

## 1. Levantamento (2026-09-28)
- **0023 em produção**: plano único por execução (`ExecutionPlan` + etapas + logs + arquivos), status do plano (pendente/andamento/pausado/concluído/falhou/cancelado), etapas sem dono nem dependência, `control`/`wait` para pausa, tempo real `execplan:{card}`. A skill cria 7 etapas fixas e termina ao publicar na Timeline.
- **PRs no PRMake**: `POST /PullRequest/{card}/github` (usado pela `gerar-prmake`) abre o PR `branchPrefix+branchName → targetBranch`, registra em `PullRequestGithub` (repositório, branches, número, URL, **status open/merged/closed**, `StatusSyncedAt`) e já escreve na Timeline. O módulo GitHub sincroniza o status (inclusive `Merged`) — dá para concluir etapas pelo merge sem nada novo no GitHub.
- **Área do card**: `System.AreaPath` já vem do Azure (`card.json` da skill) → decide o fluxo de branches (produção × release).
- **Integrações pessoais (0002)**: `IPluginConfigurationResolver` + tela "Minhas integrações" (GitHub/Azure com a credencial do próprio usuário) — base para uma integração Freshservice.
- **Freshservice**: o link "público" (`https://solvace.freshservice.com/public/tickets/<hash>`) **redireciona para o login** da Freshworks (testado sem sessão) — o PRMake não consegue ler nada por ele. Status só pela API (`GET /api/v2/tickets/{id}`, Basic `apikey:X`), que exige o **número** do chamado; o hash do link público não é o número e a API não busca por ele.
- **Skills**: sem segredos embutidos (tokens em `~/.claude/*.txt`); `analisar-bug` tem 13 MB só por causa do `.venv` (`python-tds`) — o pacote não leva o `.venv`, a instalação cria.

## 2. Decisões
### 2.1 Dois planos ligados: análise → correção
- `ExecutionPlan.Phase` = `analysis` | `correction`; `ParentPlanId` (correção → análise). Um card pode ter vários (histórico), mas a tela mostra o **par atual**: abas **Análise** e **Correção** (a de correção vira a padrão quando existe; a análise segue consultável — req. 4).
- O plano de análise ganha a etapa final **`propor-solucoes`** (depois de `publicar`): a skill escreve as opções de solução (markdown, com prós/contras e riscos) e **faz as perguntas**; a etapa fica `waiting` até as respostas. Respondidas → a skill monta o **plano de correção** a partir da solução escolhida (não fixo — req. 1/2).

### 2.2 Etapas com dono, tipo, dependências e espera
- `ExecutionStep.Executor` = `claude` | `user` (badge 🤖/👤 na tela); `Kind` = `task` | `code` | `pr` | `ticket` | `question` | `validation`; `Repository` (etapas de código/PR); `DependsOn` (keys; jsonb).
- Status novo **`waiting`** (aguardando algo externo: resposta, chamado, merge) com `StatusReason` ("Aguardando o chamado #1234 — Em andamento"). Não é pausa do plano: o plano segue `running` e as **etapas sem dependência podem avançar** (req. 3). Etapa `pending` com dependência não concluída aparece "bloqueada por X".
- O **usuário** conclui/inicia/pula etapas pela tela (`POST steps/{key}/complete|start|cancel`), típico das etapas `executor=user` (ex.: abrir o chamado). A skill vê pelo `control` (devolve também `waitingSteps`, `readySteps` e `completedByUser`) e escolhe a próxima etapa pronta.

### 2.3 Perguntas (responder no PRMake ou no Claude)
- `ExecutionQuestion`: plano/etapa, texto (markdown), `Options` (lista, opcional, com "recomendada"), `AllowFreeText`, `Answer`, `AnsweredBy`, `AnsweredVia` (`prmake` | `claude`), status `open|answered|cancelled`.
- Tela: faixa de destaque "**N perguntas aguardando você**" no topo da seção; cada pergunta com botões das opções + texto livre. Tempo real para os dois lados.
- Skill: `ask` (publica as perguntas e as mostra no terminal), `wait-answers` (bloqueia até responder — pela tela ou pelo terminal); resposta dada no terminal → `answer` grava no PRMake (`via=claude`) para as duas pontas ficarem iguais.

### 2.4 Links e chamados (Freshservice)
- `ExecutionLink`: etapa, `Kind` (`ticket` | `pr` | `doc` | `other`, detectado pela URL), `Url`, `Title`, `ExternalId` (número do chamado/PR), `ExternalStatus`, `StatusCheckedAt`, `BlocksStep`, quem adicionou. Tela: "Links" em cada etapa (adicionar/remover, abrir, status).
- **Chamado de script**: etapa `kind=ticket`, `executor=user` — a skill prepara o texto e o `.sql` do chamado (arquivos do plano, copiáveis); o usuário abre no Freshservice e cola o link (e o **número**). Com `BlocksStep`, a etapa fica `waiting` até o chamado ser resolvido/fechado → aí conclui sozinha (log + Timeline). As outras etapas seguem.
- **Status do chamado** — ⚠️ decisão em aberto (Q-a): proposta = integração pessoal **Freshservice** (domínio + API key do próprio usuário, em "Minhas integrações"); o PRMake consulta `GET /api/v2/tickets/{número}` com a chave de quem colou o link. Sem chave cadastrada: o link fica guardado e o usuário marca "Chamado concluído" na tela. O link público continua salvo para consulta humana.
- Consulta: ao abrir o plano e a cada `control` da skill, com cache de 5 min por link (Cloud Run não tem processo em segundo plano confiável) + botão "Atualizar status".

### 2.5 PRs por repositório e conclusão pelo merge
- Plano de correção: **uma etapa `pr` por repositório** (req. 5), com os PRs dela como links `kind=pr` (ex.: `edv-solvace` → `development` ✓ mesclado, `qa` ⏳ aberto). A etapa conclui quando **todos os seus PRs estão mesclados**.
- A skill abre os PRs **pela API do PRMake** (`POST /PullRequest/{card}/github`, como a `gerar-prmake`) → ficam no card, na Timeline e com status sincronizado; o link na etapa guarda o id do `PullRequestGithub`.
- Merge detectado pela sincronização de status que o PRMake já faz (ao abrir o card, ⟳ da lista de PRs e — novo — no `control` da skill e ao abrir o plano, com cache de 5 min). **Plano de correção concluído = todos os PRs do plano mesclados** e nenhuma etapa pendente (req. 6) → status `completed` automático + registro na Timeline.
- **O Claude nunca faz merge** (regra na skill e fora de qualquer etapa `executor=claude`).

### 2.6 Fluxo de branches na skill (req. 7)
Regras na `SKILL.md` (e um `branch-plan.sh` que só **lê** o git e imprime o plano de branches/PRs, para a skill e o usuário conferirem):
| Caso (pela área do card e pelo repositório) | Base | Branches | PRs |
|---|---|---|---|
| Legado `edv-solvace` ou revamp backend, **produção** (`...\Product Development Team`) | `master` | `hotfix/<card>` (correção) → `hotfix/<card>-dev` (de `development` + cherry-pick) → `hotfix/<card>-qa` (de `qa` + cherry-pick) | `-dev → development`, `-qa → qa` (**2 PRs**) |
| **Release/regressão** (`...\Release Management`) | `release-version` **ou** `hotfix-version` — **sempre perguntar** | `hotfix/<card>` | `hotfix/<card> → <base>` (1 PR) |
| Revamp frontend `edv-solvace-apps`, produção | `master` | como o legado | `development`, `qa` |
| Revamp frontend `edv-solvace-apps`, release/regressão | `edge` (padrão; perguntar) | `hotfix/<card>` | `→ edge` |
- Commit e título do PR: `AB#<card> <resumo>`; branch **sem** `AB#` (`hotfix/<card>`). A skill confere com `git ls-remote` que as branches base existem e **pergunta** quando a área não bate com nenhum caso ou a base de release não está clara.

### 2.7 Timeline conta tudo (req. 8)
O backend escreve na Timeline nos marcos do plano (vale para ações feitas pela tela e pela skill; autor = quem agiu):
- análise concluída (a skill segue publicando a análise completa) · perguntas feitas (lista) · respostas (quem, onde) · **plano de correção criado** (etapas, quem executa, o que falta) · etapa de correção concluída/cancelada/aguardando (com motivo) · link de chamado adicionado e mudança de status do chamado · PR mesclado (a abertura o PRMake já registra) · **plano concluído** (resumo: o que foi analisado, feito, PRs e chamados).
- Um registro por marco (sem editar os anteriores); etapas de análise não geram registro individual (a análise publicada já cobre).

### 2.8 Skills baixadas pelo PRMake, sempre atualizadas (req. 9)
- **Fonte única no repositório do backend**: `skills/<nome>/` (`analisar-bug`, `gerar-prmake`, `gerar-handover`, `prmake-timeline`), cada uma com `SKILL.md`, `scripts/` e `skill.json` (nome, descrição, requisitos: `jq`, `python3`, AWS CLI opcional). `~/.claude/skills` passa a ser **instalação**, não fonte (as mudanças da 0023 entram no repo nesta feature).
- **Publicação a cada deploy**: o `Dockerfile` copia `skills/` para a imagem; a API serve (com x-api-key, nada público):
  - `GET api/v1/Skills` — lista com versão (hash do conteúdo + data do commit);
  - `GET api/v1/Skills/{nome}/package` — `.zip` da skill (sem `.venv`);
  - `GET api/v1/Skills/install.sh` — instalador.
- **Instalação (um comando, copiado da tela "Skills" do PRMake)**: `curl -fsSL -H "x-api-key: <sua api-key>" https://api.softhouse.app.br/api/v1/Skills/install.sh | bash` → baixa as skills para `~/.claude/skills/`, cria `~/.claude/prmake-token.txt` se não existir, roda o `setup` de cada uma (ex.: `.venv` da `analisar-bug`) e **pergunta** se instala o hook de atualização.
- **Atualização automática**, em duas camadas: (1) hook `SessionStart` em `~/.claude/settings.json` que roda `prmake-skills update --quiet` a cada sessão do Claude Code (só baixa se a versão mudou); (2) cada skill roda `self-update` no passo 0 — atualizou → relê a `SKILL.md`. Arquivo local alterado à mão (hash ≠ manifesto) → **não sobrescreve**, só avisa (`--force` para substituir).
- Tela "Skills" no PRMake (menu do usuário): lista, versão instalada × publicada (quando informada pelo `update`), comando de instalação com a api-key do usuário (a mesma do "Minha API key"), download do `.zip`.

## 3. Fases
| Onda | Fases |
|---|---|
| 1 | B1 (modelo + API: fases, dono, tipo, dependências, `waiting`, perguntas, links, ações do usuário) · B3 (skills no repo + endpoints + instalador) |
| 2 | B2 (automação: PRs mesclados → etapa/plano; Freshservice; Timeline nos marcos) · S1 (`prmake-plan.sh`: `ask`/`wait-answers`/`answer`/`link`/`correction`, `control` estendido) · S2 (`SKILL.md` da analisar-bug: propor soluções, plano de correção, fluxo de branches, nunca merge, Timeline) · S3 (`self-update`, hook, `setup`) · F1 (abas Análise/Correção, dono/tipo/dependência/aguardando) · F2 (perguntas) · F3 (links, chamados, PRs da etapa, ações do usuário) · F4 (tela Skills) |
| 3 | T1 (ponta a ponta local: análise → perguntas respondidas na tela e no terminal → plano de correção → PRs em repo de teste → chamado bloqueando uma etapa com outra avançando → merges simulados → conclusão; instalação/atualização da skill numa HOME temporária) |
| 4 | Q1 (PRs e deploy — o merge é do usuário) |

## 4. Em aberto (confirmar antes da B2/F3)
- **Q-a Freshservice**: integração pessoal com API key (proposta) × chave única da empresa (plugin admin) × sem consulta (só link + marcar concluído). Em qualquer caso o usuário informa o **número** do chamado junto com o link público.
- **Q-b** Revamp backend (`revamp-*`): mesmo fluxo do legado (`master` → `development` + `qa`)? Algum módulo com branches diferentes?
- **Q-c** Hook `SessionStart` de atualização: instalar por padrão ou só quando o usuário aceitar no instalador (proposta: perguntar)?
