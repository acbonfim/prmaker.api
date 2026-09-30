# Status — Feature 0034

| Fase | Descrição | Status |
|---|---|---|
| M1 | Cópias rasas dos 54 repositórios (`kb-mirror`: 53 revamp-* + hubspotApi) | ✅ concluída |
| S1 | mapear.py (fatos/relacoes/secoes/tudo) + descrição do revamp-wiki + arch.sh publicar-pasta | ✅ concluída |
| B1 | Relations + graph + índice compacto + ficha `000-projeto.md` no espelho | ✅ concluída |
| G1 | Base de todos os módulos + HubSpot (`features/0034/kb`, 64 projetos, ~150 relações); publicar após o deploy | 🔄 gerada, publicação pendente |
| F1 | Mapa do ecossistema (cytoscape) + integrações no projeto + hubs na visão geral | ✅ concluída |
| K1 | KC sempre atualizado (hook em 2º plano, `kb.sh agendar`, aviso > 24 h no `kb.sh index` e na tela) | ✅ concluída |
| Q1 | Teste local (Postgres isolado, 62 projetos/243 seções/149 ligações publicados, telas no Chrome headless), PRs | ✅ PRs abertos |

## Notas
- Índice: com o parque inteiro o formato antigo passava de 10k tokens; agora é uma linha por projeto (~5k) e o detalhe
  (resumo completo, depende de / usado por com evidência) vai para `projects/<chave>/000-projeto.md`.
- Falsos positivos removidos: origens de CORS deixaram de virar dependência de S3.
- Aviso real encontrado: `lambda_bos_user_create_event` declarada em revamp-BOS e revamp-Fishbone (um deploy sobrescreve o outro).
- `revamp-AuditTrail` só tem README de template; `revamp-wiki` é a wiki por LLM (fonte das descrições).
- Teste local: `publicar-pasta` da base inteira em ~18 s; INDEX.md real = 21,5 KB (~5,1k tokens) para 62 projetos;
  mapa com 68 nós/143 arestas; filtros, busca, abrir projeto e painel Integrações conferidos; sem rolagem horizontal a 420 px.
- Depois do merge + deploy (com autorização): `arch.sh publicar-pasta features/0034/kb` em produção e `kb.sh sync`.
