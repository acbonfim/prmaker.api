# 0105 — Plano

## Decisões
- **Cálculo no servidor** (`CardSlaCalculator`, Solvace.Azure): o mesmo resultado no board, no card aberto, na Bancada e no MCP.
- **Quem abriu** = campo `Custom.Foundby` (Found by, obrigatório no Bug): `Internal Team` = time interno; qualquer outro valor
  (nome do cliente) ou vazio = cliente.
- **Severidade** = `Microsoft.VSTS.Common.Severity` ("1 - Critical"…); a regra casa com ou sem o número.
- **Início** = `Custom.CreationDate` (data do ticket no HubSpot), senão `System.CreatedDate`.
- **Dias úteis** = sem sábado/domingo e sem os feriados configurados, no fuso `America/Sao_Paulo`. Prazo = abertura + N d.u.;
  falta = d.u. de hoje (exclusive) até o prazo; 0 = vence hoje; negativo = vencido.
- **Faixas**: grave ≤ 3 d.u. (ou vencido), atenção ≤ 15 d.u., ok > 15. O pedido diz "3 grave, -10 atenção, acima de 15 ok" —
  10 a 15 ficaria sem faixa, então atenção vai até 15 (configurável).
- **Prazo para** nos estados `Test in Production` e `Closed` (configurável): mostra Cumprido / Estourado, pela data de entrada
  no estado (`StateChangeDate`).
- **Só Bug** por padrão (tipos configuráveis). Card sem severidade ou sem regra = sem selo.
- **Configurações**: seção nova "SLA dos cards" (catálogo 0086, plugin "AzureDevOps Configurations"), tabela editável
  quem abriu × severidade × prazo + limites das faixas + campos/estados/feriados. Sem migração: chave ausente = padrão do
  código (o mesmo do catálogo); o PATCH das Configurações cria a chave na primeira edição.

## Fases
| Fase | O quê |
|---|---|
| B1 | `CardSlaKeys`, `CardSlaSettings`, `CardSlaCalculator`, `CardSlaInfo` + testes (`CardSla0105Tests`) |
| B2 | `sla` em `DevOpsBoardItem` (board pede os campos do SLA; 400 do DevOps → board sem SLA), `DevOpsCardDetail` e `AzureCardFullResponse` (Bancada) |
| B3 | Catálogo: seção `sla` (grupos regras/contagem) |
| F1 | `app-sla-badge` (cores sólidas grave/atenção/ok, contorno quando parado, tooltip com o detalhe) |
| F2 | Selo no cartão do board da Mesa, no cabeçalho do card aberto (modal único) e no cabeçalho da Bancada |
| Q1 | e2e local (`.t0105`, DevOps falso com um bug por faixa) + edição pela tela de Configurações |
