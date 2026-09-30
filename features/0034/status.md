# Status — Feature 0034

| Fase | Descrição | Status |
|---|---|---|
| M1 | Cópias rasas dos 54 repositórios (`kb-mirror`: 53 revamp-* + hubspotApi) | ✅ concluída |
| S1 | mapear.py (fatos/relacoes/secoes/tudo) + descrição do revamp-wiki + arch.sh publicar-pasta | ✅ concluída |
| B1 | Relations + graph + índice compacto + ficha `000-projeto.md` no espelho | ✅ concluída |
| G1 | Base de todos os módulos + HubSpot (`features/0034/kb`, 64 projetos, ~150 relações); publicar após o deploy | 🔄 gerada, publicação pendente |
| F1 | Mapa do ecossistema + integrações no projeto | ⬜ pendente |
| K1 | KC sempre atualizado (hook em 2º plano, `kb.sh agendar`, aviso > 24 h) | 🔄 skill pronta, aviso na tela pendente |
| Q1 | Teste local, PRs | ⬜ pendente |

## Notas
- Índice: com o parque inteiro o formato antigo passava de 10k tokens; agora é uma linha por projeto (~5k) e o detalhe
  (resumo completo, depende de / usado por com evidência) vai para `projects/<chave>/000-projeto.md`.
- Falsos positivos removidos: origens de CORS deixaram de virar dependência de S3.
- Aviso real encontrado: `lambda_bos_user_create_event` declarada em revamp-BOS e revamp-Fishbone (um deploy sobrescreve o outro).
- `revamp-AuditTrail` só tem README de template; `revamp-wiki` é a wiki por LLM (fonte das descrições).
