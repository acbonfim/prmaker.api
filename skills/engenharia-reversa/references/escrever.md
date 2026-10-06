# Como escrever um documento da engenharia reversa (assertivo, completo, barato de ler)

O leitor e uma analise de bug (LLM) que vai buscar **um item** pelo indice e ler so ele, ou uma pessoa de QA/produto.
Cada item precisa responder sozinho: o que e, quando vale, o valor exato, onde esta no codigo, quem mais e afetado.

## Item bom × item ruim
Ruim:
```
### RN-004 — Validacoes do cadastro
O sistema valida alguns campos obrigatorios e o prazo.
```
Bom:
```
### RN-004 — Ideia so avanca da etapa "Analise" com o aprovador da etapa preenchido
- **Onde:** `KaizenWorkflowService.cs:212-238` (back) · `kaizen-steps.component.ts:141` (front, so desabilita o botao)
- **Quando:** clique em "Avancar etapa" (TELA-007) → `POST Kaizen/{id}/advance` (API-012)
- **Regra:** se `TB_MLH_LEVEL_WORKFLOW.ApproverId` da etapa atual for nulo → 400 "Informe o aprovador da etapa." e a
  ideia fica na etapa; com aprovador, status `2 (EmAnalise)` → `3 (EmAprovacao)` e NTF-002 vai ao aprovador.
- **Excecoes:** perfil Administrador do Kaizen (PRF-001) avanca sem aprovador quando o parametro CFG-003
  `MultiplasAprovacoes = 0`.
- **Telas:** TELA-007 · **Tabelas:** TB_MLH_MELHORIAS, TB_MLH_LEVEL_WORKFLOW · **Modulos:** revamp-users
- **Tags:** avancar etapa, aprovador, workflow, nao consigo passar a etapa, step
- **KC:** ART-34
```

## Ordem de leitura do codigo (back)
1. Pontos de entrada: controllers/endpoints (inventario `endpoint`), telas `.asp` (legado), consumers/jobs.
2. Para cada um: service/handler → validacoes (inventario `validacao`) → repositorio/SP → tabelas.
3. Enums e status (inventario `enum`) → `EST-…` com a tabela de transicoes.
4. Configuracoes por planta/parametros → `CFG-…` e o efeito em cada RN.
5. Integracoes: clientes HTTP, publicacao/consumo de eventos, tabelas de outro dono (`TB_<OUTRA SIGLA>_*`), pacotes.

## Front (revamp: `edv-solvace-apps/projects/<x>`)
Rotas (inventario `rota-front`) → componente → template (campos, botoes, mensagens, estados) → servico (`http-front`)
→ endpoint do back. Validacoes de formulario (`Validators.*`) sao regras: diga se o back tambem valida.

## Legado (`edv-solvace`)
`.asp` (ASP classico): `Request.Form/QueryString` = entrada; SQL inline/SP = regra e tabela; `alert(...)`/mensagens
traduzidas (`GetLanguageByName`) = validacoes literais; `_inc_*.asp` = trechos compartilhados. `solvace-core/<modulo>`
(.NET Core MVC): controllers → services → SP. Cite a pagina e a linha.

## Integracao (`INT`) — e o que desenha o mapa de ligacoes (0066)
```
### INT-004 — Checklist → Plano de Acao: lista os planos ligados ao checklist
- **Modulos:** legado-actionplan
- **Mecanismo:** pacote/biblioteca `helpers` (ActionPlanHelper) lendo o banco local
- **Contrato:** sincrono, na abertura da aba Planos; `ListPlansByApplicationId(systemId, checklistId)` → VW_ACP_PLAN
- **Onde:** `Controllers/ChecklistController.cs:650` → `helpers/Helpers/ActionPlanHelper.cs:200`
- **Se falhar:** 500 e a aba Planos fica vazia
- **Confirmar:** filtro de status no revamp
```
`**Modulos:**` so com chaves (`modulos.tsv`) ou `ext:<servico>`; o resto (duvida, nome que nao achou) vai em `**Confirmar:**`.

## Subagentes (modulo grande — 0066: pacotes e cartao)
Use `re.sh areas` + `re.sh pacote` (SKILL.md, passo 4): o subagente le o cartao e o pacote da area numa resposta, nao
explora o codigo e grava a parte com checkpoint. O texto abaixo vale para quando nao houver pacote.
Prompt de cada subagente: modulo, documento, a area (pastas/arquivos do inventario dessa area), a faixa de IDs, o
`modelo.md` (secoes e formato do item), "leia o codigo de verdade, valores literais, evidencia arquivo:linha, nada
inventado", e "grave em `<pasta>/parte-<area>.md` e responda so: itens por tipo, lacunas, duvidas". Depois `juntar`,
`check`, complete o que faltou.

## Tamanho
Sem limite artificial: um modulo grande pode ter centenas de RN. Mas cada item e enxuto (5–12 linhas), sem repetir o
que esta em outro item (referencie o ID). Diagramas mermaid so onde ajudam.
