# Status — Feature 0053

Branch `feature/0053` em `prform.api-0053` (API, skills) e `prform-app-0053` (front, `node_modules` → `prform-app-0019`).

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B1 | ✅ concluída | Claude | 15e6dba |
| B2 | ✅ concluída | Claude | 15e6dba |
| B3 | ✅ concluída | Claude | 15e6dba |
| B4 | ✅ concluída | Claude | 15e6dba |
| S1 | ✅ concluída | Claude | 5e351c8 |
| S2 | ✅ concluída | Claude | 5e351c8 |
| S3 | ✅ concluída | Claude | 5e351c8 + ajustes do T1 |
| F1 | ✅ concluída | Claude | front deb469b, 5d40822 |
| F2 | ✅ concluída | Claude | front deb469b |
| T1 | ✅ local (banco falso) · ⏳ DEMO real | Claude | — (harness fora do git em `.t0053/`) |

## Decisões
- Catálogo lê o banco inteiro (sys.objects, dependências) e filtra em Python: a guarda do `sql-query.sh` barra
  palavras como `delete` e nomes `sp_` na consulta; definições/colunas/checks são buscadas por `object_id`.
- Módulo = tabelas `TB_<SIGLA>_` (prefixo mais citado pelo código; `--prefix`/`--sigla` corrigem) + o que depende
  delas (2 níveis) + objetos com a sigla no nome + procedures chamadas pelo código + triggers nas tabelas.
- Jobs: só passos que citam objetos/tabelas do módulo e rodam nos bancos da DEMO; os de outros clientes viram contagem.
- Termos do glossário: rótulos da tela (`GetLanguageByName`, i18n), menus/aplicação da DEMO, sigla; genéricos de
  interface, mensagens e nomes de outros módulos ficam fora; traduções EN/ES do `TB_WCM_LANGUAGE`.
- Sinônimos valem por módulo (um "RCA" do glossário do A3 não expande a busca do Kaizen); nos artigos do KC, todos.
- Lista de trabalho do melhorar: itens afetados pelas **linhas** alteradas (lado antigo do diff ±10 do `arquivo:linha`
  citado) e pelos objetos do banco novos/alterados/removidos.

## Log
- 2026-10-04 — spec e plano escritos a pedido do usuário; worktree `prform.api-0053` (branch `feature/0053` a partir de
  `origin/master` 15151d6). Perguntas em aberto na spec.
- 2026-10-04 — respostas do usuário na spec: bancos DEMO (global + 3 locais, alias prod), msdb acessível, revamp nos mesmos bancos (foco no global), glossário sem número fixo. Primeira leitura: global com 442 tabelas, 18 views, 4 triggers, sem procedures; locais deram timeout (VPN).
- 2026-10-04 — §5 (sumário) e §6 (melhorar fecha o ciclo) acrescentados; fases F2, B4 e S3.
- 2026-10-04 — execução: B1–B4 (tipos SQL/TRG, evidência de banco, glossário com sinônimos, configuração semeada,
  busca com sinônimos na ER e na Base Solvace, decisões sobre sugestões resolvidas na publicação com Timeline do card,
  termos sugeridos), S1–S3 (`re_banco.py`, `re.sh banco|termos|trabalho`, termos, cobertura com banco e glossário,
  retrato do banco na revisão, `--sugestoes`), F1/F2 (sumário robusto nas duas telas, aba Glossário, termos sugeridos,
  sugestões tratadas na revisão, Como gerar). 81 testes do módulo Knowledge.
- 2026-10-04 — T1 (`.t0053/`: Postgres 55453, API 5083, front 4200): v1 real do SA3 publicada com sessão antiga;
  `melhorar` pela skill — inventário (352 itens), banco pelo `sql-query.sh` falso (procedure divergente entre locais,
  trigger, view, check, job com senha mascarada, local PAR inacessível → aviso), 106 termos, lista de trabalho (4
  arquivos e objetos alterados → 39 itens pelas linhas; antes 94 só pelo arquivo); submit com decisões; tela: sugestões
  tratadas, publicar, sugestões resolvidas (aplicada/recusada) e Timeline do card 75067, termos sugeridos → apelido,
  glossário, busca "rca" achando regras que só dizem "A3"; sumário: 173/178 itens no topo (os 5 restantes são os últimos
  do documento, visíveis), Base Solvace 184/190 (idem) incluindo EST-001/CFG-002 com `_`. VPN desligada: leitura real
  da DEMO fica para o primeiro uso.
