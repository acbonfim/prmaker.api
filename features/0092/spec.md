# 0092 — Validação em ambiente (item de trabalho com prints)

Pedido (2026-11-08):

Permitir criar um **item de trabalho para validar a demanda em um ambiente específico**.

Ambientes conhecidos para sugerir:

| Ambiente | URL | Admin |
|---|---|---|
| Desenvolvimento | https://devsp.solvacelabs.com/ | admin@ewcm.com.br |
| QA | https://qa.solvacelabs.com/ | admin@ewcm.com.br |
| Teste e performance | https://performancetest.solvacelabs.com/ | |
| Homologação de release | https://sandboxefeso.solvacelabs.com/ | |
| Demonstração (**jamais pode alterar dados**) | https://demo.solvacelabs.com/ | |
| Homologação de release/hotfix | https://sandboxrt.solvacelabs.com/ | |
| Produção de alguns clientes | https://gerdau.solvacelabs.com/, https://mondelez.solvacelabs.com/ | |

- No banco corporativo estão todos os clientes (acesso como `corporate`).
- Normalmente existe um admin por planta: `admin<iniciais da planta>@solvacelabs.com` (ex.: `admincor@solvacelabs.com`
  = admin da planta corporativa da Mondelez).

Como vai funcionar:
- Toda funcionalidade com alteração que precise de validação: orientar/padronizar a criação de um **item de validação**,
  com uma ou várias sub-tarefas, uma por ambiente (ex.: dev, qa, sandboxefeso, produção do cliente do bug ou de um
  cliente específico na US). O usuário pode informar um ambiente específico.
- A validação: o Claude loga no ambiente, grava uma sequência de prints mostrando o funcionamento correto e salva as
  evidências na sub-tarefa de validação daquele ambiente.
- Senhas de cada usuário salvas **na máquina do usuário**, sem banco — como já é feito com os bancos: comando + script
  que grava num arquivo local.
- Nova tool para o especialista criar essa task, e também pelos comandos `/xxx`.
- O Claude sabe achar URL do ambiente, plantas, bancos de cada planta e o admin da planta; sem a senha, orienta o
  usuário a rodar o comando que grava a senha daquele usuário.
- No fim: PR e merge automáticos (o usuário testa depois de publicado).
