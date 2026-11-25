# 0079 — Dados de uso dos usuários (logins, presença ao vivo e análise de uso)

## Pedido (do usuário, 2026-11-04)
Objetivo: ter dados de uso dos usuários. O usuário pediu que o Claude escreva, planeje, implemente e mescle sem perguntar.
1. Registrar **todo login** do usuário.
2. Na **tela de usuários**, ver as informações da sessão do usuário: status (ativo usando a aplicação em primeiro
   plano, executando algo em segundo plano etc.), **ausente** (mais de 5 minutos sem atividade), IP e todas as
   informações possíveis do usuário logado.
3. Registrar **logs estratégicos** pela aplicação: caminhos e ações que o usuário percorre, para entender como ele usa
   a aplicação.
4. Na tela de usuários ver tudo isso e ter uma **seção específica de análise**, com gráficos, tabelas e filtros.
5. **Sem aumentar o custo de infra na nuvem** e sem custo significativo de performance. Decidir o que vai sob demanda,
   em tempo real ou em lote.
6. Saber **quem está online agora e o que está fazendo**.

## Decisões (do Claude, autorizado a decidir)

### Onde cada dado trafega (custo)
| Dado | Como | Por quê |
|---|---|---|
| Presença (estado, tela, ação em andamento, IP, navegador) | **Tempo real pelo relay** (SignalR no MonsterASP, custo fixo já pago), em memória | O navegador já mantém o WebSocket com o relay. Heartbeat HTTP na API do Cloud Run seria cobrado; no relay custa zero |
| Login (sucesso e falha) | **Na própria requisição de login** da Cime.Auth (1 INSERT) | Já existe a requisição; captura IP/navegador e falhas que o front não vê |
| Navegação, cliques, ações, erros, tempos | **Em lote** do navegador para a API: a cada 5 min, 200 eventos, ou ao fechar/ocultar a aba (`fetch keepalive`) | Uma requisição curta por lote. Aba parada não envia nada (sem eventos, sem requisição) |
| Análise (gráficos, tabelas, histórico do usuário) | **Sob demanda**, só quando um admin abre a tela; agregação no banco (`GROUP BY`) | Nada calculado em segundo plano |

Estimativa: ~12 lotes/hora por usuário ativo × ~150 ms ≈ 2 s/h de instância por usuário — dentro da cota grátis do
Cloud Run. Banco no MonsterASP (custo fixo). Retenção de **180 dias** (limpeza feita pelo próprio endpoint de lote, no
máximo uma vez por dia por instância).

### Estados de presença
- **Ativo** — aba visível e com interação (mouse/teclado/toque/rolagem) nos últimos 5 min.
- **Executando** — há algo em andamento (requisição longa, IA gerando, plano de execução rodando na tela). Mostra o
  que é. Vale em primeiro ou segundo plano.
- **Em segundo plano** — aba oculta/minimizada há menos de 5 min sem atividade.
- **Ausente** — mais de 5 minutos sem interação (aba visível ou não).
- **Offline** — sem conexão; mostra "visto por último" (último lote/login).
- Várias abas do mesmo usuário: cada aba é uma sessão; o usuário aparece com o estado "mais ativo" (Executando >
  Ativo > Segundo plano > Ausente) e a lista de abas no detalhe.

### O que é registrado
- **Login**: data, usuário (ou o nome digitado, se não existe), resultado (ok, senha incorreta, não encontrado,
  desativado, e-mail não confirmado), IP, navegador (user agent). Também o `DataUltimoLogin` na listagem.
- **Sessão (aba)**: início, última atividade, fim, IP, navegador/SO/dispositivo, resolução da tela, idioma, fuso,
  versão do app, tela atual, tempo em primeiro plano / segundo plano / ausente, nº de telas e ações.
- **Eventos**: `view` (tela visitada com o tempo nela em primeiro plano), `click` (rótulo do botão/link/aba/menu e a
  tela), `action` (chamadas que alteram dados — POST/PUT/PATCH/DELETE — com nome amigável, status e duração), `error`
  (respostas ≥ 400 e falhas de rede), `slow` (leitura ≥ 3 s), `track` (ações estratégicas nomeadas: logout, busca de
  card etc.), `login`/`logout`.
- Nunca grava valores digitados, corpo de requisição ou resposta. Rótulos cortados em 80 caracteres; ids numéricos e
  GUIDs das rotas viram `:id` (o card da tela fica no campo próprio).

### Telas
- **Gestão de usuários**: cada cartão ganha o selo de presença (ponto + estado + tela/ação, "ausente há 12 min",
  "visto há 2 h"); no topo, a faixa **"Online agora"** com quem está conectado e o que está fazendo, ao vivo; filtro
  "só online". Clique no selo → **painel do usuário**: abas abertas ao vivo (estado, tela, ação, IP, navegador, SO,
  dispositivo, tela, idioma, fuso, conectado desde, última interação), últimos logins, últimas sessões e o caminho
  recente (eventos).
- **Análise de uso** (rota `auth/user/usage`, admin, também no menu e com alternância no cabeçalho da gestão):
  filtros (período 24 h/7/30/90 dias ou personalizado, usuário, tela, tipo de evento), indicadores (usuários ativos,
  sessões, logins e falhas, tempo médio de sessão, tempo em primeiro plano, ações), gráficos (usuários ativos por dia,
  logins por dia ok × falha, mapa de calor dia da semana × hora, telas mais usadas, ações mais usadas, navegadores/SO/
  dispositivos), tabela por usuário e tabelas paginadas de logins, sessões e eventos (com filtros).

### Segurança
- Presença e análise só para **admin** (rota com `AdminGuard`, endpoints `[Authorize(Roles = "admin")]`, hub confere o
  papel `admin` no token de tempo real, que passa a levar `name` e `role`).
- O grupo de presença não pode ser assinado pelo `AddToGroup` genérico.
- O IP vem do último salto do `X-Forwarded-For` (o que o proxy do Cloud Run acrescenta) ou da conexão.
