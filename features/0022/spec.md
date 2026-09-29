Tipo: Correção + melhoria (front)
Prioridade: alta
Origem: conversa de 2026-09-28 (redigida pelo Claude a pedido do usuário).

Problemas relatados:
1. **Troca de tela sem feedback**: às vezes demora (às vezes é o refresh token, às vezes "lento por nada") e o usuário não vê nada carregando; às vezes **trava e não muda de tela**.
2. **Estado antigo na tela de PR**: ao sair e voltar para a tela de PR, Descrição e Root Cause aparecem como preenchidos, os pull requests ficam carregados e só o botão Limpar fica liberado — sem nenhum card buscado.
3. **Card na URL**: ao digitar um número de card e buscar, levar o card para a URL, para que ao atualizar a tela (F5) volte para o mesmo card.

Requisitos:
1. Indicador de carregamento global em toda troca de tela (barra no topo) e, se passar de ~1,5 s, um aviso "Carregando…" / "Renovando sessão…". Falha ao abrir uma tela → mensagem, nunca silêncio.
2. O refresh do token nunca pode deixar a navegação presa: com erro, sem resposta ou resposta inválida, termina (e o fluxo atual leva ao login com "Sessão encerrada"); tempo máximo de espera; um único refresh por vez.
3. Entrar na tela de PR sempre começa limpo (sem descrição, root cause, PRs ou registro de outro card).
4. Buscar um card grava `?card=<número>` na URL (sem empilhar histórico a cada busca); Limpar remove; abrir a URL com `?card=` busca o card (já existe para os atalhos da home).

Fora de escopo: backend.
