## Endpoints HTTP
- **CustomTranslationController** (`[controller]`, Solvace.Multilingual/src/Solvace.Multilingual.API/Controllers/CustomTranslationController.cs): `GET GetToCreate` · `PUT UpdateIsReviewed` · `PUT UpdateCustomTranslationContentById`
- **HistoryController** (`[controller]`, Solvace.Multilingual/src/Solvace.Multilingual.API/Controllers/HistoryController.cs): `GET Download`
- **ReportController** (`[controller]`, Solvace.Multilingual/src/Solvace.Multilingual.API/Controllers/ReportController.cs): `GET ByAge` · `GET ByLanguage`
- **SpeechToTextController** (`[controller]`, Solvace.Multilingual/src/Solvace.Multilingual.API/Controllers/SpeechToTextController.cs): `POST /`
- **TermKeyController** (`[controller]`, Solvace.Multilingual/src/Solvace.Multilingual.API/Controllers/TermKeyController.cs): `GET List` · `GET Download/Excel` · `POST Upload/Excel`
- **TranslateController** (`[controller]`, Solvace.Multilingual/src/Solvace.Multilingual.API/Controllers/TranslateController.cs): `GET /`
- **TranslationController** (`[controller]`, Solvace.Multilingual/src/Solvace.Multilingual.API/Controllers/TranslationController.cs): `PUT UpdateIsReviewed` · `PUT UpdateTranslationContentById`

## Casos de uso
- **Queries**: CustomTermKeyById, CustomTermKeyGetToCreate, GetExcelTermKeyTranslation, GetLanguageByTerm, GetSpeechToText, GetTranslation, HistoryList, LanguageList, TermKeyById, TermKeyByTermKeyPortugueseBR, TermKeyList, TermKeyTranslation, TranslationByAge, TranslationByLanguage
- **UseCases**: CreateCustomTranslation, CreateTermKey, CreateTranslation, DeleteCustomTranslation, DeleteTermKey, HistoryDownload, ImportTermKeyTranslation, UpdateCustomTranslation, UpdateCustomTranslationContentById, UpdateReviewedCustomTranslationById, UpdateReviewedTranslationById, UpdateTermKey, UpdateTermsCache, UpdateTranslationContentById
