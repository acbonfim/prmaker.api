---
name: gerar-handover
description: Gera e salva a Passagem de Conhecimento (handover) de um card no PRMake, preenchendo o formulário cadastrado nas configurações (plugin AI Configurations, TemplatePassagemConhecimento) com os dados do card no Azure DevOps (campos, comentários, histórico), o PR salvo e a linha do tempo. Descobre o card pela branch atual (hotfix/<card> ou bugfix/<card>) ou por um número informado. Use quando o usuário pedir para "gerar handover", "passagem de conhecimento", "passar o card para o próximo turno" ou similar.
---

# gerar-handover

Faz, pelo Claude Code, o mesmo que o botão **Handover** da tela do PRMake (`handover-dialog.component.ts`):
lê o layout do formulário configurado, cruza os dados do card e salva o formulário preenchido
(markdown) em `POST /Handover`. **Você (Claude) é o gerador**: não chame `AI/generate`.

> Skill no nível do usuário (`~/.claude/skills/gerar-handover`). Usa o mesmo token da `gerar-prmake`.

## Configuração
- **API base:** `https://api.softhouse.app.br/api/v1` (env `PRMAKE_BASE`). Não use `prformapi.runasp.net`.
- **Token:** header `x-api-key`, resolvido por env `PRMAKE_TOKEN`, senão `~/.claude/prmake-token.txt`.
- **Layout:** plugin **AI Configurations** (id `3`, env `PLUGIN_ID`), chave `TemplatePassagemConhecimento`,
  editado em Configurações → Plugins. Sempre buscado na hora (é a fonte da verdade do formulário).
- **Saída:** `/tmp/handover` (env `OUTDIR`).

## Endpoints
| Etapa | Endpoint |
|---|---|
| Layout do formulário | `GET /PluginConfiguration/get-all-by-id?id=3` → `configurations.TemplatePassagemConhecimento` |
| Card (campos, comentários, histórico, alertas) | `GET /Azure/card/{card}/full` |
| PR salvo | `GET /PullRequest/GetByCardNumber?cardNumber={card}` |
| Linha do tempo | `GET /Timeline/card/{card}` |
| Handover atual | `GET /Handover/GetByCardNumber?cardNumber={card}` |
| **Salvar** (upsert por card) | `POST /Handover` `{cardNumber, content, repositoryId?, isPublic}` |
| Visibilidade (opcional) | `PUT /Handover/{card}/visibility` `{isPublic}` |

## Fluxo

### 1. Identificar o card
`git rev-parse --abbrev-ref HEAD` → `hotfix/<numero>` ou `bugfix/<numero>` (ignore sufixos como `-qa`).
Se o usuário informou um número, use-o. Se não der para descobrir, **pergunte**.

### 2. Buscar os dados (somente leitura)
```bash
bash ~/.claude/skills/gerar-handover/scripts/handover-fetch.sh <card>
```
Grava em `/tmp/handover/`:
- `template.md`: o layout do formulário. **Pode trazer regras próprias** (ex.: "priorize a timeline",
  "timeline de análise cronológica"), que valem tanto quanto as regras abaixo;
- `context.json`: os dados cruzados no mesmo formato que a tela envia (`card`, `pullRequestSalvo`, `linhaDoTempo`);
- `prompt.txt`: o prompt completo da tela (regras + layout + dados);
- `card.json`, `pr.json`, `timeline.json`, `handover_atual.json`: os dados brutos.

O manifesto mostra título, tipo, estado, quantidade de comentários, histórico e timeline, se há PR salvo e
se **já existe handover** (que será sobrescrito). O script para com erro se o layout estiver vazio, se o card
não existir no DevOps, com token expirado (401) ou com `PERSONAL_INTEGRATION_REQUIRED` (o usuário precisa salvar
a chave do DevOps em **Minhas integrações**).

> `context.json` pode ser grande (o histórico do card chega a centenas de KB). Leia `template.md` inteiro e
> explore o contexto por partes com `jq` (ex.: `jq '.linhaDoTempo'`, `jq '.card.comentarios'`,
> `jq '.card.historicoDeAlteracoes[] | {rev:.rev, quem:.changedByName, quando:.changedDate, campos:[.changes[].field]}'`,
> `jq '.pullRequestSalvo'`). Não deixe nenhuma fonte de fora.

### 3. Preencher o formulário
Siga `prompt.txt`, que tem as mesmas regras da tela:
- Preencha **exatamente** o layout de `template.md`, mantendo **todos os títulos e a estrutura** e trocando os
  espaços em branco (`____`, `#____`) pelas informações reais.
- Cruze **todos** os dados: campos do card, comentários, histórico de alterações, PR salvo e linha do tempo.
- Onde não houver informação, escreva `—`.
- **Português**, **Markdown**, **apenas o formulário**: nada antes nem depois e sem cercas ```.
- Seja objetivo e técnico. Priorize o que o próximo turno precisa: o que já foi investigado, onde está o
  problema, o que foi descartado, a causa provável e o próximo passo.
- Respeite as regras que vierem dentro do próprio layout.
- **Nunca invente.** Use só o que está nos arquivos. Datas no formato `dd/mm/aaaa`. "De:" = quem vem
  tratando o card (timeline, histórico ou autor do PR). "Para:" = `—`, a menos que o usuário informe.
- Se estiver em um repositório com o código do card, pode complementar "Onde está o problema" com
  arquivos/métodos confirmados no código (sem especular).

Se `handover_atual.json` tiver `content`, use-o só como referência de formato. O novo texto deve refletir os dados atuais.

Escreva o resultado em `/tmp/handover/handover.md`.

### 4. Confirmar com o usuário
Mostre o `handover.md` e **peça confirmação** antes de salvar. Avise se vai **sobrescrever** um handover
existente e que um handover **novo nasce público** (link sem autenticação). Pergunte se deve ficar privado.
Nunca salve sem o "ok".

### 5. Salvar (após confirmação)
```bash
bash ~/.claude/skills/gerar-handover/scripts/handover-publish.sh <card> /tmp/handover/handover.md [repositoryId]
# criar já privado:           IS_PUBLIC=false bash .../handover-publish.sh ...
# forçar visibilidade depois: VISIBILITY=false bash .../handover-publish.sh ...   (ou true)
```
- `POST /Handover` é um upsert por `cardNumber`. Na regeneração troca só `content` (e `repositoryId`, se
  informado) e **preserva a visibilidade atual**. `IS_PUBLIC` só vale na criação; para mudar um
  existente use `VISIBILITY` (faz `PUT /Handover/{card}/visibility`).
- `repositoryId` padrão = o do PR salvo (`pr.json`).
- O autor (`CreatedBy`) sai do token; não é preciso enviar `userId`.

### 6. Reportar
Informe o card, se o handover foi criado ou atualizado, o `id`, o `isPublic`, `updatedAt` e o acesso público
(`GET /Handover/public/{card}` retorna 200 se público e 403 se privado; na tela: `<front>/handover/{card}`).
Erros: **401** = token expirado; **403 PERSONAL_INTEGRATION_REQUIRED** = falta a chave do DevOps em
"Minhas integrações"; **400** no card = card inexistente/erro no DevOps.
