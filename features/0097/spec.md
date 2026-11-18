# 0097 — Engenharia reversa mais barata, aberta e ancorada na master

Origem: avaliação da ER em 2026-11-18 (sessão do Claude Code). Medições: 147 sessões do executor, 36 itens conferidos contra
a master, 55 sessões de geração. Pedido do usuário (2026-11-18):

1. **Todos acessam a ER** — hoje (0074) só `admin` lê: a análise de quem não é admin, o `for-card`, a busca do MCP e o
   Pergunte voltam vazios. Leitura para todo usuário autenticado; gerar, aprovar e publicar continuam como hoje.
2. **Ler da master** — a geração já lê a `origin/master` desde a 0067, mas os documentos publicados apontam para
   `hotfix/73821` (legado, 25/09) e um commit de julho fora da master (front). Garantir que nada seja gerado fora da
   master sem aviso, citar o caminho completo a partir da raiz do repositório no `Onde:` e **reancorar** (sem IA) as
   linhas dos documentos publicados na master atual, marcando os itens cujo código mudou.
3. **Parar de gerar design e visão**; **visão prática só sob demanda**, quando o Pergunte não souber responder (perguntas
   sem resposta do módulo); **tirar os GAPs que falam do próprio gerador** ("fora do pacote", "sem Figma"…).
4. **Índice técnico sem IA** — rotas/endpoints, tabelas, componentes/telas do front, páginas ASP, procedures/views/
   triggers/jobs, chaves de configuração e mensagens, extraídos deterministicamente (o `inventario` já extrai), com o
   commit, publicados direto na Base (sem aprovação — não há texto de LLM) e regeráveis a qualquer hora para todos os
   módulos (inclusive os do revamp que não têm ER).
5. **Achar o módulo do card pelo histórico** — além do campo Module (que erra: "All", módulo trocado — 77697, 78266,
   77698), usar os módulos que cards parecidos realmente leram.
6. **Geração 50% mais barata e ~60% mais rápida, mesma qualidade** (cobertura ≥ 90%, evidência conferida, precisão
   amostral ≥ a atual de 89%).

Base de comparação (medida, mediana por módulo, cache lido / tempo de parede):
funcional 32 M / 21 min · arquitetura 23 M / 20 min · prática 20 M / 13 min · design 17 M / 16 min ·
spec-arquitetura 3,1 M / 11 min · visão 2,7 M / 6 min → **~98 M e ~87 min por módulo**. A sessão principal (Opus)
consome ~39% do total consolidando.
