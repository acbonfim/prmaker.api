# Status — Feature 0042

Branch `feature/0042` em `prform.api-0042` (backend + skills) e `prform-app-0042` (front).

| Fase | Status |
|---|---|
| B1–B4 | ✅ concluída |
| F1–F3 | ✅ concluída |
| S1 | ✅ concluída |
| T1 | ✅ concluída |

## Log
- 2026-10-01 — diagnóstico: as ações de IA usam a chave pessoal do "Claude Plugin" (Haiku 4.5); os lotes da skill
  (≈ 130 guias, baterias do Pergunte) custaram ≈ US$ 5–6 dos créditos do usuário.
- 2026-10-01 — implementação e teste local (Postgres 18 isolado na 55442, IA falsa Gemini com 12.000 + 800 tokens):
  `X-AI-Usage: calls=1;in=12000;out=800;cost=0.0056;model=gemini-2.5-flash` (0,3/2,5 por milhão ✓), CORS expõe o
  cabeçalho, `X-AI-Action: pr:generate` gravado, rota `Architecture/ask` → `base-solvace:ask`, `me` e visão do admin
  por usuário; seed da migração grava a tabela de preços como texto e o FieldSettings (Hidden). Front: aviso depois do
  Pergunte e diálogo (desktop e 420 px) no Chrome headless.
