# 0085 — Ferramentas de dia a dia do Especialista (análise dos cards tratados)

> Análise **somente leitura** em 2026-11-05. Fontes: cache `~/.prmake/cards/*` (34 cards) e API do PRMake em produção
> (só GET): `Home/cards` (mine + participated), `ExecutionPlan/card/{card}` (68 planos de 30 cards),
> `ExecutionPlan/{id}` (30 planos de correção, 145 etapas, links e comentários), `PullRequest/{card}/github` (55 PRs),
> `CaseChat/{card}` (4 cards com conversa), `Promotion` (1 lote), `Skills/config`, `Azure/actions/config`,
> `Azure/actions/classifications`. Nenhuma escrita em lugar nenhum.

## 1. Universo

| Recorte | Quantidade |
|---|---|
| Cards com plano | 30 (29 Bug, **1 US** — 77988, só análise) |
| Área (cache `dados/card.json`) | 31 Product Development Team (fluxo `producao`), 2 Release Management (fluxo `release`; 76607 e 77434, que **passaram** para lá em Release Candidate) |
| Planos | 38 análises + 30 correções (várias com v2/v3: 75648, 76143, 76327, 76607, 77434, 77697, 77900) |
| Cards com PR | 14 · repositórios: `edv-solvace` (legacy) 7, `edv-solvace-apps` (revamp-frontend) 6, `revamp-Users`/`revamp-RCA`/`revamp-Multilingual` (revamp-backend) 3, `edv-solvace-api` (integration-api) **0** |
| Cards sem código (fechamento, orientação, script, chamado) | 16 |
| Chamados Freshservice abertos pelo plano | 5 (75067, 75777, 76507, 76701, 77205) |
| Lote de promoção (0074) | 1 lote `hotfix 3.274.11`: 77698→hotfix-version (cherry-pick), 77434→release-version (já aberto), edge (cherry-pick), master (direto) |

**US:** só há uma (77988), ainda em análise, sem correção nem PR. Não há base empírica para ferramentas específicas de US;
as propostas abaixo marcam `type: ["bug","user-story"]` só quando a ação independe de RCA/root cause.

## 2. Ações recorrentes (o que de fato se repete)

### 2.1 PRs por destino (55 PRs)

| Tipo de repo | Destino | Origem usada | Qtde (status) | Cards |
|---|---|---|---|---|
| legacy | development | `hotfix/<card>-dev` | 7 merged | 75067, 76143, 76327, 76607, 77317, 77697, 77698 |
| legacy | qa | `hotfix/<card>-qa` | 7 merged | idem |
| legacy | hotfix-version | `hotfix/<card>` direto (4) · `hotfix/<card>-hotfix-version` (1, lote) | 5 merged | 76143, 76327, 76607, 77317, 77698 |
| revamp-frontend | development | `-dev` | 5 merged + 1 closed | 74963, 75349, 75499(closed), 75669, 76327, 77434 |
| revamp-frontend | qa | `-qa` | 5 merged + 1 closed | idem |
| revamp-frontend | release-version | `-rv` (2) · `-RV` (1) | 3 merged | 75349, 75669, 77434 |
| revamp-frontend | edge | `-edge` | 3 merged | 75349, 75669, 77434 |
| revamp-frontend | master | `hotfix/<card>` direto | 2 merged + 1 open | 75349, 75669, 77434 |
| revamp-frontend | **hotfix-version** | `hotfix/<card>` e `-qa` | **4 closed (erro)** | 75349, 75669 |
| revamp-backend | development | `-dev` | 3 merged | 75648, 75669, 76051 |
| revamp-backend | release-version / edge / master | `-rv`/`-edge`/`-master` (75648) · `hotfix/<card>` direto nos 3 (76051) | 6 merged + **2 closed** (`-dev` → edge/rv, erro) | 75648, 76051 |

Leituras:
- **Par dev+qa** (fluxo `producao`) é a ação nº 1: 13 cards. revamp-backend só tem `development` (confere com `BranchStrategy`).
- **Promoção** (depois do QA / Release Candidate) é a nº 2: legacy → `hotfix-version` (5 cards); revamp → `release-version` +
  `edge` + `master` (5 cards: 75349, 75648, 75669, 76051, 77434). Exatamente o `PromotionProfiles` de produção.
- **Erros que a ferramenta deve impedir** (8 PRs fechados): PR para `hotfix-version` em revamp-frontend (perfil não tem esse
  destino), PR da `-dev` para edge/release-version (branch tem que nascer do destino), nome fora do padrão (`-RV`,
  `-hotfix-version`, `-release` sugerido pelo especialista no 77434), PR legacy→hotfix-version direto da branch principal
  (funcionou 4×, mas o 76607 deu conflito e precisou de commit de resolução).

### 2.2 Etapas dos planos de correção (145 etapas)

| Etapa (key) | Vezes | Observação |
|---|---|---|
| `fechar-card` | 29 | sempre: root cause, classificação, resumo PT/EN, mover o card (e zerar Remaining no sem-código) |
| `corrigir-<repo>` (code) | 19 | 1 por repositório |
| `pr-<repo>` (pr) | 19 | "PRs em <repo> (development e qa)" |
| `validar-qa` | 16 | pessoa valida em QA; libera Ready for QA |
| `validar` (build/testes) | 15 | |
| `impacto-na-base` | 15 | automática (0052) |
| `gerar-prmake` | 5 (+ embutido no fechamento em ~8) | descrição + RCA "sem abrir PR" |
| `validar-dados` | 5 | consulta só leitura depois do script |
| `preparar-script-*` + `chamado-*` (ticket) | 5 + 5 | script com rollback + texto do chamado → Freshservice |
| `resolver-conflito-release`, `validar-release`, `pr-release` | 2 | 77434 v2/v3 (promoção travada) |
| outros únicos | — | `conceder-permissao` (75648), `configurar-multilingual` (77852), `confirmar-75648`/`registrar-tratamento` (75703), `novo-resumo-rca` (76143) |

### 2.3 Fechamento (classificação + movimentação)

| Desfecho | Cards | Movimento do card |
|---|---|---|
| `code-fix` | 74963, 75349, 75499, 75669, 76051, 76143, 76327, 76607, 77317, 77434, 77697, 77698, (77900) | **Dev Test in QA** quando os PRs estão em QA (automático, `AutoDevTestInQa=true`); **Ready for QA** só com autorização / após `validar-qa`; 75349/77698 foram para Test in Production |
| `code-data-fix` | 75067 | Ready for QA após validar-qa |
| `script-user-action` | 76507, 76701, 77205 | **zero-remaining + Test in Production** (`testInProduction` → state Test QA) |
| `script-environment` | 75777 | zero-remaining + Test in Production |
| `configuration` | 77852 | zero-remaining + Test in Production |
| `user-education` (inclui pedido de .har) | 76590, 77290, 77510, 77696, 75648 v2 | zero-remaining + Test in Production (orientação ao cliente PT/EN na discussion) |
| resolvido por outro card (`duplicated`) | 75703 (ref. 75648) | Ready for QA autorizado |

Estimativa inicial (`initialEstimate` 6 h configurada) e "lançar horas": **sem evidência** nos planos/cards; só aparece na
configuração. `zero-remaining` aparece em ~9 fechamentos sem código.

### 2.4 Pedidos depois do plano (comentários do plano + conversa do especialista)

| Pedido | Cards | Vira ferramenta |
|---|---|---|
| Promover para release-version/edge com cherry-pick "no mesmo padrão" e abrir os PRs | 75669 (+ 77434 lote) | promoção |
| PR de promoção com **conflito** → branch nova do destino, cherry-pick, resolver, PR (ou commit de resolução no PR aberto) | 77434 (2 versões), 76607 | resolver conflito |
| PRs dev/qa com conflito porque as branches não nasceram de development/qa | 75499 | refazer PRs do fluxo |
| Texto novo de validação precisa de aprovação de Produto → usar **termo já existente** | 77434, 76143 | ajuste pós-revisão |
| Sonar barrando merge (duplicação 10,6% > 3%) | 76051 | corrigir Sonar |
| Novo comentário/resumo PT/EN **sem apagar** o anterior (call com cliente, novo root cause) | 75648, 76143 | novo resumo |
| Replicar a correção em outro módulo (core → revamp) | 76143 (desistiu → card novo), 75669 (front + back) | replicar correção |
| Conferir banco + Cognito do usuário antes de perguntar ao cliente | 75349 (+ Cognito em 6 análises; `00_consulta-usuario*` em 75349, 75777, 76507, 76701) | diagnóstico de usuário |
| Comparar com PR aberto por outra pessoa (turno contrário) | 77697 | revisar PR |
| Cancelar plano de teste / retomar plano cancelado sem querer | 77534, 77900 | **não** é item de trabalho (proposta do especialista) |

## 3. Achados que afetam as ferramentas

1. **`PromotionBranchPattern` em produção = `_{branch}{suffix}`** (com `_` na frente). A 0085 troca `{branch}-{target}` →
   `{branch}{suffix}` só quando o valor antigo é exatamente `{branch}-{target}`; o valor atual geraria `_hotfix/77698-hv`.
   Conferir/limpar no admin antes de usar a promoção ou os modelos.
2. Produção já tem `WorkItemTaskKinds`/`WorkItemTemplates` da 0085 (4 modelos, 9 tipos), **sem** a 0085b (`ticket`, `notify`
   e `input` nos params ainda não estão lá).
3. Sufixo do release: config `-rv`, ponto combinado no 77434 `-RV`, PR real `hotfix/77434-RV`. Os nomes das branches no
   GitHub diferenciam maiúsculas e minúsculas: padronizar (sugiro manter `-rv` e corrigir o ponto do especialista).
4. `{repository}` = `DefaultRepository` (`edv-solvace`). Num card de front (`edv-solvace-apps`) os modelos vêm com o repo
   errado. Sugestão: `{repository}` = repositório do PR principal do card (o primeiro `pr-<repo>`/PR registrado) e,
   havendo mais de um, a sub-tarefa abre com `input: repository` listando **os repos do card** primeiro.
5. `ajuste-pos-qa` atual abre PR `{branch}` → `{flowTargets}`: no fluxo `producao` os PRs saem de `-dev`/`-qa`, então o
   ajuste precisa ser **cherry-pick para as branches já abertas** (o PR existente se atualiza), não PR novo da principal.
6. Destinos válidos por repo já existem em `PromotionProfiles` + `BranchStrategy`. A ferramenta "PR para a branch X" deve
   oferecer **esses** destinos primeiro (e a lista de branches do repo via `GET Promotion/branches?repository=` só como
   "outra"), bloqueando hotfix-version em revamp-frontend.
7. Classificações válidas vêm de `GET Azure/actions/classifications` (14 opções: code-fix, code-data-fix, script-defect,
   script-user-action, script-environment, configuration, configuration-change-request, user-education,
   user-education-change-request, change-request, not-mapped-requirement, cannot-reproduce, no-user-feedback, duplicated).

## 4. Extensões necessárias no formato (mínimas)

Os modelos atuais aceitam `{key, title, description, type?, tasks[{title, kind, executor?, params}]}`. Para as
ferramentas abaixo proponho, **opcionais e retrocompatíveis** (o catálogo hoje repassa campos extras sem quebrar):

- `when`: `{ "type": ["bug"|"user-story"], "flow": ["producao"|"release"], "repoKinds": ["legacy"|"revamp-frontend"|"revamp-backend"|"integration-api"], "states": [...] }`
  — o catálogo/especialista só oferece a ferramenta quando bate (hoje o catálogo não filtra).
- `ask`: campos pedidos **uma vez** ao criar o item, referenciados nas sub-tarefas como `{ask:<key>}`;
  `{suffix:@<key>}` = sufixo do destino escolhido. Ex.: `{ "key": "target", "label": "Destino", "input": "target-branch", "options": "{promotionTargets}" }`.
- Marcadores novos: `{promotionTargets}` (destinos do perfil do repo), `{base}` (base do fluxo: master/release-version),
  `{qaBranch}` (`QaBranchPattern`), `{repoKind}`.
- Tipos de sub-tarefa novos (além dos 9 + `ticket`/`notify` da 0085b):

```json
[
  { "kind": "devops-action", "label": "Ação no DevOps", "icon": "assignment_turned_in", "executor": "claude",
    "description": "Ações do PRMake no card: root cause, resumo PT/EN, classificação, Remaining, estados configurados.",
    "params": [
      { "key": "action", "label": "Ação", "required": true, "input": "select",
        "options": ["rootcause", "summary", "classify", "zero-remaining", "initial-estimate", "dev-test-in-qa", "ready-for-qa", "test-in-production"] },
      { "key": "value", "label": "Classificação / texto", "input": "textarea" }
    ] },
  { "kind": "pr-text", "label": "Gerar PRMake (descrição + RCA)", "icon": "description", "executor": "claude",
    "description": "Mesmo caminho da gerar-prmake: descrição do PR e RCA pelo prompt configurado, sem abrir PR.",
    "params": [
      { "key": "repository", "label": "Repositório", "required": true, "input": "repository" },
      { "key": "branch", "label": "Branch", "required": true, "input": "branch" }
    ] },
  { "kind": "sql-query", "label": "Consulta (somente leitura)", "icon": "manage_search", "executor": "claude",
    "description": "SELECT nos hosts do cliente (alias do mapa) / Cognito; resultado nos arquivos do plano.",
    "params": [
      { "key": "database", "label": "Banco / ambiente", "input": "text" },
      { "key": "how", "label": "O que conferir", "required": true, "input": "textarea" }
    ] }
]
```

(`sql-script` existente continua sendo o script de **alteração**, executado por uma pessoa via chamado.)

## 5. Ferramentas propostas (formato `WorkItemTemplates`)

Notação de inputs: vêm do tipo da sub-tarefa (`repository|branch|target-branch|state|commits|text|textarea|select`);
valor `""` = a pessoa preenche ao criar.

### F1 — `pr-para-branch` · Abrir PR para uma branch (genérica) — *exemplo do pedido*
Freq.: base de todas as promoções e refações (≈ 20 PRs fora do par dev/qa). Bug e US, qualquer repo.

```json
{
  "key": "pr-para-branch",
  "title": "Abrir PR para uma branch",
  "description": "Branch {branch}{suffix:@target} criada a partir do destino escolhido, cherry-pick dos commits do card e PR para o destino. Destinos sugeridos: {promotionTargets}.",
  "when": { "type": ["bug", "user-story"] },
  "ask": [
    { "key": "repository", "label": "Repositório", "input": "repository", "default": "{repository}" },
    { "key": "target", "label": "Branch de destino", "input": "target-branch", "options": "{promotionTargets}" }
  ],
  "tasks": [
    { "title": "Criar {branch}{suffix:@target} a partir de {ask:target}", "kind": "branch",
      "params": { "repository": "{ask:repository}", "branch": "{branch}{suffix:@target}", "source": "{ask:target}" } },
    { "title": "Cherry-pick dos commits do card", "kind": "cherry-pick",
      "params": { "repository": "{ask:repository}", "branch": "{branch}{suffix:@target}", "from": "{branch}", "commits": "card" } },
    { "title": "Abrir PR {branch}{suffix:@target} → {ask:target}", "kind": "pr",
      "params": { "repository": "{ask:repository}", "source": "{branch}{suffix:@target}", "target": "{ask:target}", "draft": "false" } }
  ]
}
```

### F2 — `prs-fluxo-producao` · PRs do fluxo (development + qa)
Freq.: 13 cards (26 PRs) + refação no 75499. `when`: fluxo `producao`, repoKinds legacy/revamp-frontend (revamp-backend: só a parte dev — modelo F2b igual sem as 3 últimas sub-tarefas).

```json
{
  "key": "prs-fluxo-producao",
  "title": "PRs do fluxo (development e qa)",
  "description": "Branches {branch}{suffix:development} e {branch}{suffix:qa} criadas a partir de development e qa, cherry-pick dos commits da {branch} e PRs. Use também para refazer PRs com conflito.",
  "when": { "type": ["bug", "user-story"], "flow": ["producao"], "repoKinds": ["legacy", "revamp-frontend"] },
  "tasks": [
    { "title": "Criar {branch}{suffix:development} a partir de development", "kind": "branch",
      "params": { "repository": "{repository}", "branch": "{branch}{suffix:development}", "source": "development" } },
    { "title": "Cherry-pick para {branch}{suffix:development}", "kind": "cherry-pick",
      "params": { "repository": "{repository}", "branch": "{branch}{suffix:development}", "from": "{branch}", "commits": "card" } },
    { "title": "PR {branch}{suffix:development} → development", "kind": "pr",
      "params": { "repository": "{repository}", "source": "{branch}{suffix:development}", "target": "development" } },
    { "title": "Criar {branch}{suffix:qa} a partir de {qaBranch}", "kind": "branch",
      "params": { "repository": "{repository}", "branch": "{branch}{suffix:qa}", "source": "{qaBranch}" } },
    { "title": "Cherry-pick para {branch}{suffix:qa}", "kind": "cherry-pick",
      "params": { "repository": "{repository}", "branch": "{branch}{suffix:qa}", "from": "{branch}", "commits": "card" } },
    { "title": "PR {branch}{suffix:qa} → {qaBranch}", "kind": "pr",
      "params": { "repository": "{repository}", "source": "{branch}{suffix:qa}", "target": "{qaBranch}" } }
  ]
}
```

### F3 — `promover-revamp` · Promover revamp (release-version + edge + master)
Freq.: 5 cards (75349, 75648, 75669, 76051, 77434), 15 PRs. `when`: repoKinds revamp-frontend/revamp-backend. Segue `PromotionProfiles` (master em rascunho, PR direto da principal).

```json
{
  "key": "promover-revamp",
  "title": "Promover para release-version, edge e master",
  "description": "Perfil do revamp: {branch}{suffix:release-version} e {branch}{suffix:edge} nascem do destino com cherry-pick dos commits do card; master recebe PR direto da {branch} em rascunho.",
  "when": { "type": ["bug", "user-story"], "repoKinds": ["revamp-frontend", "revamp-backend"] },
  "tasks": [
    { "title": "Criar {branch}{suffix:release-version} a partir de release-version", "kind": "branch",
      "params": { "repository": "{repository}", "branch": "{branch}{suffix:release-version}", "source": "release-version" } },
    { "title": "Cherry-pick para {branch}{suffix:release-version}", "kind": "cherry-pick",
      "params": { "repository": "{repository}", "branch": "{branch}{suffix:release-version}", "from": "{branch}", "commits": "card" } },
    { "title": "PR {branch}{suffix:release-version} → release-version", "kind": "pr",
      "params": { "repository": "{repository}", "source": "{branch}{suffix:release-version}", "target": "release-version" } },
    { "title": "Criar {branch}{suffix:edge} a partir de edge", "kind": "branch",
      "params": { "repository": "{repository}", "branch": "{branch}{suffix:edge}", "source": "edge" } },
    { "title": "Cherry-pick para {branch}{suffix:edge}", "kind": "cherry-pick",
      "params": { "repository": "{repository}", "branch": "{branch}{suffix:edge}", "from": "{branch}", "commits": "card" } },
    { "title": "PR {branch}{suffix:edge} → edge", "kind": "pr",
      "params": { "repository": "{repository}", "source": "{branch}{suffix:edge}", "target": "edge" } },
    { "title": "PR {branch} → master (rascunho)", "kind": "pr",
      "params": { "repository": "{repository}", "source": "{branch}", "target": "master", "draft": "true" } }
  ]
}
```

### F4 — `pr-hotfix-version` (já semeado, ajustar `when`) · Promover legado para hotfix-version
Freq.: 5 cards legacy. Manter o modelo atual (branch `-hv` do destino + cherry-pick + PR) e acrescentar
`"when": { "repoKinds": ["legacy", "revamp-backend"] }` — **nunca** revamp-frontend (4 PRs fechados por isso).

### F5 — `pr-release-version` (já semeado, ajustar `when`)
Freq.: 5 cards revamp. `when`: repoKinds revamp-frontend/revamp-backend/integration-api (ou fluxo `release`). F3 cobre o
pacote; F5 fica para promover só um destino.

### F6 — `resolver-conflito-promocao` · PR de promoção com conflito
Freq.: 3 (77434 ×2 versões, 76607, 75499 no fluxo).

```json
{
  "key": "resolver-conflito-promocao",
  "title": "Resolver conflito do PR para {ask:target}",
  "description": "O PR para {ask:target} parou em conflito: branch {branch}{suffix:@target} recriada a partir do destino, cherry-pick dos commits do card, conflito resolvido mantendo os dois lados, testes do que mudou e PR (o antigo é fechado).",
  "when": { "type": ["bug", "user-story"] },
  "ask": [
    { "key": "repository", "label": "Repositório", "input": "repository", "default": "{repository}" },
    { "key": "target", "label": "Destino com conflito", "input": "target-branch", "options": "{promotionTargets}" }
  ],
  "tasks": [
    { "title": "Criar {branch}{suffix:@target} a partir de {ask:target}", "kind": "branch",
      "params": { "repository": "{ask:repository}", "branch": "{branch}{suffix:@target}", "source": "{ask:target}" } },
    { "title": "Cherry-pick e resolver o conflito", "kind": "cherry-pick",
      "params": { "repository": "{ask:repository}", "branch": "{branch}{suffix:@target}", "from": "{branch}", "commits": "card" } },
    { "title": "Validar os testes do que mudou na branch resolvida", "kind": "validation",
      "params": { "how": "testes dos arquivos com conflito e dos que eles usam" } },
    { "title": "PR {branch}{suffix:@target} → {ask:target}", "kind": "pr",
      "params": { "repository": "{ask:repository}", "source": "{branch}{suffix:@target}", "target": "{ask:target}" } },
    { "title": "Registrar na Timeline que a promoção foi destravada", "kind": "comment",
      "params": { "target": "timeline", "text": "" } }
  ]
}
```

### F7 — `ajuste-pos-qa` (já semeado, **corrigir**) · Ajuste após retorno de QA / revisão
Freq.: 77434, 76143 (texto novo sem aprovação de Produto), 75669/77434 v2. Trocar a última sub-tarefa (PR da principal
para `{flowTargets}`) por cherry-pick do ajuste em cada branch já aberta, o que atualiza os PRs existentes:

```json
{
  "key": "ajuste-pos-qa",
  "title": "Ajuste após retorno de QA",
  "description": "O QA (ou a revisão) devolveu o card: ajuste na {branch}, validação e o mesmo commit levado às branches dos PRs abertos ({flowTargets}) — os PRs se atualizam, sem PR novo.",
  "when": { "type": ["bug", "user-story"] },
  "tasks": [
    { "title": "Ajustar o código conforme o retorno", "kind": "code",
      "params": { "repository": "{repository}", "branch": "{branch}", "instructions": "" } },
    { "title": "Validar o ajuste", "kind": "validation", "params": { "how": "build e testes do que mudou" } },
    { "title": "Levar o ajuste para {branch}{suffix:development}", "kind": "cherry-pick",
      "params": { "repository": "{repository}", "branch": "{branch}{suffix:development}", "from": "{branch}", "commits": "" } },
    { "title": "Levar o ajuste para {branch}{suffix:qa}", "kind": "cherry-pick",
      "params": { "repository": "{repository}", "branch": "{branch}{suffix:qa}", "from": "{branch}", "commits": "" } },
    { "title": "Avisar no card que o ajuste subiu", "kind": "notify", "params": { "message": "" } }
  ]
}
```

### F8 — `usar-termo-existente` · Trocar mensagem nova por termo já aprovado
Freq.: 2 (77434, 76143) — Produto barra texto novo de validação/erro. Variante de F7 com instrução pronta.

```json
{
  "key": "usar-termo-existente",
  "title": "Trocar a mensagem nova por um termo existente",
  "description": "Texto novo precisa de aprovação de Produto: substituir pela mensagem/termo do Multilingual que já existe e levar o ajuste aos PRs abertos.",
  "when": { "type": ["bug", "user-story"] },
  "tasks": [
    { "title": "Achar o termo existente equivalente (Multilingual/legado)", "kind": "sql-query",
      "params": { "database": "multilingual", "how": "" } },
    { "title": "Trocar a mensagem pelo termo existente", "kind": "code",
      "params": { "repository": "{repository}", "branch": "{branch}", "instructions": "Usar o termo existente; não criar texto novo." } },
    { "title": "Validar", "kind": "validation", "params": { "how": "build e testes do que mudou" } },
    { "title": "Levar o ajuste aos PRs abertos ({flowTargets})", "kind": "cherry-pick",
      "params": { "repository": "{repository}", "branch": "{branch}{suffix:development}", "from": "{branch}", "commits": "" } }
  ]
}
```

### F9 — `fechamento-code-fix` · Fechamento de correção de código
Freq.: ~13 cards. Bug (US: sem root cause/classify — variante só com resumo + estados).

```json
{
  "key": "fechamento-code-fix",
  "title": "Fechamento: PRMake, root cause, code-fix e Dev Test in QA",
  "description": "Gera a descrição/RCA (sem abrir PR), grava o root cause, classifica code-fix, publica o resumo PT/EN e move para Dev Test in QA quando os PRs estão em QA. Ready for QA só depois da validação em QA.",
  "when": { "type": ["bug"] },
  "tasks": [
    { "title": "Gerar PRMake (descrição e RCA, sem abrir PR)", "kind": "pr-text",
      "params": { "repository": "{repository}", "branch": "{branch}" } },
    { "title": "Root cause no card", "kind": "devops-action", "params": { "action": "rootcause", "value": "" } },
    { "title": "Classificar como code-fix", "kind": "devops-action", "params": { "action": "classify", "value": "code-fix" } },
    { "title": "Resumo PT/EN na discussion", "kind": "devops-action", "params": { "action": "summary", "value": "" } },
    { "title": "Mover para Dev Test in QA", "kind": "devops-action", "params": { "action": "dev-test-in-qa", "value": "" } }
  ]
}
```

### F10 — `validar-em-qa` · Validar em QA e liberar Ready for QA
Freq.: 16 etapas `validar-qa`. Bug e US.

```json
{
  "key": "validar-em-qa",
  "title": "Validar em QA e mover para Ready for QA",
  "description": "Uma pessoa confere a correção no ambiente de QA; aprovada, o card vai para Ready for QA (In Development (done)).",
  "when": { "type": ["bug", "user-story"], "flow": ["producao"] },
  "tasks": [
    { "title": "Validar em QA", "kind": "manual", "params": { "instructions": "" } },
    { "title": "Mover para Ready for QA", "kind": "devops-action", "params": { "action": "ready-for-qa", "value": "" } }
  ]
}
```

### F11 — `fechamento-sem-codigo` · Orientação ao cliente / não é defeito
Freq.: ~9 cards (76590, 77290, 77510, 77696, 77852, 75777, 76507, 76701, 77205). Bug.

```json
{
  "key": "fechamento-sem-codigo",
  "title": "Fechamento sem código (orientação ao cliente)",
  "description": "Root cause, classificação escolhida, orientação ao cliente PT/EN na discussion, Remaining zerado e Test in Production.",
  "when": { "type": ["bug"] },
  "ask": [
    { "key": "classification", "label": "Classificação", "input": "select",
      "options": ["user-education", "user-education-change-request", "configuration", "script-user-action", "script-environment", "script-defect", "cannot-reproduce", "no-user-feedback", "duplicated", "change-request", "not-mapped-requirement"] }
  ],
  "tasks": [
    { "title": "Root cause no card", "kind": "devops-action", "params": { "action": "rootcause", "value": "" } },
    { "title": "Classificar ({ask:classification})", "kind": "devops-action", "params": { "action": "classify", "value": "{ask:classification}" } },
    { "title": "Orientação ao cliente PT/EN na discussion", "kind": "devops-action", "params": { "action": "summary", "value": "" } },
    { "title": "Zerar o Remaining Work", "kind": "devops-action", "params": { "action": "zero-remaining", "value": "" } },
    { "title": "Mover para Test in Production", "kind": "devops-action", "params": { "action": "test-in-production", "value": "" } }
  ]
}
```

### F12 — `script-com-chamado` · Script de dados com chamado
Freq.: 5 chamados Freshservice + rollback em 13 análises.

```json
{
  "key": "script-com-chamado",
  "title": "Script de correção de dados com chamado",
  "description": "Consulta antes (somente leitura), script com rollback nos arquivos do plano, chamado no {ticketSystem} e conferência depois.",
  "when": { "type": ["bug"] },
  "tasks": [
    { "title": "Consultar o estado atual (somente leitura)", "kind": "sql-query", "params": { "database": "", "how": "" } },
    { "title": "Preparar o script com rollback", "kind": "sql-script", "params": { "database": "", "file": "" } },
    { "title": "Abrir o chamado com o script", "kind": "ticket",
      "params": { "system": "", "title": "AB#{card} ", "description": "", "priority": "Média" } },
    { "title": "Conferir os dados depois do script", "kind": "sql-query", "params": { "database": "", "how": "" } }
  ]
}
```

### F13 — `diagnostico-usuario` · Conferir usuário no banco e no Cognito
Freq.: ~6 (75349, 75777, 76507, 76701, 77205, 75648). Bug.

```json
{
  "key": "diagnostico-usuario",
  "title": "Conferir o usuário (banco + Cognito)",
  "description": "Cadastro, vínculo SSO, sites/permissões e conta no pool do Cognito do cliente, só leitura, antes de perguntar ao cliente.",
  "when": { "type": ["bug"] },
  "tasks": [
    { "title": "Cadastro, SSO e permissões no banco do cliente", "kind": "sql-query", "params": { "database": "", "how": "" } },
    { "title": "Conta no Cognito (pool do ambiente)", "kind": "sql-query", "params": { "database": "cognito", "how": "" } },
    { "title": "Registrar o diagnóstico na Timeline", "kind": "comment", "params": { "target": "timeline", "text": "" } }
  ]
}
```

### F14 — `novo-resumo` · Novo comentário/resumo PT/EN (sem apagar o anterior)
Freq.: 75648 (call com cliente), 76143 (novo root cause). Bug e US.

```json
{
  "key": "novo-resumo",
  "title": "Novo resumo PT/EN no card",
  "description": "Publica um novo resumo não técnico (PT/EN) com o contexto informado, mantendo os anteriores; opcionalmente atualiza o root cause.",
  "when": { "type": ["bug", "user-story"] },
  "tasks": [
    { "title": "Resumo PT/EN com o contexto novo", "kind": "devops-action", "params": { "action": "summary", "value": "" } },
    { "title": "Atualizar o root cause (se mudou)", "kind": "devops-action", "params": { "action": "rootcause", "value": "" } }
  ]
}
```

### F15 — `corrigir-sonar` · Destravar o PR barrado pelo Sonar
Freq.: 1 (76051), mas bloqueia merge em qualquer revamp-backend.

```json
{
  "key": "corrigir-sonar",
  "title": "Corrigir crítica do Sonar no PR",
  "description": "Ajustar a branch do PR para passar no quality gate (duplicação, cobertura, code smells) e atualizar os PRs abertos.",
  "when": { "type": ["bug", "user-story"], "repoKinds": ["revamp-backend", "revamp-frontend", "integration-api"] },
  "tasks": [
    { "title": "Corrigir o que o Sonar apontou", "kind": "code",
      "params": { "repository": "{repository}", "branch": "{branch}", "instructions": "" } },
    { "title": "Validar (build e testes)", "kind": "validation", "params": { "how": "build e testes do que mudou" } },
    { "title": "Levar a correção às branches dos PRs abertos", "kind": "cherry-pick",
      "params": { "repository": "{repository}", "branch": "{branch}{suffix:development}", "from": "{branch}", "commits": "" } }
  ]
}
```

### Outras (baixa frequência — catálogo, não semear agora)
- `replicar-correcao` (76143, 75669): `branch` + `code` + `validation` + `pr` em outro repositório (`ask: repository`).
- `fechar-como-duplicado` (75703): `comment` referenciando o card + `devops-action classify duplicated` + summary + estado.
- `revisar-pr-terceiro` (77697): `validation` com a URL do PR e conclusão na Timeline.
- `estimativa-inicial` (sem evidência de uso; config com 6 h): `devops-action initial-estimate`.
- Cancelar/retomar plano (77534, 77900): continua como **proposta** do especialista, não item de trabalho.

## 6. Lista priorizada para semear

| # | key | Motivo | Situação |
|---|---|---|---|
| 1 | `pr-para-branch` | pedido explícito; cobre qualquer destino com sufixo certo | novo (precisa `ask`/`{suffix:@target}`/`{promotionTargets}`) |
| 2 | `prs-fluxo-producao` (+ variante revamp-backend só dev) | 13 cards / 26 PRs; refação de PR com conflito | novo |
| 3 | `promover-revamp` | 5 cards / 15 PRs, padrão do `PromotionProfiles` | novo |
| 4 | `pr-hotfix-version` | 5 cards legacy | existe — só `when` (bloquear revamp-frontend) |
| 5 | `pr-release-version` | 5 cards revamp | existe — só `when` |
| 6 | `fechamento-code-fix` | ~13 cards | novo (precisa `devops-action`, `pr-text`) |
| 7 | `fechamento-sem-codigo` | ~9 cards | novo (precisa `devops-action`, `ask` de classificação) |
| 8 | `validar-em-qa` | 16 etapas | novo |
| 9 | `ajuste-pos-qa` | retorno de QA/revisão | existe — **corrigir** (cherry-pick nas branches abertas em vez de PR da principal) |
| 10 | `resolver-conflito-promocao` | 3 cards, 2 versões de plano só para isso | novo |
| 11 | `script-com-chamado` | 5 chamados, rollback em 13 análises | novo (usa `ticket` da 0085b + `sql-query`) |
| 12 | `diagnostico-usuario` | ~6 cards | novo (`sql-query`) |
| 13 | `usar-termo-existente` | 2 cards, regra de Produto recorrente | novo |
| 14 | `novo-resumo` | 2 cards | novo |
| 15 | `corrigir-sonar` | 1 card, bloqueia merge | novo |
| — | `mover-card`, `abrir-chamado` | genéricos | existem — manter |

Antes de semear: corrigir `PromotionBranchPattern` de produção (`_{branch}{suffix}`), padronizar `-rv` × `-RV`, e fazer
`{repository}` vir do repositório do card (não do `DefaultRepository`).
