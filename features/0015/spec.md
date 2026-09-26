Tipo: Melhoria técnica (banco de dados da API principal)
Prioridade: alta (destrava o .NET 10; o suporte ao .NET 8 acaba em 10/11/2026)
Origem: conversa de 2026-09-26 (redigida pelo Claude a pedido do usuário). Continuação da 0014.

Objetivo: levar os dados da API principal (módulos PullRequests, Vacations e Timeline), hoje no MySQL `db31021`, para o **mesmo PostgreSQL 18** da autenticação (`db70140`), cada módulo no seu schema, migrando **todos** os dados sem perda. Com isso o sistema passa a ter um SGBD só.

Motivação:

1. **Um banco só**: a auth já está no Postgres (0014). O MySQL fica só para o PR.
2. **.NET 10**: o provider MySQL (Pomelo) ainda não tem EF Core 10. Sem ele, o upgrade para o .NET 10 (LTS) fica livre.
3. **Separação por módulo** com schemas de verdade (`prform`, `vacations`, `timeline`, ao lado de `auth`), o que o MySQL não oferece.

Requisitos:

1. `DefaultContext`, `VacationContext` e `TimelineContext` passam a usar Postgres (Npgsql), cada um no seu schema e com histórico de migrações próprio.
2. Migrar todas as tabelas preservando ids, Guids, textos (inclusive os JSON guardados em texto: `Options`, `FieldSettings`, prompts), datas e flags.
3. **Nenhuma mudança visível no comportamento**: mesmas respostas da API (inclusive o formato das datas no JSON), mesmas buscas.
4. Verificação objetiva de "nenhum dado perdido": contagem e hash por linha (o migrador da 0014, generalizado).
5. O MySQL fica intacto como fallback por um período; rollback sem perda.
6. Desenvolvimento local deixa de apontar para o banco de produção (Postgres local em Docker).
