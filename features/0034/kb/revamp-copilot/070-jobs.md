## Lambdas
| Função | Gatilho | O que faz |
|---|---|---|
| `lambda_feedback_analysis_event` | fila FEEDBACK_ANALYSIS | Triggers the autonomous analysis (Claude Code) of negative Copilot feedback. |
| `lambda_meeting_notes_event` | fila MEETING_NOTES | This Lambda function is intended to request the creation of a summary or notes for a Digital Obeya meeting. |
| `lambda_template_creator_event` | fila TEMPLATE_CREATOR | This Lambda function is intended to process TemplateCreator upload orders (creation of documents/templates from uploaded files). |
