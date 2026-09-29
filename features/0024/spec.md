Tipo: Feature (backend + front + skills)
Prioridade: alta
Origem: conversa de 2026-09-28 (redigida pelo Claude a pedido do usuário), depois da 0023 em produção.

Contexto:
Na 0023 a skill `analisar-bug` passou a registrar um plano de execução no PRMake, mas o plano é fixo (7 etapas de análise) e termina ao publicar a análise na Timeline. Falta o que vem depois: propor soluções, decidir com o usuário e executar a correção (código, PRs, chamados), com a Timeline contando a história completa.

Requisitos:
1. **Plano dinâmico em duas fases.** O plano inicial é só o de **análise**. A partir do que foi analisado, é montado o **plano de correção** — não fixo — com ações de código, abertura de PRs e abertura de chamados. Cada passo é executado **pelo Claude ou pelo usuário** (fica claro na tela quem executa).
2. **Das soluções à correção.** Depois de publicar a análise, a skill propõe soluções e **faz perguntas ao usuário**, que pode responder **no PRMake ou no Claude**. Com as respostas, gera o plano de correção, na mesma estrutura do plano de análise. Os dois planos ficam consultáveis.
3. **Links e espera por chamados.** O usuário pode anexar links (ex.: chamado no **Freshservice**). Um passo que depende de um chamado (ex.: chamado de script) fica **aguardando** até o chamado ser concluído; o usuário ou o Claude podem **avançar em outros passos** que não dependam dele. Se possível, consultar o status do chamado pelo link.
4. **O plano de análise continua visível** depois que o de correção existe.
5. **PRs por repositório.** Quando a correção envolve código, há passos de abertura de PR; com mais de um PR (em um ou vários repositórios), **cada repositório é um passo** do plano de correção (com os seus PRs dentro).
6. **Conclusão** do plano de correção = **todos os PRs do plano mesclados**.
7. **Fluxo de branches** (branch sempre `hotfix/<card>`; `AB#<card>` vai no título do PR e na mensagem do commit):
   - **Legado (`edv-solvace`), card de produção** (área `Solvace Product Improvement\Product Development Team`): parte da `master`, cria `hotfix/<card>` e corrige nela; atualiza a `development`, cria `hotfix/<card>-dev` a partir dela, faz *cherry-pick* da correção e abre PR para `development`; o mesmo para a `qa` (`hotfix/<card>-qa` → PR para `qa`). **Sempre 2 PRs.**
   - **Bug de release/regressão** (área `Solvace Product Improvement\Release Management`): parte da `release-version` ou `hotfix-version`, cria `hotfix/<card>`, corrige e abre PR direto para essa branch. **A branch muda às vezes: sempre perguntar ao usuário.**
   - **Revamp**: os mesmos passos; o **frontend revamp (`edv-solvace-apps`)** tem `development`, `qa` e `edge` (`edge` para regressão/release — também pode mudar, perguntar).
   - **O Claude só abre PRs, nunca faz merge.**
8. **A Timeline conta tudo**: o que foi analisado, o que foi feito e o que falta — hoje ela para na análise inicial.
9. **Baixar a skill pelo PRMake**, sempre atualizada, de forma automática (sem publicar como plugin — o usuário não tem essa permissão).

Fora de escopo: executar o merge dos PRs; abrir o chamado no Freshservice automaticamente (o usuário abre e cola o link; a skill pode preparar o texto e o script do chamado).
