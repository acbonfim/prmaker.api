Tipo: Feature (backend + front + skill)
Prioridade: alta
Origem: conversa de 2026-09-28 (redigida pelo Claude a pedido do usuário).

Contexto:
A skill `analisar-bug` (Claude Code, `~/.claude/skills/analisar-bug`) faz a triagem técnica de um bug e hoje guarda tudo o que produz (scripts, análises, evidências, plano de execução) só na máquina de quem roda, em `~/.claude/cards/<card>/`, e publica apenas a análise final na Timeline do card. Quem acompanha o card no PRMake não vê o que está sendo feito nem acessa os arquivos.

Objetivo:
A skill trabalha em conjunto com o PRMake: enquanto analisa e corrige, manda atualizações em tempo real para o card.

Requisitos:
1. Ao analisar, a skill monta um **plano de execução** (etapas) e o registra no PRMake logo no início.
2. A tela do card mostra esse plano numa **seção nova entre "Pull Requests" e "Linha do tempo"**:
   1. Atualizada em **tempo real**, **animada** e intuitiva: qual etapa já foi (concluída), qual está em andamento e quais estão pendentes.
   2. Etapa **cancelada** (por decisão do usuário ou outro motivo) fica evidente — meio apagada, com **tooltip** do motivo.
   3. Cada etapa abre os **detalhes** ao passar o mouse ou clicar: o que vai ser feito e o que está sendo feito agora pela skill (atualizações em pequenos pedaços, ao vivo).
   4. **Status no cabeçalho**, animado: em andamento, pendente, concluído, pausado ou cancelado.
   5. **Tela cheia** (a mesma experiência da linha do tempo).
   6. **Rodapé** (mesma altura do rodapé da linha do tempo) com botões para acessar **scripts, análises, anexos** e tudo mais que a skill salva: cada arquivo pode ser **copiado** ou **baixado**. Scripts SQL legíveis e baixáveis como `.sql`; **imagens** visualizadas dentro da ferramenta; análises (markdown) renderizadas.
3. Tudo fica **salvo em banco** (avaliar PostgreSQL ou outro).
4. É possível **parar (cancelar), pausar e continuar** de onde parou, em qualquer etapa.
5. A skill já começa criando o registro e salva tudo o que estiver fazendo, repassando em pequenos pedaços.
6. **Nunca se perder**: se a conexão cair (skill ou navegador) ou o usuário tiver algum problema, sempre é possível saber onde parou e continuar dali.

Fora de escopo: executar a skill a partir do PRMake (ela continua rodando no Claude Code de quem analisa).
