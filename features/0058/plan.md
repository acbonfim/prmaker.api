# Plano — 0058

| Fase | Entrega | Onde |
|---|---|---|
| B1 | Tipo `INF`, evidência `aws`, seção opcional, config `ReverseEngineeringInfra`, migração | CIME/modules/Solvace.Knowledge, Solvace.PullRequests |
| S1 | `re_infra.py` (coletor somente leitura + ligação ao módulo + esteiras do repo) | skills/engenharia-reversa/scripts |
| S2 | `re.sh infra`, cobertura (`aws-*`, `esteira`), `SKILL.md`, `references/infra.md` | skills/engenharia-reversa |
| T1 | Testes C# (`ReverseInfraTests`) + execução real contra a conta (leitura) | tests, local |
