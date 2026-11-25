# Plano — Feature 0082

| Fase | Descrição | Depende |
|---|---|---|
| B1 | Correção sobre análise vazia parte da última análise feita; a vazia é cancelada (`ExecutionPlanApplication.CreateAsync`) | — |
| B2 | Migração `ReparentCorrectionsFromEmptyAnalysis0082` (dados: o mesmo para o que já existe, ex. 77900) | B1 |
| F1 | Botão da versão (abre o histórico) + barra com as etapas da versão em tela | — |
| F2 | "Versão anterior" por fase, ignorando a versão vazia | — |
| F3 | "Ver a análise completa" / "Ver o plano completo" no "Em que pé está" (`PlanDocumentService` + `markdown-view-dialog`) | — |
| F4 | Tour guiado v2 (passos versão, etapas, documento) | F1, F3 |
| Q1 | Build, testes (back 63, front `plan-document.service.spec.ts`), SQL da migração no Postgres local | B1–F4 |

Causa do "nem existe análise": a proposta aceita era de correção, com a análise v1 cancelada. O pedido (`analyze`,
criado pelo #151) rodou o `start` da skill, que abre análise nova quando a atual está cancelada; o `prmake_correction`
pendurou a correção na análise mais recente — a v2 vazia. Correção no servidor (B1) para não depender de nova versão da
skill nem do executor.
