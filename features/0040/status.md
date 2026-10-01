# Status — Feature 0040

Branch `feature/0040` em `prform.api-0040` (backend + skills) e `prform-app-0040` (front).

| Fase | Descrição | Status | Commits |
|---|---|---|---|
| B1 | Registro de perguntas | ✅ concluída | 642311d |
| B2 | Pergunte de operação (kind, reforço, passo a passo, templates) | ✅ concluída | 642311d |
| K1 | Projeto `operacao-plataforma` | ⏳ | |
| S1 | `mapear.py operacao` | ✅ concluída | 0592f5c |
| S2 | Skill: mapear operação + resolver perguntas | ✅ concluída | 0592f5c |
| F1 | Painel "Perguntas sem resposta" | ✅ concluída | front 76dec2e |
| Q1 | Bateria de 40 perguntas (antes/depois) | 🔄 linha de base feita | |
| P1 | Piloto em 3 módulos + transversal | ⏳ | |
| G1 | Geração em massa | 🔄 autorizada (o usuário liberou aplicar, mesclar e publicar tudo) | |

## Log
- 2026-10-01 — spec, análise e plano (0039 já estava em uso por outra feature — "Analisar pelo PRMake sem abrir o
  Claude Code").
- 2026-10-01 — Q1 linha de base (produção): 10 respondidas, 5 parciais, 25 sem resposta de 40 (`bateria/antes.jsonl`).
- 2026-10-01 — B1/B2 (642311d): registro de perguntas + Pergunte de operação; 50 testes. Reforço das seções de operação
  2,5× (1,7× perdia para o título "Módulos e fluxos", que casa com "módulo" em todo projeto).
- 2026-10-01 — S1/S2 (0592f5c): `operacao.py` — catálogo do sandbox EFESO (TST): 80 aplicações, 1075 itens de menu,
  119 papéis, 65 parâmetros de planta, 101 globais → 32 módulos com `085-operacao.md` e 14 aplicações sem projeto no
  catálogo do `operacao-plataforma`; skill com `perguntas`/`pergunta-respondida` e `references/operacao.md`.
- 2026-10-01 — F1 (front 76dec2e): painel "Perguntas" do admin e aviso no Pergunte. Teste local: mesma pergunta com
  acento/caixa diferentes soma no mesmo registro (2×, kind operacao), resolver grava seção e autor, `arch.sh perguntas` ok.
- 2026-10-01 — usuário liberou: aplicar tudo, mesclar e publicar sem pedir permissão.
