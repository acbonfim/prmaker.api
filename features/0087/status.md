# Status — Feature 0087

| Fase | Descrição | Status | Notas |
|---|---|---|---|
| B1 | `GET WorkItems/card/{card}/files` (itens + planos ligados + planos sem item) | concluída | `WorkItemFileListing.BuildForCard`; 130 testes executionplans |
| B2 | Trava de itens/plano de correção ricos (`CodeCitationGate.CheckWorkAsync`) | concluída | REST + MCP; 365 testes prform |
| S1 | Skills: bloco "Itens de trabalho ricos (0087)" | concluída | analisar-bug (correcao.md), analisar-us, especialista-do-caso |
| F1 | Execução contínua (silent, skeleton atrasado, estado vivo) | concluída | build dev ok; **não validado visualmente** com executor real |
| F2 | Aba Arquivos + filtro por item + atalho na Execução + faixa "só acompanha" | concluída | `?focus=files&item=n` |
| F3 | Aviso de desligamento + tour `pr-deprecation` | concluída | tour antigo da tela de PR só pelo botão de ajuda |
| F4 | Itens ricos no front (popover de código nos modais, "O que será feito", arquivos na visão geral) | concluída | |

## Pendências / decisões
- **Modelo da criação dos itens**: o plano de correção (que cria os itens) roda no Sonnet. Para criar os itens no Opus e executar no Sonnet é preciso decidir como (ex.: Opus até `prmake_correction` e retomar em Sonnet, ou chave `ExecutorItemsModel`). Não alterado.
- Validar numa análise real: fotografias exigidas ao criar o plano de correção (pode recusar na primeira tentativa — a skill corrige e repete).
- Arquivos da conversa do especialista (`CaseFile`) não entram na aba Arquivos.
