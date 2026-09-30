## Endpoints HTTP
- **CounterController** (`[controller]`, Solvace.Kaizen/src/Solvace.Kaizen.API/Controllers/CounterController.cs): `GET kaizen/approvals`
- **IdeaController** (`[controller]`, Solvace.Kaizen/src/Solvace.Kaizen.API/Controllers/IdeaController.cs): `GET /` · `GET summary` · `GET download/pdf` · `GET download/excel`
- **KaizenController** (`[controller]`, Solvace.Kaizen/src/Solvace.Kaizen.API/Controllers/KaizenController.cs): `GET /simpleSearch`
- **LegacyController** (`[controller]`, Solvace.Kaizen/src/Solvace.Kaizen.API/Controllers/LegacyController.cs): `POST {id}/events/upsert` · `POST {id}/events/delete`
- **ScoreCardsController** (`[controller]`, Solvace.Kaizen/src/Solvace.Kaizen.API/Controllers/ScoreCardsController.cs): `GET kaizen/suggested` · `GET kaizen/executed` · `GET kaizen/delivered` · `GET kaizen/executed/items` · `GET kaizen/suggested/items` · `GET kaizen/delivered/items`
- **TypeController** (`[controller]`, Solvace.Kaizen/src/Solvace.Kaizen.API/Controllers/TypeController.cs): `GET names`

## Casos de uso
- **Commands**: DeleteKaizenComments, Events
- **Queries**: Builder, GetExcelKaizenIdea, GetKaizenApprovalsCounter, GetKaizenIdeasFiltered, GetKaizenIdeasSummary, GetKpiKaizenDelivered, GetKpiKaizenDeliveredItem, GetKpiKaizenExecuted, GetKpiKaizenExecutedItem, GetKpiKaizenSuggested, GetKpiKaizenSuggestedItem, GetPdfKaizenIdea, GetTypeNames, SimpleSearch
