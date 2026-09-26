# Status — Feature 0017 (Upgrade para o .NET 10)

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0017` (`../prform.api-0017`), a partir de `master`.

| Fase | Descrição | Onda | Depende de | Status | Responsável | Commits |
|---|---|---|---|---|---|---|
| B1 | TFM `net10.0`, `global.json`, pacotes 10.x, IdentityModel 8.x, remoção de pacotes sem uso | 1 | — | ✅ concluída | Claude | `eaa4da4` |
| B2 | `Asp.Versioning` + Swashbuckle 10 (OpenApi 2) | 2 | B1 | ✅ concluída | Claude | `6b193ff` |
| B3 | EF Core 10: modelo × snapshot nos 4 contextos | 2 | B1 | ✅ concluída (nenhuma mudança) | Claude | `6b193ff` |
| B4 | Docker 10.0, workflow do relay, docs | 3 | B1 | ✅ concluída | Claude | `bb883c1` |
| T1 | Ensaio local (contrato 8 × 10, tokens cruzados, relay, Swagger) | 4 | B1–B4 | ✅ concluída | Claude | — |
| Q1 | Deploy | 5 | T1 | ⬜ pendente | usuário + Claude | — |

Legenda: ⬜ pendente · 🟨 em andamento · ✅ concluída · ⛔ bloqueada

## Notas
- **Build**: a solução inteira compilou em .NET 10 na 1ª tentativa depois da troca de pacotes. Nenhum aviso de API obsoleta; o que resta é nulabilidade e o `NU1900`, que é o CodeArtifact inacessível sem login (os pacotes vêm do nuget.org; restore local com `--configfile` só com o nuget.org).
- **B3**: `has-pending-model-changes` limpo nos 4 contextos com dotnet-ef 10.0.12 (ferramenta no scratchpad, sem instalar global) → nenhuma migração nova; o `Migrate()` do EF 10 não vai recusar.
- **B4**: imagens `sdk/aspnet:10.0` construídas localmente (~283 MB cada); relay publicado para `win-x86` com runtime 10.0 (`web.config` in-process).
- **T1** (Postgres local com cópia do `db70152`; auth e API em .NET 8 — código da `master` — e .NET 10 lado a lado, com as mesmas chaves de dev; usuário de teste criado só na cópia local):
  - **Tokens cruzados**: api-key .NET 8 ↔ API .NET 10 e vice-versa (200), JWT de sessão aceito pela auth da outra versão, refresh renovado na outra versão, chave adulterada 403, sem chave 401 → **api-keys e sessões atuais continuam válidas**.
  - **Tempo real**: token da API .NET 8 conecta no relay .NET 10 e vice-versa (WebSocket), hubs em processo cruzados também; token inválido e publish sem chave recusados.
  - **Contrato**: 27 leituras sobre os dados reais (cards, PRs do GitHub, handovers, timeline, férias, calendário, saldos, plugins, forms, integrações, tempo real) → **JSON idêntico** entre .NET 8 e .NET 10; header `api-supported-versions: 1.0` igual; Swagger abre nas duas APIs (60 e 32 rotas, OpenAPI 3.0.1 → 3.0.4).
  - **Escritas no .NET 10**: saldo, pedido de férias com data UTC do JSON, aprovação/autorização, calendário, timeline criar/editar/apagar, registro de PR e resumo — sem erro do Npgsql; datas iguais ao comportamento atual (`03:00`).
- **Vulnerabilidades**: MailKit 4.8 → **4.18.0** (GHSA-9j88-vvj5-vhgr), validado em execução (envio chega a tentar o SMTP). **AutoMapper 12.0.0** (GHSA-rvv3-g6hj-g44x, DoS por recursão) só tem correção a partir da 15.1.1, que tem **licença comercial**. Na auth ele só mapeia DTOs internos (19 usos, sem objetos do cliente) → risco baixo; **melhoria registrada: remover o AutoMapper da auth** (mapeamento manual).

## Log
- 2026-09-26 — Planejamento (spec, plano, status); worktree `feature/0017`.
- 2026-09-26 — B1–B4 e T1 concluídas; MailKit atualizado. Falta o Q1 (deploy).
