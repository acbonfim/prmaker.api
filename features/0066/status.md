# Feature 0066 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md)
> Branch: `feature/0066` em `prform.api` (backend + skills) e `solvace.prform.web/prform-app` (frontend).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

## Fases

| Fase | Descrição | Repo | Status | Responsável | Commits |
|---|---|---|---|---|---|
| B1 | `ReverseIntegrations` | back | ✅ | Claude (sessão principal) | 5bdf4b6 |
| B2 | Relações efetivas no mapa/projeto/espelho | back | ✅ | Claude (sessão principal) | 5bdf4b6 |
| B3 | Checagem + modelos do INT | back | ✅ | Claude (sessão principal) | 5bdf4b6 |
| B4 | Config `ReverseEngineeringGeneration` | back | ✅ | Claude (sessão principal) | 5bdf4b6, 3aa088b |
| B5 | Testes | back | ✅ | Claude (sessão principal) | 5bdf4b6, 3aa088b |
| S1 | `re_tool.py` areas/pacote/faltando/evidencia/cartao | skill | ✅ | Claude (sessão principal) | 18a45f6, 3aa088b |
| S2 | Retrato do banco e da AWS | skill | ✅ | Claude (sessão principal) | 18a45f6 |
| S3 | `re.sh` comandos novos | skill | ✅ | Claude (sessão principal) | 18a45f6, 3aa088b |
| S4 | SKILL.md e referências | skill | ✅ | Claude (sessão principal) | 18a45f6, 3aa088b |
| S5 | Testes dos scripts | skill | ✅ | Claude (sessão principal) | 18a45f6, 3aa088b |
| F1 | Mapa com itens/origem | front | ✅ | Claude (sessão principal) | front 3562ea8 |
| F2 | "Por pergunta" | front | ✅ | Claude (sessão principal) | front 3562ea8 |
| Q1 | Build, e2e local, piloto | ambos | ✅ | Claude (sessão principal) | ver notas |
| Q2 | PRs, merge e deploy | ambos | ⬜ | Claude (autorizado pelo usuário) | |

## Decisões

| # | Decisão | Motivo |
|---|---|---|
| D1 | Integrações da ER calculadas do índice na leitura (não gravadas no projeto) | corrige os já publicados sem republicar; uma fonte só |
| D2 | Retrato local (`~/.prmake/reverse/_retrato`), não central | escopo; quem gera tem o acesso; central fica para depois |
| D3 | Modelo dos subagentes por configuração, padrão `sonnet` (Haiku só se o admin configurar) | "mesma qualidade" — o barato vem da estrutura; Haiku sem medição é risco |
| D4 | Avisos (não erros) para INT sem chave/mecanismo | documentos antigos continuam publicáveis |
| D5 | Merge dos dois PRs juntos ao final | autorizado pelo usuário em 2026-10-06 ("ao final pode mergear e publicar") |

## Notas de handoff

### Backend (B1–B5) ✅
- `ReverseIntegrations` (domínio) lê o INT; `ReverseRelations` (aplicação) aplica as relações efetivas na leitura (mapa,
  projeto, "Usado por", módulo da ER/MCP, relacionados da sessão, espelho). Publicar/editar seção `re-*` só remove as
  relações `re#` antigas do projeto (`DropReverseRelations`); não grava mais relação nova.
- Checagem: avisos de Módulos não reconhecido / Mecanismo ausente ou fora do vocabulário (com os documentos reais do
  checklist: 0 erros; os avisos apontam chaves erradas como `legado-administracao` → `legado-administration`).
- `ReverseEngineeringGeneration` (padrão no código + migração `20261006060656`), exposto em `/ReverseEngineering/settings` → `generation`.
- Testes: 129 no `solvace.knowledge.tests`.

### Skill (S1–S5) ✅
- `re_pacote.py` (áreas, pacote com apoio, cartão, faltando, juntar, compactar, evidência), retrato do banco
  (`re_banco.py retrato` + fonte "retrato" no catálogo) e da AWS (`re_infra.py --retrato-dir`), comandos novos no `re.sh`.
- Inventário pega `ewcmAlert/ewcmConfirm` + `GetLanguageByTerm` e `Url.Content("~/…")` das views do legado .NET Core.
- `tools/SkillTests`: 24 testes (inclui retrato do banco com `sql-query.sh` falso).
- O retrato ao vivo da DEMO não pôde ser testado aqui (sem VPN — timeout); o tratamento de erro funcionou (exit 3, mensagem).

### Front (F1–F2) ✅
- `kb-by-question.component.ts` (Por pergunta), itens INT nas arestas do mapa (`openItem`), INT como link nas Integrações.
- e2e local (`.t0066/ui.mjs`, Postgres isolado com os 94 projetos do espelho e a ER real de checklist/actionplan/rca): 11/11.

### Piloto (Q1) ✅
Área `chk-rpt-checklist-performance` do funcional do `legado-checklist` (as telas do card 75294), subagente Sonnet só
com cartão + pacote, comparada às áreas do funcional geradas antes (mesmo módulo, Sonnet):

| | Antes (áreas do funcional) | Piloto 1 (pacote) | Piloto 2 (pacote + apoio) |
|---|---|---|---|
| Chamadas | 43–117 | 13 | 10 |
| Leitura de cache | 8,3–29,6 M | 1,23 M | 0,93 M |
| Contexto máximo | 336–438 mil | 155 mil | 136 mil |
| Tempo | 10–19 min (+ retomadas) | 7,4 min | 7,4 min |
| Itens | — | 78 (com GLO/PRF repetidos) | 49 (sem GLO/PRF da área) |
| Evidência conferida | — | 164/164 | 93/93 |

Publicado para as mesmas telas: 19 itens (8 RN); o piloto 2: 26 RN + 12 GAP. Estimativa do funcional inteiro (25
áreas): ~25 M de leitura de cache (antes ~87 M só no funcional) e ~35–40 min com 5 em paralelo (antes 45+ min, e 200
min quando o limite caiu). O tempo por área é dominado pela escrita (saída) — paralelismo é a alavanca de tempo.
Limite visto: o subagente gravou a parte de uma vez no fim (o checkpoint a cada 10 itens nem sempre é seguido).

### Ajustes depois do SOC (`feature/0066-ajustes`)
Primeira geração real com o fluxo novo (`legado-soc`, funcional e arquitetura em paralelo, 2026-10-06): áreas em ~10 min
(13 por documento, 0,3–3,5 M cada), mas a sessão principal (Opus) gastou mais ~13–17 min e ~10 M juntando duplicados do
que é do módulo inteiro (tabelas/objetos do banco, tecnologias, configuração, glossário) e movendo tipos de outro
documento. Funcional enviado em 26 min. Ajustes:
- arquivo que cabe no orçamento não é partido entre áreas (o `soc_busca_ajax.asp` partido fez um subagente ir atrás da
  outra metade: 29 chamadas, 3,5 M); arquivo grande começa área nova;
- apoio com as funções compartilhadas do legado que a área chama (`systems/includes` ASP/JS, `view_shared`), sem libs;
- **subagentes especiais** despachados junto com as áreas: funcional → `glossario`; arquitetura → `banco` (catálogo/
  retrato: tabelas, objetos, triggers, jobs) e `modulo` (tecnologias com os sinais do código, configuração só por nome,
  segurança, observabilidade, infra); as áreas recebem a lista dos tipos que criam;
- `maxParallel` 13 na configuração de produção (já estava; regravado sem mudar as outras 22 chaves).

### Ajustes 2 (`feature/0066-ajustes2`)
- Visão e spec de arquitetura são SÍNTESES (o modelo diz "a partir do levantamento"): `re.sh areas` não divide o código e
  `re.sh pacote` monta `pacote-sintese.md` com os itens do funcional/arquitetura (rascunho local, senão o publicado) —
  inteiros os que a síntese usa, só ID + título o resto (SOC: visão 51 KB, spec 156 KB, contra 908 KB de código).
- IDs usados = publicados + `documento.md` de cada documento (partes/pacotes/modelos tinham IDs de faixa e empurravam a
  base); no `juntar`, os IDs dos outros documentos são lidos de novo — a arquitetura do SOC, gerada em paralelo com o
  funcional, colidiu GAP/CFG e a sessão principal teve de renumerar à mão.

### Ajustes 3 (`feature/0066-ajustes3`)
- Código de teste fora do inventário e das áreas (`tests/`, `*.Tests`/`*.UnitTests`, `*Test(s).cs`, `.spec/.test.ts`):
  no revamp-users eram 768 KB de 2.003 KB (38%) e 11 das 28 áreas por documento; agora 17 áreas e ~1,4 MB.

### Ajustes 4 (limite e inchaço)
- O limite de 600 mil caracteres por documento era só validação (coluna `text`, servidor aceita 30 MB): virou 2 milhões
  (#111). No revamp-users o funcional passou (878 mil) e as sessões compactaram as partes para 60%: 17 subagentes e
  38 M de leitura de cache (mais que gerar) e itens fundidos/removidos.
- Causa do inchaço: cada área colava a sua versão das seções do módulo (resumo, perfis, integrações em prosa) e repetia
  endpoints/integrações externas. Cartão: área só escreve as seções dos seus itens; API é de quem tem a rota no pacote;
  INT com `ext:` é do especial `modulo`. `juntar` funde repetidos (mesmo tipo e título, referências remapeadas — nos
  documentos reais ainda achou 21 API + 2 INT e 9 CFG) e mostra o tamanho × limite; a SKILL proíbe compactar regras.

## Log

- 2026-10-06 — spec/plan/status criados a partir da análise da sessão (card 75294, sessões do `legado-checklist`).
- 2026-10-06 — B1–B5, S1–S5, F1–F2 implementados; e2e local 11/11; pilotos 1 e 2; PRs e merge (autorizado).
