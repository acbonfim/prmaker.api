# Status — Feature 0038

Branch `feature/0038` em `prform.api-0038` (backend + skills) e `prform-app-0038` (front).

| Fase | Descrição | Status | Commits |
|---|---|---|---|
| B1 | Público da seção (`llm`/`human`), export só `llm`, busca/chat | ✅ concluída | b1a9990 |
| B2 | Dados amigáveis do projeto | ✅ concluída | b1a9990 |
| B3 | Gerar guia com a IA | ✅ concluída | b1a9990 |
| B4 | Aprender com um card | ✅ concluída | b1a9990 |
| B5 | Pergunte: cobertura, seção sugerida, analisar a fundo, sugestão `gap` | ✅ concluída | b1a9990 |
| F1 | Modo Simples × Técnico, ficha amigável | ✅ concluída | front 815b0b1 |
| F2 | Visão geral para leigos, glossário, mapa simplificado | ✅ concluída | front 815b0b1 |
| F3 | Admin: guia com IA, público, dados amigáveis | ✅ concluída | front 815b0b1 |
| F4 | Aprender com um card (tela) | ✅ concluída | front 815b0b1 |
| F5 | Pergunte: "leia a seção", lacuna → analisar a fundo → seção proposta | ✅ concluída | front 815b0b1 |
| S1 | Skill base-solvace: template do Guia, `--audience`, `learn` | ✅ concluída | 1244c33 |
| Q1 | Teste local | ✅ backend + front + IA (provedor falso) |  |
| G1 | Carga inicial dos guias (produção) | ⏸ aguarda autorização | |

## Handoff
- Contrato implementado como no plano. Respostas da IA em blocos `<<<TAG ... TAG>>>` (`ArchitectureBlocks`) — markdown com
  mermaid não quebra JSON. `POST projects/{key}/guide` → 409 sem IA, 502 IA falhou. `learn-from-card` → 404 sem nada sobre o
  card, 400 número inválido; sem IA devolve as fontes e as sugestões existentes com `aiUnavailableReason`.
- Retrocompatível: seções existentes ficam `llm`; seção nova `guia-*` nasce `human`; export/índice/ficha/hash só `llm`
  (testado: guia + dados amigáveis não mudam o hash nem o INDEX.md; o zip não leva `guia-*`).
- Front: componentes novos em `architecture/kb-*.ts` (overview amigável, conexões, glossário, revisão do guia, aprender com
  card, analisar a fundo, editor de proposta) + `kb-friendly.ts` (nomes/tipos/frases/glossário/sumário).
- Achado fora do escopo: o provedor **OpenAI** do Solvace.AI nunca é montado — `AIServiceFactory` procura a propriedade
  `Openai` (não `OpenAI`) e o `OpenAIService` falha com "Configurações do OpenAI não encontradas". Gemini/Claude ok.
- Teste local: `.t0038/` (fora do git) — Postgres `cime-pg-0038` (55439), base semeada do espelho (62 projetos, 243 seções),
  `fakeai.py` (formato Gemini, respostas fixas por tipo de prompt) com os plugins "AI Configurations"/"Gemini Plugin" locais.

## Log
- 2026-10-01 — spec, análise e plano. Spec ganhou "aprender com um card" (para os cards que a skill não sugeriu) e o
  "Pergunte" que aponta a seção e cobre lacunas (B5/F5).
- 2026-10-01 — B1–B5 (b1a9990): público da seção, dados amigáveis, guia com IA, aprender com um card, Pergunte com
  cobertura/seção sugerida/análise a fundo, sugestão `gap`; migração `AddGuideAudienceAndFriendly`; 47 testes passando.
- 2026-10-01 — S1 (1244c33): `arch.sh section --audience`, `project --display-name/--tagline/--area`, `publicar-pasta` com
  `guia-*`, `learn <card> [--send]`, `guia <chave> <pasta>`, `lacunas`, `resolver`; template e SKILL.md da base-solvace.
- 2026-10-01 — F1–F5 (front 815b0b1): modo Simples × Técnico, ficha e visão geral amigáveis, Guia, conexões em frases, mapa
  por vizinhança, sumário, artigo formatado, guia com IA (admin), aprender com um card, Pergunte com "Leia a seção" e
  análise a fundo.
- 2026-10-01 — Q1: hash `f959e8ee2a1abc5e` igual antes/depois de criar `login/guia-regras` e dados amigáveis; INDEX.md
  idêntico; zip sem `guia-*`. Sem IA: Pergunte `unknown`, deep com motivo, guide 409, learn 404/400 e com Timeline + sugestão
  existente. Com IA (falsa): "O usuário consegue fazer login com sua senha, ou somente com SSO?" → `partial` + seção
  `login/autenticacao` › "Tabela de registro"; deep leu 10 seções e propôs `login/guia-regras` (Guia) como lacuna com o que
  conferir no código; guia → nome/frase/área + 2 seções; learn → resumo + 2 propostas (a de projeto inexistente avisada).
  Telas conferidas no Chrome headless (Simples/Técnico, ficha, mapa, aprender, pergunte, deep, revisão do guia).
