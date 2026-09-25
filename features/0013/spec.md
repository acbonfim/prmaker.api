Tipo: Melhoria (custo de infraestrutura / tempo real)
Prioridade: alta
Origem: análise de custos do GCP (2026-09-25), redigida pelo Claude a pedido do usuário.

Objetivo: tirar o hub de tempo real (SignalR, `/ws`) de dentro da API no Cloud Run e hospedá-lo num **relay** no MonsterASP, que já é pago (custo fixo). O Cloud Run volta a cobrar só pelos requests de verdade.

Situação atual (medida em 2026-09-25):

1. O Cloud Run cobra CPU/memória **enquanto houver um request aberto**. Um WebSocket conta como request aberto.
2. A `cime-pullrequest` teve **~100% do tempo cobrado** (164.632 s em 47 h), mesmo de madrugada com 3–6 requests/hora. Os logs mostram `GET /ws` (101) durando até 3601 s (o timeout) e reconectando. Uma aba esquecida aberta mantém a instância ligada 24 h.
3. Projeção: **~US$ 61/mês** só nisso. Sem o WebSocket, tudo cabe na cota grátis (~US$ 0,50/mês de DNS, secrets e egress).
4. Efeitos colaterais do desenho atual: `max_instances = 1` (sem backplane) limita a 80 conexões simultâneas. A chave do hub (`123456789`) está fixa no front, vai na query string e fica gravada no Cloud Logging.

Requisitos:

1. O relay é um app ASP.NET Core separado (SignalR), publicado no MonsterASP (suporta WebSocket, .NET 8, Let's Encrypt e Web Deploy).
2. A API continua publicando pelos mesmos pontos (`IRealTimeNotifier`). Nenhuma regra de negócio muda. Só troca a implementação: POST HTTP para o relay com chave servidor-a-servidor.
3. O navegador conecta no relay com um **token de curta duração** emitido pela API (endpoint autenticado com a api-key do usuário). A chave fixa sai do front.
4. Os eventos, grupos e payloads chegam ao front exatamente como hoje (mesmo JSON do SignalR).
5. Falha do relay **nunca** quebra uma operação da API. A notificação é best-effort, com timeout curto.
6. Modo em processo continua disponível por configuração (`RealTime:Mode = InProcess`), para desenvolvimento local e rollback.
7. Ao reconectar (queda de rede, reciclagem do relay), as telas recarregam os dados, porque os eventos perdidos durante a queda não são reenviados.
8. Sem dependência de GCP nem de fornecedor novo: o relay roda em qualquer host .NET.

Fora do escopo: backplane/Redis, autorização por grupo (hoje qualquer conexão entra em qualquer grupo), desconectar abas inativas.
