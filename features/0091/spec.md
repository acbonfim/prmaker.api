# 0091 — O que o item gerou (entregas do item e das sub-tarefas)

Pedido (2026-11-06):

1. Registrar e exibir no item de trabalho, e nas respectivas sub-tarefas, **o que foi gerado** por ele.
   - Ex.: a sub-tarefa era abrir um PR → ao clicar na sub-tarefa e abrir o modal, ter anexado o PR aberto de fato.
   - Ex.: alteração de código → exibir todo o código alterado e a branch criada.
2. Verificar os outros casos que também entram nessa situação.

## Diagnóstico (2026-11-06)

Caso real: card 77988, item #56 (implementação, 10 sub-tarefas). 7 concluídas — código em 2 repositórios, script +
chamado, 2 de PR — e **todas com `output: {}` e o item com `links: []`**.

- A sub-tarefa já tem `Result` (markdown), `Output` (JSON livre) e `Undo`; o item tem `Links`. Mas:
  - **Sub-tarefa espelho de etapa do plano** (todas as de análise/correção) conclui pela etapa (`prmake_advance`):
    nada vai para `Output`. As skills dizem para não chamar `prmake_work_task` nesses itens.
  - O PR aberto fica em `prform.PullRequestsGithub` (só por card) e em `execution.ExecutionLinks` (plano + etapa),
    sem ligação com a sub-tarefa.
  - **Código alterado não fica em lugar nenhum**: os commits ficam no worktree local (`branches`), o PR leva para
    `-dev`/`-qa` por cherry-pick, e o worktree é apagado no fim.
  - Timeline, horas lançadas, mudança de estado/ação no DevOps, comentário na discussion e texto do PR ficam só no
    card (ou só no Azure).
- O modal da sub-tarefa mostra `Saídas` como tabela chave/valor crua; o do item só lista `links` como `<a>`.

## O que cada tipo de sub-tarefa gera (os "outros casos")

| Sub-tarefa (`kind`) | O que gera | De onde vem |
|---|---|---|
| `code` | branch de trabalho (repo, base), commits, **diff completo** (arquivos, +/-) | novo: `prmake-plan.sh deliver-code` envia o diff do worktree |
| `branch` | branch criada (repo, base, sha) | `output.branch` / `deliver-code` |
| `cherry-pick` | branch de destino + commits levados | `output.commits` / `deliver-code` |
| `pr` | PR (repo, #, origem → destino, estado ao vivo, checks, aprovações; "já existia") | abertura do PR (`PullRequest/{card}/github`, `prmake_pr_open`, `open-pr`) + `ExecutionLinks` da etapa |
| `pr-text` | texto do PR (descrição + RCA) | `save-pr-text` (`PullRequests`) |
| `pr-review` | revisão/comentários no PR | link do PR + resultado |
| `devops-state` / `devops-action` | estado de → para, ação aplicada | executor de ações do DevOps |
| `comment` | comentário publicado na discussion do card | executor de ações do DevOps |
| `sql-script` | script + texto do chamado | arquivos do plano/item (já existem) |
| `sql-query` | resultado da consulta | arquivo de dados (já existe) + resultado |
| `ticket` | chamado (link, sistema, situação aberto/resolvido) | `output.ticket` / `ExecutionLinks` kind `ticket` |
| `notify` | quem foi avisado e onde | `output` |
| `validation` | build/testes (resumo) e evidências | resultado + arquivos |
| `card-brief` | `resumo-do-pedido.md` | arquivo do item (já existe) |
| `research` / `decision` / `task` / `manual` | texto do resultado, arquivos | resultado + arquivos |
| item `analysis` | análise publicada na Timeline, documento da análise | Timeline + arquivo |
| qualquer | entrada na Timeline, horas lançadas | `prmake_timeline` / `prmake_devops log-hours` durante a sub-tarefa |

## Decisões (tomadas na implementação — revisar)

- **Entregas** = um registro por coisa gerada (`execution.WorkItemDeliverables`), ligado ao item e à sub-tarefa.
  Tipos: `pr`, `branch`, `commit`, `code`, `ticket`, `pr-text`, `devops-state`, `comment`, `timeline`, `hours`,
  `notify`, `doc`. Arquivos continuam na tabela de arquivos (não duplicam) e aparecem junto, na mesma seção.
- **Gravação automática no servidor**, sem depender do Claude lembrar:
  - abrir PR (tela, skill, MCP) → entrega `pr` (+ `branch` de origem) na sub-tarefa;
  - Timeline, horas, estado/ação e comentário do DevOps → entrega na sub-tarefa;
  - a sub-tarefa é a **informada** (`taskNumber` / `?workItemTask=`) ou a **em andamento no card** de tipo compatível
    (PR → `pr`; DevOps → `devops-state`/`devops-action`; Timeline → `notify`/`comment`), a mais recente; vindo do Claude
    (MCP/skill), sem compatível, a **única** em andamento. Pela tela (pessoa clicando), só a compatível. Sem nenhuma →
    **não grava** (o card já tem o registro: Timeline, PRs).
- **Código**: `prmake-plan.sh deliver-code <repo> [--task n]` no worktree, no fim da etapa/sub-tarefa `code`: branch,
  base (merge-base), commits e o **diff completo** gravado (fotografia — a branch pode sumir depois do merge). Limite
  de 2 MB por diff; acima disso grava cortado e a tela oferece o compare no GitHub. O diff vai **direto do script para a
  API** (não passa pelo contexto do Claude). Concluir uma etapa `code` da correção sem diff registrado devolve um
  **aviso** no `prmake_advance` (não recusa — análise também tem etapas `code` sem alteração).
- **Leitura retroativa** (itens antigos, como o #56): na leitura, além das entregas gravadas, entram as deduzidas de
  `Output`/`Links` da sub-tarefa, dos `ExecutionLinks` da etapa espelho e, para sub-tarefa `pr` sem nada, dos PRs do
  card no mesmo repositório abertos durante a sub-tarefa (marcadas "deduzido").
- **Estado ao vivo**: o PR mostra o estado da `PullRequestsGithub`/`ExecutionLinks` (aberto/mesclado/fechado, checks,
  aprovações); chamado mostra aberto/resolvido.
- **Desfazer**: sub-tarefa desfeita mantém as entregas, marcadas "desfeito" (PR fechado, branch apagada).
- **Tela**:
  - modal da sub-tarefa: seção **"O que foi gerado"** logo após a descrição — cartão por entrega (PR com estado e
    "Ver alterações", branch com link e base, código com arquivos e +/- e o diff completo expansível, chamado,
    Timeline, horas, DevOps) + os arquivos da sub-tarefa. A tabela crua de saídas vira "Dados brutos" recolhido;
  - modal do item: aba nova **"Entregas (n)"** com tudo do item agrupado (PRs, branches, código, chamados, arquivos,
    card), cada entrega com o `#n` da sub-tarefa; a Visão geral mostra o resumo (PRs com estado);
  - popover `#nn` e sessão de execução usam as entregas (no lugar de `pullRequestLinks`).
- **Diff** carregado sob demanda (`GET .../deliverables/{id}/diff`) — nunca no GET do item (regra 0070). PR/branch sem
  fotografia: compare ao vivo no GitHub (repositório informado); sem a integração do GitHub ou branch apagada → `error`
  legível com o link do PR. PRs do card para a leitura retroativa vêm do banco (`PullRequestsGithub`), sem o GitHub.
