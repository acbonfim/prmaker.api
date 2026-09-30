## O que faz
É por aqui que o chamado do cliente (ticket na **HubSpot**) vira card (**Bug** no Azure DevOps) — e o andamento do card volta para o
ticket. Tudo por rotinas **Hangfire** (`IT.HamgFire`, `IT.Services/Services/HangfireJobService.cs`):

| Rotina | Frequência | O que faz |
|---|---|---|
| `CompareTicketsAndUpdateAzureJob` | 1 min | Tickets novos/alterados na HubSpot → cria/atualiza o Bug no DevOps |
| `CompareWorkItemsAndUpdateHubSpotJob` | 1 min | Estado do Bug no DevOps → estágio do ticket na HubSpot |
| `CheckHubspotHealth`, `CheckAzureHealth`, `FileDownloadHealth` | 1 h | Saúde das integrações (alerta por e-mail) |

Projetos: `IT.Api` (controllers), `IT.Services` (`IntegrationService`, `ApiService` = HubSpot, `AzureService` = DevOps,
`EmailService`, `TxtLogService`), `IT.Data`/`IT.Infra` (tabela de vínculo ticket ↔ work item `TaskIntegration`: `TICKET_ID`,
`WORKITEM_ID`, `WORKITEM_STATUS`), `IT.Domain`, `IT.DI`. .NET 6.

## Campos do card preenchidos a partir do ticket
`System.Title`, `System.Description`, `System.AreaPath`, `System.Tags`, `System.WorkItemType` = Bug e os customizados
**`Custom.Environment`** (ambiente/cliente), **`Custom.Module`**, **`Custom.Site`**, **`Custom.Foundby`**, **`Custom.Originador`**,
`Custom.PLASolvaceInternal`. Do ticket: `subject`, `content`, `hs_ticket_category`, `hs_ticket_priority`, `hs_pipeline_stage`.
Na análise de um bug, **Environment/Module/Site** dizem em qual ambiente, módulo e planta procurar.

## Estados: DevOps → estágio do ticket
| Estado do card | Estágio na HubSpot |
|---|---|
| Release Candidate, Ready to Deliver | `3` |
| Test UAT | `27819271` |
| Test in Production | `27829146` |
| Closed | mantém o estágio atual do ticket |
| demais | `2` |

## Ticket → card (volta do cliente)
- Estágio `2` com retorno do cliente → card vai para **Planning (done)**, **área `Solvace Product Improvement\Product Development
  Team`** e tag **`RETORNO`**.
- Estágio `4` → card **Closed**. Estágios `27829146`/`1210083717` → **Test in Production**.

## Endpoints (`IT.Api`)
`GET QueryToListWorkItems`, `POST UpdateHubspotTicketsFromWorkItemStatus`, `GET GetAllTicketsFromHubspot`,
`GET AzureApiHealth`/`HubspotApiHealth`/`FileDownloadHealth`, `POST SendEmail`, `POST DownloadTxtLog`, `POST HangFire`.
Código antigo em `Services/OldServices` (`HubspotToAzure`, `AzureToHubspot`) — não é o fluxo atual.

## Armadilhas
- Card "sumiu" da fila ou voltou com tag `RETORNO`: foi o cliente respondendo no ticket (regra acima), não alguém da equipe.
- Mudança de estado no DevOps demora até ~1 min para refletir na HubSpot.
- Estágios são ids numéricos da HubSpot (pipeline de suporte) — conferir no HubSpot se um novo estágio foi criado.
