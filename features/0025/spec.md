Tipo: Melhoria (backend + front + skill)
Prioridade: alta
Origem: conversa de 2026-09-29 (redigida pelo Claude a pedido do usuário), usando a 0024 no card 74519.

Problema:
1. Para o PRMake perceber que um PR foi mesclado, o usuário precisava clicar no ⟳ da lista de PRs; depois disso o plano de correção ainda levava ~1 min para refletir.
2. A sessão do Claude Code que está tratando o card não fica sabendo do que acontece no PRMake (respostas, etapa concluída pelo usuário, chamado resolvido, PR mesclado, Continuar): o usuário precisa escrever no chat para ela continuar.

Requisitos:
1. A tela consulta o status dos PRs abertos por baixo dos panos e só atualiza quando o status muda de verdade — sem cliques.
2. O plano de correção reflete o merge assim que o PRMake souber dele.
3. O PRMake "acorda" a sessão do Claude que está tratando o card quando algo muda, e ela continua sozinha; o que o Claude faz já aparece no PRMake em tempo real.
