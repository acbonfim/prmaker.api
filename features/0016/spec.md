Tipo: Limpeza técnica (bancos, código e configuração legados)
Prioridade: média (a parte de código destrava o .NET 10; a parte de bancos espera o período de segurança)
Origem: conversa de 2026-09-26 (redigida pelo Claude a pedido do usuário). Depois da 0014 (auth no Postgres) e da 0015 (API principal no Postgres).

Objetivo: remover o que ficou para trás nas migrações para o Postgres e para o GCP:

- os bancos MySQL (`db31021`) e SQL Server (`db30567`) do MonsterASP;
- os providers e pacotes que não são mais usados;
- as chaves de configuração e os secrets antigos;
- as senhas que estão nos `appsettings` versionados;
- o caminho legado do tempo real (chave fixa `123456789`);
- arquivos e documentação obsoletos.

Motivação:

1. Menos superfície: hoje os `appsettings.json` versionados têm senhas de banco, SMTP, token do GitHub, PAT do Azure e segredo JWT.
2. Menos dependências: Pomelo (MySQL), SQL Server e Sqlite saem. **Sem o Pomelo, o upgrade para o .NET 10 fica livre** (o suporte ao .NET 8 acaba em 10/11/2026).
3. Menos confusão: documentação e configuração passam a refletir só o que existe (Postgres, Cloud Run, relay).

Requisitos:

1. **Nada que ainda sirva de fallback é apagado antes da hora.** O código sai cedo (as revisões antigas do Cloud Run continuam existindo para rollback); secrets, envs e bancos só depois do período de segurança de cada virada, com backup final guardado pelo usuário.
2. A remoção dos secrets/envs de rollback é o **ponto sem volta** e precisa de confirmação explícita do usuário.
3. Toda credencial que esteve no histórico do git (ou nas conversas) e continua válida é **trocada** (rotação), não só apagada dos arquivos.
4. Build, deploy e o app funcionando igual depois de cada etapa.
