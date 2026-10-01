# Status — Feature 0040

Branch `feature/0040` em `prform.api-0040` (backend + skills) e `prform-app-0040` (front).

| Fase | Descrição | Status | Commits |
|---|---|---|---|
| B1 | Registro de perguntas | ✅ concluída | 642311d |
| B2 | Pergunte de operação (kind, reforço, passo a passo, templates) | ✅ concluída | 642311d |
| K1 | Projeto `operacao-plataforma` | ✅ concluída | produção |
| S1 | `mapear.py operacao` | ✅ concluída | 0592f5c |
| S2 | Skill: mapear operação + resolver perguntas | ✅ concluída | 0592f5c |
| F1 | Painel "Perguntas sem resposta" | ✅ concluída | front 76dec2e |
| Q1 | Bateria de 40 perguntas (antes/depois) | ✅ 10 → 39 de 40 | `bateria/` |
| P1 | Piloto em 3 módulos + transversal | ✅ feito junto com o G1 | |
| G1 | Geração em massa | ✅ concluída | produção |

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
- 2026-10-01 — PRs #62 (back) e prmakerweb#36 (front) mesclados juntos e publicados; depois #63, #64 e #66 (Pergunte:
  bloco inteiro das melhores seções, bloco que mais casa com a pergunta, palavras da pergunta e nova busca com poucos
  candidatos).
- 2026-10-01 — K1/G1 em produção: projeto `operacao-plataforma` (técnica `operacao` levantada do código, `catalogo-modulos`
  e `parametros` do catálogo; Guia `guia-o-que-e`, `guia-como-configurar`, `guia-perguntas` com as perguntas como títulos);
  seção `operacao` (catálogo: telas, papéis, parâmetros) em 32 módulos; Guia `guia-como-configurar` gerado pela IA do
  PRMake nos 32; procedimentos levantados do código (subagentes) em 15 módulos — Plano de Ação, Defect Tag, Incidentes,
  Checklist, CIL, MOC, Permissão de Trabalho, Treinamento, RCA, Condição Insegura, Usuários (times), Digital Obeya,
  Pesquisa, Score Card, Campos personalizados — na seção técnica e como "Perguntas frequentes de configuração" no Guia.
- 2026-10-01 — Bateria (produção, 40 perguntas): linha de base 10/5/25 (respondidas/parciais/sem resposta) → após
  publicar 16/10/14 → bloco inteiro 21/6/12 → procedimentos 36/0/4 → termos da pergunta **39 respondidas + 1 timeout**
  (refeita: respondida). Variação entre execuções vem dos termos da IA; arquivos em `bateria/*.jsonl`.
- Achados a tratar fora da base: ART-23 do KC diz que o tipo de plano de ação é cadastrado em Settings (não existe);
  endpoints protegidos só pelo menu (`PUT /Modules`, Users, Survey, CustomField, ScoreCard, Times `Save`); cache de
  parâmetros não invalidado ao salvar (`PUT /ClientFuncionality`, `PUT /Parameter/site`); provedor OpenAI do Solvace.AI
  quebrado (`Openai` × `OpenAI`).
