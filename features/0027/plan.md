# Feature 0027 — Catálogo de tratamentos na analisar-bug (aprendido dos cards reais) e fechamento sem código

> Spec: [`spec.md`](./spec.md) · Status: [`status.md`](./status.md)

## 1. Estudo (2026-09-29, somente leitura)
- **Azure DevOps (WIQL)**: 300 bugs com `Custom.ResolutionType` preenchido, alterados nos últimos 120 dias. **58% sem código.**

  | Resolution Type | Cards | General Classification · Classification mais comuns |
  |---|---|---|
  | Code Fix | 97 | Code · Code Required - Code Defect (85); Code · Data Fix - Caused by Defect (5) |
  | User Education | 84 | No Code · Not a Defect - Training (72) |
  | Configuration (Script) | 33 | No Code · Environment / Platform (11); Code · Data Fix - Caused by Defect (8); No Code · Data Fix - Caused by User Action (5) |
  | Configuration | 32 | No Code · Environment / Platform (18); No Code · Change Request / Missed Requirement (6) |
  | Cannot reproduce | 28 | No Code · Environment / Platform (11); No Code · No user feedback - Pending information (8) |
  | Duplicated | 10 | Duplicated · Ticket duplicated (9) |
  | Not Mapped Requirement | 8 | No Code · Environment / Platform / Change Request |
  | Change Request | 8 | No Code · Change Request / Missed Requirement (5) |
- **Root causes (amostra por tipo)**: script de dados (registro excluído restaurado, preferência de usuário ausente, flag de menu, `LAST_SITE_ID`), acesso/Cognito (usuário deslogado/desabilitado, SSO com e-mail trocado, nativo × federado), configuração/ambiente (WAF, tamanho de instância, parâmetros), orientação (regra de negócio, filtro ativo, uso incorreto).
- **Timelines do PRMake** (10 dos 120 cards mais recentes têm timeline): código → branches + PRs; script → diagnóstico SQL/Cognito, chamado no Freshservice, validação depois do script; user education → diagnóstico + correção pela tela do sistema ou orientação; **fechamento comum**: RC no DevOps, classificação, resumo não técnico PT/EN, Remaining Work zerado, mover para Test in production.

## 2. Decisões
- **SKILL.md**: catálogo de padrões A–H (defeito de código, dados via script/chamado, acesso/Cognito, configuração/ambiente, user education, change request, não reproduz/sem retorno, duplicado) com sinais, diagnóstico, etapas típicas (ponto de partida, não lista fixa) e a classificação; a análise traz o "padrão de tratamento provável"; as perguntas confirmam padrão(ões) e fechamento; o plano de correção é montado a partir do(s) padrão(ões) + especificidades do card e sempre termina com o fechamento.
- **prmake-plan.sh devops**: fechamento de qualquer card pelos endpoints das Ações DevOps do PRMake — `rootcause`, `summary`, `classify <tipo>` (presets com os valores reais; usa o `azure-fields.sh` da gerar-prmake, sem alterá-la), `zero-remaining`, `ready-for-qa`, `test-in-production`, `initial-estimate`.
- Nada de dados pessoais no repositório: o estudo fica agregado (os dados brutos ficaram só na máquina, fora do git).
