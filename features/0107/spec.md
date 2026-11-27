# 0107 — Escolhas clicáveis na conversa do especialista

Pedido do usuário (2026-11-27):

> quando estou conversando com o especialista, as vezes ele me da opcoes e sugestoes. Eu quero que quando for opcoes,
> seja mais dinamico, eu possa responder diretamente a ele sem ficar digitando no site, e sim clicando em um checklist ou
> componentes dinamicos de escolha unica, ou ate picklist.
>
> Tambem gostaria que ele oferecesse botoes pra escolha, por ex: no 79047 eu estava pedindo um ajuste, ele deveria me dar
> uma sugestao e perguntar se pode seguir com essa solucao e ter um botao com texto dinamico "Criar item e executar
> agora", "Cancelar", etc...

## O que muda

1. **Escolha na mensagem do especialista.** `prmake_case_reply` (e `POST CaseChat/{card}/reply`) aceita `choice`:
   - `mode`: `buttons` (cada botão responde na hora — "Criar item e executar agora", "Só criar o item", "Cancelar"),
     `single` (escolha única, rádio), `multi` (checklist), `select` (lista suspensa/picklist, para muitas opções);
   - `question` (opcional, curta), `options[{id?, label, detail?, style?: primary|danger|neutral, recommended?}]`,
     `allowOther` (+ `otherLabel`) para a pessoa escrever outra resposta.
2. **Responder clicando.** A tela mostra o componente dentro da bolha. O clique (ou "Enviar" no rádio/checklist/lista)
   chama `POST CaseChat/{card}/messages/{id}/choice` → grava a resposta na escolha (quem, quando, o que), cria a mensagem
   da pessoa ("**Criar item e executar agora**", em resposta à mensagem da escolha) e **sempre** chama o especialista
   (mesmo com várias pessoas na conversa).
3. **Uma resposta por escolha.** Respondida, fica travada mostrando o que foi escolhido e por quem. Escolha nova do
   especialista fecha as abertas anteriores ("substituída"); a pessoa também pode ignorar e digitar.
4. **O especialista usa** (skills especialista-do-caso e especialista-de-gestao): sempre que oferecer opções ou pedir
   para seguir com uma solução, manda a escolha em vez de "responda 1 ou 2"; botões com o texto do que vai acontecer;
   encerra a vez e, quando chega "escolheu ...", executa a opção (o clique é a autorização) — "Cancelar" não faz nada.
