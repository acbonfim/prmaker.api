# 0095 — Ver tela ao vivo (navegador do agente na Bancada)

Pedido do usuário (2026-11-12, card 78161 — sub-tarefas de reproduzir no ambiente do cliente):

> Quando o agente estiver testando no ambiente do cliente, ter um botão para ver o que o agente está fazendo — a parte
> visual dele navegando entre as telas, como um navegador embutido.
>
> Quero que fique disponível ao lado do título "O que está sendo feito", um botão com ícone de navegador, com o tooltip
> "Ver tela ao vivo". Essa tela só vai aparecer quando isso estiver acontecendo, e só vai exibir na máquina de quem está
> na máquina do executor. Não quero gravar nada em banco ou algo do tipo, quero apenas que quem está executando consiga
> ver; abrir com janela faz sentido, desde que seja embutido e não saia do PRMake.
>
> No final, abre o PR e faz o merge automaticamente.

## Decisões

- Janela nativa (`--headed`) não pode ser embutida numa página web. Em vez disso o navegador continua sem janela e a
  **tela dele é transmitida da própria máquina** para o modal da Bancada: Chrome → executor local (127.0.0.1) → `<img>`
  MJPEG. Não passa pela API, pelo relay nem pelo banco.
- Só aparece para quem é **dono da execução** (`mine` no `GET WorkItems/{n}/live`) e **na máquina do executor**
  (a Bancada lê `http://127.0.0.1:47615`; em outra máquina não há o que ler).
- O Chrome pede uma vez a permissão "Acessar apps e serviços neste dispositivo" (Local Network Access).
