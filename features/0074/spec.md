# 0074 — Histórico de versões legível + promoção de PRs em lote (release/hotfix)

## Pedido
**A. Histórico de versões do plano.** Toda análise/correção gera uma versão do plano do card e hoje o botão de histórico
abre um menu confuso. Melhorar: título claro, número da versão, quem criou/alterou, data e hora, avatar, lista bem
organizada.

**B. Promoção de PRs em lote.** Quando os cards entram numa release ou hotfix, o Release Manager pede o PR de cada card
para branches específicas (ex.: `hotfix-version`; `release-version` + `master`; `release-version` + `edge` + `master`
como rascunho). Hoje isso é feito à mão, card a card. Queremos:
1. um fluxo que, a partir da **branch principal do card** (`hotfix/<card>`, aberta da master ou de uma release), faz
   **cherry-pick** do que foi implementado e abre o PR para cada destino pedido;
2. **abrir em lote**: tela onde se informa os cards, o PRMake sugere repositório e destinos, o usuário ajusta, dispara, e
   no fim recebe uma **tabela com todos os PRs abertos**, com "copiar como texto" e "copiar como tabela" (como no
   registro do PR);
3. **colar a mensagem do Release Manager** e a IA do usuário preencher essa tela (versão, regras de destino, cards); o
   usuário revisa e só então dispara.

---

## A. Histórico de versões

### Hoje
`execution-plan.component.html` (~linha 78–100): botão `history` aparece com mais de 1 plano e abre um `mat-menu` com
bolinha de status, título, `status · etapas · data`. Sem número da versão, sem autor, sem separar Análise × Correção, sem
mostrar de qual análise veio a correção. O dado vem de `ExecutionPlanSummary` (`createdBy` é só texto; `Version` no back é
o `xmin` de concorrência, **não** é número de versão).

### Como fica
Um **painel de histórico** (popover ancorado no botão; em tela estreita vira folha inferior de largura total) com:
- **Título**: "Versões do plano" + subtítulo "Card 77317 · 5 versões (3 análises, 2 correções)".
- **Filtro** por fase: Todas · Análise · Correção.
- **Lista da mais nova para a mais antiga**; cada linha mostra:
  - badge da **versão** por fase: `Análise v3`, `Correção v2` (1 = a mais antiga do card naquela fase) e a etiqueta
    "mais recente" / "em exibição";
  - **avatar** de quem criou (foto da conta; sem foto, iniciais — `app-user-avatar`) e o nome;
  - **criada em** `dd/MM/yyyy HH:mm` (e "há 2 h" como apoio), e, se o status foi alterado por outra pessoa ou depois,
    "alterada por <nome> · <data hora>" (`statusChangedBy` + `updatedAt`);
  - **status** (chip colorido), progresso `5/8 etapas` com barra curta e, quando terminou, a duração;
  - para correção: "gerada da Análise v2" (usa `parentPlanId`), clicável.
- Clique na linha abre aquela versão (mesmo comportamento de `selectHistory`); a atual fica destacada.
- Teclado: setas, Enter e Esc; leitor de tela com `role="listbox"`/`aria-selected`.
- Estados: carregando, vazio, erro com "tentar de novo". Lista rola dentro do painel (altura máxima).

### Backend
`ExecutionPlanSummary` ganha `versionNumber` (calculado na consulta do histórico: posição do plano entre os do mesmo card
e fase por `CreatedAt`; **sem migração**) e `createdByUserId` (já existe na entidade) para a foto. A foto vem de
`authService.getPhotosByExternalIds`, como na Timeline do card.

---

## B. Promoção de PRs em lote

### Vocabulário
- **Branch principal**: a branch do card (`hotfix/<card>` por `BranchNamePattern`, ou `bugfix/<card>`), criada da master
  (bug de produção) ou da release (bug de release).
- **Destino**: branch para onde o Release Manager pede o PR (`hotfix-<versão>`, `release-<versão>`, `master`, `edge`…).
- **Promoção**: card × repositório × destino → um PR.
- **Lote**: conjunto de promoções disparadas juntas, com a **versão** informada (ex.: `3.274.10`).

### B1 — Promoção de um card (cherry-pick)
Para cada destino da promoção:
1. **Commits** = os da branch principal desde o ponto de corte (`merge-base` com a base), em ordem, sem merges.
2. Cria a branch `<prefixo>/<card>-<destino>` **a partir de `origin/<destino>`**, `git cherry-pick -x` dos commits e `push`.
3. Abre o PR pelo próprio PRMake (`POST /PullRequest/{card}/github`): título pelo `PrTitlePattern` (`AB#{card} {TARGET}`),
   descrição/RCA já registrados do card, `Draft` conforme o destino. Fica registrado no card como qualquer outro PR.
4. Modo alternativo por destino: **PR direto** (sem cherry-pick), para quando a branch principal já nasceu do destino.

Regras de segurança (inegociáveis):
- **Nunca mescla** PR (merge é humano), nunca `push --force`, só cria branches novas (nome já existe → sufixo `-2`).
- Roda num **worktree próprio** (`.prmake-wt`, como a 0067): o clone de trabalho e a branch em que ele estiver não são tocados.
- **Conflito no cherry-pick**: aborta aquele destino (`git cherry-pick --abort`), marca `conflito` com a lista de arquivos
  e segue com os outros destinos. O usuário resolve depois ("Retomar no Claude" no card) e reenvia só aquele destino.
- **Idempotente**: já existe PR aberto do mesmo card/repositório/destino → `ja_aberto` (mostra o link), não duplica.
- Destino inexistente (a release ainda não foi cortada) → `destino_inexistente`, nada é criado.

**Onde roda.** O GitHub não tem API de cherry-pick e o servidor não tem clone. Roda na máquina do usuário pelo executor
da 0039 (`Cime.ExecutionAgent`, fila `ExecutionRequest`) com um novo tipo de pedido `promote`; o servidor só enfileira,
acompanha e abre o PR via API. Sem executor online, a tela avisa e oferece o PR direto onde for possível.

### B2 — Tela "Promoções" (lote manual)
Rota nova (ex.: `/promocoes`, item no menu). Cabeçalho: **Versão** (`3.274.10`) e **Tipo** (Hotfix | Release).
Grade, uma linha por card:

| Card | Título (DevOps) | Repositório | Perfil | Destinos (chips editáveis) | Origem |
|---|---|---|---|---|---|

- Inserir o número do card (ou vários colados): o PRMake busca título/área/responsável no DevOps e os **repositórios com
  commits na branch principal** do card (PRs/branches já registrados). Card com vários repositórios vira várias linhas.
- **Perfil de destino** sugerido pelo tipo do repositório (`BranchStrategy.repositories[].kind`):
  Netcore/legado → `hotfix-{versão}`; API de Integrações → `release-{versão}`, `master`; Angular/revamp-frontend →
  `release-{versão}`, `edge`, `master` (rascunho). **Nada fixo no código**: os perfis ficam em Skills Configurations
  (`PromotionProfiles`, semeado por migração; o admin edita), como o `BranchStrategy`.
- O usuário pode **trocar o perfil, adicionar/remover destinos e marcar rascunho** por linha, antes de disparar.
- Validação antes de disparar (linha vermelha, botão bloqueado): destino não existe no repositório, card sem commits na
  branch principal, executor offline, card duplicado no lote.
- **Resolução do nome**: `release-{versão}` vira o nome real consultando as branches remotas (`^(release|hotfix)[-/]v?<versão>$`,
  sem diferenciar maiúsculas); se houver mais de uma candidata, o usuário escolhe na linha.
- Botão **Disparar lote**: cria o lote e enfileira. Pode fechar a tela; o processamento segue em segundo plano.

### B3 — Colar a mensagem do Release Manager (IA)
Botão "Colar mensagem do Release Manager": caixa de texto (exemplo no fim) → **Interpretar**. O resultado **preenche a
tela da B2**; nada é disparado. O usuário revisa e usa o mesmo botão **Disparar lote**.
- **Cartões**: extraídos de forma **determinística** (linhas `ID  Tipo  Título` + responsável na linha seguinte), sem IA, e
  conferidos no DevOps (ID inexistente = linha vermelha).
- **IA** (provedor/modelo/chave **do próprio usuário**, plugin pessoal; Haiku basta) interpreta só o **texto livre**:
  versão (`V3.274.10` → `3.274.10`), tipo (hotfix/release) e as **regras de destino por tipo de ticket** ("Netcore:
  hotfix-version", "API de Integrações: release-version e master", "Angular: release-version, edge, master (draft)").
  Devolve JSON validado por esquema; as regras viram **perfis só desse lote**, sobrepondo os padrões da B2.
- Prompt em **AI Configurations** (editável no PRMake), nunca no código. Uma chamada por colagem; a tela mostra que gasta
  a chave do usuário (regra de custo da IA do servidor). IA indisponível/sem chave: usa os perfis padrão e avisa.
- A IA **não executa nada** e nada que ela devolva vira comando: só preenche campos que o usuário revisa.

### B4 — Acompanhamento e tabela final
- Progresso ao vivo (SignalR já existente): por linha `na fila → cherry-pick → push → PR aberto`, ou
  `conflito | ja_aberto | destino_inexistente | erro` com a mensagem; resumo no topo (`8 abertos · 1 conflito · 1 erro`).
- Ao terminar, **tabela final** com Card | Título | Repositório | Origem → Destino | Rascunho | PR (link) | Status.
  - **Copiar como texto** e **Copiar como tabela** (HTML + texto, `ClipboardService`, igual ao registro do PR); só os PRs
    abertos (e opção "incluir conflitos/erros").
  - Texto no formato que se cola no chat do Release Manager: `AB#77317 · Gerdau - Araçariguama… · release-3.274.10 · <url>`.
- **Reenviar** só as linhas com conflito/erro. Histórico dos lotes (quem, quando, versão) para consultar depois.
- Cada PR aberto registra uma entrada na **Timeline** do card (gravação sempre pelo PRMake).

---

## Fora de escopo
- Mesclar PRs; criar as branches de release/hotfix (só valida que existem); repositórios do Azure DevOps (só GitHub).
- Resolver conflito de cherry-pick sozinho com IA (fica para uma próxima; hoje só reporta e deixa retomar no Claude).
- Transformar a promoção em "plano de execução" da tela do card: o plano hoje só admite as fases análise/correção
  (`ExecutionPlan` valida a fase e a tela do card assume o par análise/correção). O lote tem progresso próprio; reavaliar depois.

## Decisões em aberto (com o padrão adotado)
1. **Nome real das branches**: `release-version` é `release-3.274.10`? Padrão: descobrir pelo remoto (regra da B2) e
   perguntar na linha se houver ambiguidade.
2. **Nome da branch do cherry-pick**: `hotfix/<card>-<destino>`. Confirmar o padrão de nome que o time espera.
3. **Cherry-pick × PR direto como padrão** por perfil: padrão cherry-pick; PR direto só quando o usuário marcar.
4. **Quantos cards por lote**: limite de 30 e, por repositório, um de cada vez no executor (evita corrida de worktree).

## Aceite
**Histórico**
1. Card com 3 análises e 2 correções: o painel lista 5 versões, com `Análise v3 … Correção v1`, avatar e nome de quem criou,
   data e hora, status e progresso; a correção mostra "gerada da Análise v2".
2. O filtro por fase, o teclado e a folha inferior no celular funcionam; clicar abre a versão e destaca a atual.

**Promoção**
3. Informar 3 cards manualmente (1 Netcore, 1 API de Integrações, 1 Angular) com versão `3.274.10`: a tela sugere
   `hotfix-3.274.10`; `release-3.274.10` + `master`; `release-3.274.10` + `edge` + `master (rascunho)`. Disparar abre 6 PRs.
4. Cada PR contém só os commits do card (sem trazer o histórico da master) e o rascunho sai como rascunho.
5. Um destino com conflito vira `conflito` com os arquivos; os demais seguem; reenviar só ele funciona.
6. Disparar de novo o mesmo lote não duplica PRs (`ja_aberto`).
7. Colar a mensagem de exemplo preenche versão, regras e os 12 cards; a IA não abre nenhum PR; o usuário edita e dispara.
8. A tabela final copia como texto e como tabela (cola formatada no e-mail/Teams) e lista só os PRs abertos.
9. Sem executor online: a tela avisa antes de disparar. Nada é mesclado e nenhum `push --force` acontece em nenhum cenário.

### Exemplo de mensagem do Release Manager
```
Estou iniciando os preparativos para o hotfix V3.274.10
Podem enviar o pr do ticket para o hotfix?
Branch para ticket Netcore: hotfix-version
Branch para tickets com API De Integrações: release-version e master
Branch para tickets Angular: release-version, edge, master (draft)

ID    Work Item Type    Title    Assigned To
77317    Bug    Gerdau - Araçariguama - Documentation - Botão "mostrar conteúdo"…
Alex Carlos
76607    Bug    EFESO Sandbox - Checklist - Create New Version not working
Alex Carlos
```
