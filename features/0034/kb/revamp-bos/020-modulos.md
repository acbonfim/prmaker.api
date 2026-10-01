## Endpoints HTTP
- **BehaviorController** (`[controller]`, Solvace.BOS/src/Solvace.BOS.API/Controllers/BehaviorController.cs): `POST /` · `PUT /` · `DELETE /` · `GET /` · `GET BySiteId` · `GET List` · `GET download/excel` · `GET download/pdf`
- **BehaviorPhotoController** (`[controller]`, Solvace.BOS/src/Solvace.BOS.API/Controllers/BehaviorPhotoController.cs): `POST /` · `DELETE /`
- **BosReportController** (`[controller]`, Solvace.BOS/src/Solvace.BOS.API/Controllers/BosReportController.cs): `GET FeedbackGroupByDay` · `GET FeedbackGroupByMonth` · `GET FeedbackGroupByYear` · `GET FeedbackGroupByPerson` · `GET FeedbackGroupByTeam` · `GET FeedbackGroupByArea` · `GET FeedbackGroupByWordCloud` · `GET FeedbackByComment` · `GET BehaviorGroupByFrequency` · `GET BehaviorGroupByWeek` · `GET BehaviorAll` · `GET BehaviorGroupByMonthCorrectIncorrect`
- **CategoryController** (`[controller]`, Solvace.BOS/src/Solvace.BOS.API/Controllers/CategoryController.cs): `POST /` · `PUT /` · `DELETE /` · `GET /` · `GET BySiteId` · `GET List` · `GET download/excel` · `GET download/pdf`
- **CriticalBehaviorController** (`[controller]`, Solvace.BOS/src/Solvace.BOS.API/Controllers/CriticalBehaviorController.cs): `GET List` · `PUT /`
- **FeedbackController** (`[controller]`, Solvace.BOS/src/Solvace.BOS.API/Controllers/FeedbackController.cs): `POST /` · `PUT /` · `DELETE /` · `GET /` · `GET List` · `GET ListDrillDown` · `GET download/excel` · `GET download/pdf`
- **FlexmonsterController** (`[controller]`, Solvace.BOS/src/Solvace.BOS.API/Controllers/FlexmonsterController.cs): `POST Handshake` · `POST Fields` · `POST Select` · `POST Members`
- **JustificationController** (`[controller]`, Solvace.BOS/src/Solvace.BOS.API/Controllers/JustificationController.cs): `GET /` · `GET List/Weeks` · `POST /` · `DELETE /`
- **JustificationTypeController** (`[controller]`, Solvace.BOS/src/Solvace.BOS.API/Controllers/JustificationTypeController.cs): `GET /`
- **PivotTableController** (`[controller]`, Solvace.BOS/src/Solvace.BOS.Integration.API/Controllers/PivotTableController.cs): `GET List` · `GET OutputFilePath`
- **ScoreCardsController** (`[controller]`, Solvace.BOS/src/Solvace.BOS.API/Controllers/ScoreCardsController.cs): `GET bos/reported` · `GET bos/participation` · `GET bos/reported/items` · `GET bos/participation/items`
- **ShiftController** (`[controller]`, Solvace.BOS/src/Solvace.BOS.API/Controllers/ShiftController.cs): `GET /`
- **TypeController** (`[controller]`, Solvace.BOS/src/Solvace.BOS.API/Controllers/TypeController.cs): `POST /` · `PUT /` · `DELETE /` · `GET /` · `GET BySiteId` · `GET List` · `GET ListTypeBehaviors` · `GET download/excel` · `GET download/pdf`

## Casos de uso
- **Commands**: Athena
- **Queries**: Analytics, Attachment, Behaviors, Builder, Categories, CriticalBehaviors, Feedbacks, GetUserIdsByFilters, Helpers, Justifications, Kpis, ListFeedbacksBehaviors, ListJustificationTypes, ListPivotTable, ListPivotTableFile, ListShifts, ListTypeBehaviors, ListUsers, ListWeeksOfMonth, Parameters, Types
- **UseCases**: Attachment, Behaviors, Calendars, Categories, CriticalBehaviors, Feedbacks, FeedbacksHistoricsWeeks, Flexmonster, Justifications, Parameters, SendNotification, Types
