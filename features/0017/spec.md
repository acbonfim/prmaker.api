Tipo: Melhoria técnica (plataforma)
Prioridade: alta — o suporte ao .NET 8 acaba em 10/11/2026
Origem: conversa de 2026-09-26 (redigida pelo Claude a pedido do usuário).

Objetivo: levar a solução inteira (API principal e módulos, Cime.Auth, relay de tempo real, building blocks e o migrador) do .NET 8 para o **.NET 10 (LTS)**, sem mudança de comportamento visível.

Contexto: o upgrade estava bloqueado pelo provider MySQL (Pomelo, sem EF Core 10), que saiu na 0015/0016. Hoje todos os bancos são PostgreSQL (Npgsql, que tem versão para o EF Core 10).

Requisitos:
1. Todos os projetos em `net10.0`, com pacotes nas versões 10.x correspondentes (EF Core, ASP.NET, Npgsql, JwtBearer) e SDK fixado.
2. Trocar o versionamento de API descontinuado (`Microsoft.AspNetCore.Mvc.Versioning`) pelo `Asp.Versioning`, mantendo as rotas `/api/v1/...`.
3. Swagger funcionando nas duas APIs.
4. Mesmas respostas da API (contrato), mesmos tokens: api-keys e sessões atuais continuam válidas; tokens do tempo real aceitos entre API e relay durante a transição.
5. Migrações do EF sem alteração de schema (nenhuma migração nova, a não ser que o EF 10 exija).
6. Imagens Docker, pipeline e relay (MonsterASP) em .NET 10.
