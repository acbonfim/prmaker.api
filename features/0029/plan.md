# Feature 0029 — Configurações do fechamento lidas do PRMake

- O backend já expõe a configuração efetiva do usuário em `GET Azure/actions/config` (AI Configurations com os campos pessoais: área/estado/comentário do Test in production, área exigida, estado do Ready for QA, estimativa inicial, prompt do resumo) e as opções de classificação em `GET Azure/actions/classifications` (0028). Nenhum endpoint novo.
- **analisar-bug**: `prmake-plan.sh devops <card> config` imprime as ações configuradas (ou "NAO configurado") e salva o prompt do resumo em `analises/summary-prompt.txt`. A SKILL.md passa a exigir `config` + `classifications` antes de perguntar sobre o fechamento ou gravar; perguntas citam estado/área exatamente como configurados; só oferece ações configuradas e aplicáveis à área atual do card; o resumo segue o prompt configurado.
- **gerar-prmake**: `summary_prompt.txt` vem do `actions/config` (efetivo do usuário), com o plugin global como reserva.
