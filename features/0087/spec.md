# 0087 — Bancada: execução contínua, arquivos do card, aviso de desligamento da tela de PR e itens ricos

## Pedido (do usuário, 2026-11-05)
1. **Execução sem pisca-pisca.** Ao clicar em "Analisar com Claude" a tela piscava (skeleton, some, volta). Carregamentos e animações contínuos e amigáveis.
2. **Aviso de depreciação na tela de Pull Request** (será desligada; tudo passa para a **Bancada**), com **tour** — prioridade ao abrir a tela.
3. **Validar os modelos**: Opus para analisar e criar os itens de trabalho; Sonnet para executar.
4. **Arquivos do card fora da execução**: todas as análises, chamados, dados etc. visíveis fora de um plano, com **filtro por item**; cada item mostra só os seus, e em algum lugar **todos**.
5. **Deixar claro que a Execução só exibe** o que se faz num item (perguntas/respostas, ao vivo).
6. **Itens ricos**: itens e sub-tarefas de código com o código, a linha e a fotografia com hiperlink (popover), regras da engenharia reversa/Base Solvace com hiperlink; o usuário entende cada passo/alteração; arquivos e imagens também no item.

## Como ficou
- **Front** `components/execution-plan`: leitura silenciosa sempre que a tela já leu o plano (antes só com plano em tela → card sem plano voltava ao skeleton a cada evento); skeleton só após 220 ms; troca "sem plano → plano" sem recarga; estado vivo "Na fila / O Claude está trabalhando" (atividade ao vivo + últimas) em vez de "Nenhum plano".
- **Bancada**: aba **Arquivos** (`app-card-files`, `CardFilesStore`, `GET WorkItems/card/{card}/files`) com chips por item (+ "Sem item" para versões antigas), `?focus=files&item=n`, atalho "Arquivos do item" na Execução; faixa explicando que a Execução só acompanha; tour v7.
- **Tela de PR**: aviso fixo + botão "Abrir na Bancada" + tour `pr-deprecation` (abre sozinho, antes de qualquer outro; o tour antigo da tela não abre mais sozinho).
- **Itens ricos**: modais de item/sub-tarefa agora abrem o popover de código/regras (`appCodeRefHost`), "O que será feito" por sub-tarefa, arquivos do item na visão geral; **trava no servidor** (`CodeCitationGate.CheckWorkAsync`) para itens criados por skill/especialista e plano de correção: citação sem fotografia e etapa/sub-tarefa `code` sem citação são recusadas; skills (analisar-bug/us, especialista) com o bloco "Itens de trabalho ricos (0087)".

## Modelos (validação)
`ExecutionQueueController.ModelForAsync`: fase **analysis** → `ExecutorAnalysisModel` (semeado `opus`); fase **correction** e **item de trabalho** → `ExecutorCorrectionModel` (semeado `sonnet`); especialista rápido `sonnet`, a fundo `opus`. O executor passa `--model`. **Atenção**: os itens de trabalho do Bug/US nascem no plano de **correção** (`prmake_correction`), que roda no modelo da correção (Sonnet) — ver status.md.
