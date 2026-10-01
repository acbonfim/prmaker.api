## Endpoints HTTP
- **PraiseController** (`[controller]`, Solvace.Praise/src/Solvace.Praise.API/Controllers/PraiseController.cs): `POST /` · `PUT {id}` · `GET /` · `GET {praiseId}` · `GET {startDate}/{endDate}/monthly` · `GET {startDate}/{endDate}` · `DELETE {id}` · `GET {praiseId}/user/eligibility`
- **TitleController** (`[controller]`, Solvace.Praise/src/Solvace.Praise.API/Controllers/TitleController.cs): `GET /` · `GET descriptions` · `POST /` · `PUT {id}` · `DELETE {titleId}` · `GET download/excel` · `GET download/pdf`
- **UserController** (`[controller]`, Solvace.Praise/src/Solvace.Praise.API/Controllers/UserController.cs): `GET {userId}/praises` · `GET praised` · `GET received/counter`

## Casos de uso
- **Queries**: Builder, GetAllTitles, GetExcelTitle, GetPdfTitle, GetPraiseById, GetPraiseByPeriod, GetPraiseByUser, GetPraiseMonthly, GetPraiseUserEligibility, GetPraisedUsers, GetPraisesFiltered, GetReceivedPraiseCounter, GetTitleDescription
- **UseCases**: Praises, Titles
