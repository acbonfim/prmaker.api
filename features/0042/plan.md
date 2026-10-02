# Plano — Feature 0042

| Fase | O quê |
|---|---|
| B1 | Tokens de entrada/saída separados em `AIGenerateResponse` (Claude, Gemini, OpenAI) |
| B2 | `IAIUsageRecorder` chamado pelo `PluginAIService`; `AiUsageRecorder` no host (DbContext próprio, ação, preço, cabeçalho) |
| B3 | Entidade `AiUsageRecord` + migração `AddAiUsageRecords` com o seed dos preços |
| B4 | `GET api/v1/AiUsage/me` e `GET api/v1/AiUsage` (admin): total, por ação, modelo, dia, usuário e últimas 50 |
| F1 | Interceptor: aviso "IA: 12,8 mil tokens · ≈ US$ 0,0056 (modelo)" com "Detalhes" |
| F2 | Diálogo "Meu consumo de IA" no menu do usuário (7/30/90 dias; admin: Meu/Todos) |
| F3 | `X-AI-Action` em Gerar com IA do PR, passagem de conhecimento e resumo não técnico |
| S1 | `arch.sh guia/learn`: aviso + custo (lido do `X-AI-Usage`); docs: gerar em massa no Claude Code |
| T1 | Teste local com Postgres isolado e IA falsa (formato Gemini com `usageMetadata`) — sem gastar créditos |
