## Endpoints HTTP
- **CustomFieldController** (`[controller]`, Solvace.CustomField/src/Solvace.CustomField.API/Controllers/CustomFieldController.cs): `POST /` · `GET /`
- **CustomFieldLegacyController** (`[controller]`, Solvace.CustomField/src/Solvace.CustomField.API/Controllers/CustomFieldLegacyController.cs): `GET /`
- **CustomFieldListController** (`[controller]`, Solvace.CustomField/src/Solvace.CustomField.API/Controllers/CustomFieldListController.cs): `GET searchable`
- **CustomFieldTypeController** (`[controller]`, Solvace.CustomField/src/Solvace.CustomField.API/Controllers/CustomFieldTypeController.cs): `GET /`
- **CustomFieldValueController** (`[controller]`, Solvace.CustomField/src/Solvace.CustomField.API/Controllers/CustomFieldValueController.cs): `POST /` · `GET /`
- **TemplateController** (`[controller]`, Solvace.CustomField/src/Solvace.CustomField.API/Controllers/TemplateController.cs): `POST /` · `PUT /` · `DELETE /` · `GET /` · `GET BySiteId` · `GET BySiteAndApplicationId` · `GET List`

## Casos de uso
- **Queries**: GetCustomField, GetCustomFieldList, GetCustomFieldType, GetCustomFieldValue, GetLegacyCustomFieldStatus, Template
- **UseCases**: CreateCustomField, CreateCustomFieldValue, Template
