# Feature 0042 — Custo por ação de IA

## Contexto
As ações de IA do PRMake (Gerar com IA do PR, passagem de conhecimento, resumo não técnico, Pergunte, Analisar a fundo,
Gerar guia, Aprender com um card, sugestão de seção) rodam no servidor com a **chave de API do perfil** de quem usa
(plugin pessoal "Claude Plugin"/"Gemini Plugin"), não com a assinatura do Claude Code. O usuário viu US$ 3,50 sumirem
em poucas ações e não tinha como saber quanto cada clique custou. Parte do gasto veio de chamadas em lote da skill
`base-solvace` (`arch.sh guia`/`learn`) feitas com o token dele.

## Objetivo
1. Registrar cada chamada de IA: quem, qual ação, provedor/modelo, tokens de entrada/saída, custo estimado, duração e
   sucesso.
2. Mostrar o custo **logo depois da ação** e numa tela "Meu consumo de IA" (admin vê de todos, por usuário).
3. Skill: `arch.sh guia`/`learn` avisam que consomem créditos de API e mostram o custo; a documentação manda gerar
   conteúdo em massa no Claude Code e publicar com `section`.

## Regras
- Preços configuráveis no PRMake (nada fixo no código): "AI Configurations" → `AiModelPricesUsdPerMillion`
  (`{"claude-haiku-4-5": {"input": 1, "output": 5}, ...}`), semeado por migração; o modelo casa pelo prefixo mais
  longo; modelo fora da tabela grava só os tokens (custo "—").
- O preço vem da configuração **global** do plugin, mesmo que ele seja pessoal.
- Registro nunca derruba a chamada de IA (falha só gera log).
- Ação: cabeçalho `X-AI-Action` enviado pelo front (ex.: `pr:generate`) ou a rota conhecida (`base-solvace:ask`...).
- Resposta com `X-AI-Usage: calls=N;in=..;out=..;cost=..;model=..` (exposto no CORS).
