# Feature 0034 — Todo o revamp mapeado, interdependências e mapa do ecossistema

## Decisões
1. **Cópias rasas à parte** (`~/repos/solvace/kb-mirror/<repo>`, `--depth 1` da branch principal): não mexe nos clones de trabalho
   do usuário (branches/alterações locais). 54 repositórios: módulos de negócio + infra compartilhada (api-gateway-infra, api-infra,
   BuildingBlocks, ModuleIntegration, Datalake, AuditTrail, wiki) + `edv-solvace-hubspotApi`. Fora: experimentais/modelos.
2. **Extrator determinístico** (`base-solvace/scripts/mapear.py`): de cada repositório tira fatos (projetos e frameworks, rotas HTTP,
   comandos/queries, tabelas, chaves de configuração que apontam para outros módulos, clientes HTTP, eventos/filas/tópicos, Lambdas,
   hubs SignalR, pacotes Solvace.*) → `fatos.json` + seções em markdown. Barato de rodar de novo (a base se mantém atualizada) e
   não depende de ler cada arquivo com o modelo.
3. **Interdependências cruzando os fatos de todos os módulos** (`relacoes.py`): HTTP (URL/config apontando para o módulo), eventos
   (quem publica × quem consome), banco (tabela de outro módulo pelo prefixo `TB_<SIGLA>_`), pacotes (Integration/Contracts de outro
   módulo), front (app Angular → API). Cada relação guarda a **evidência** (arquivo/chave).
4. **Relações como dado estruturado no PRMake** (não só texto): `ArchitectureProject.Relations` (jsonb: alvo, tipo, detalhe,
   evidência) + `GET /Architecture/graph` (nós e arestas). O índice ganha "integra com" por projeto; o pacote do espelho leva
   `graph.json`.
5. **Mapa interativo** na tela: grafo do ecossistema (cytoscape.js sob demanda, como o mermaid) com filtros por tipo de relação e de
   projeto, clique para destacar vizinhos e abrir o projeto; no projeto, painel "Depende de / Usado por" agrupado por tipo com a
   evidência.
6. **KC sempre atualizado**: agendamento local (`kb.sh agendar install` → LaunchAgent a cada 2 h: `kc.sh sync` + `kb.sh sync`, só
   em máquina com credencial), sync também no início de sessão (hook das skills) e aviso na tela quando a última sincronização passa
   de 24 h.

## Fases
| Fase | Descrição |
|---|---|
| M1 | Cópias rasas dos 54 repositórios |
| S1 | `mapear.py` (fatos + seções) e `relacoes.py` (interdependências) na skill base-solvace |
| B1 | `Relations` no projeto + `GET /Architecture/graph` + índice/espelho com as relações |
| G1 | Gerar e revisar a base de todos os módulos + HubSpot; publicar |
| F1 | Mapa do ecossistema (grafo) + painel de integrações no projeto + visão geral melhor |
| K1 | KC sempre atualizado (agendamento, hook, aviso de atraso) |
| Q1 | Teste local, PRs |
