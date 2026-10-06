# Status — Feature 0068

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| B1 | ⏳ | Claude | — |
| E1 | ⏳ | Claude | — |
| T1 | ⏳ | Claude | — |
| T2 | ⏳ depois do deploy | — | — |

## Notas
- O relay não muda (o hub já aceita `AddToGroup` de qualquer conexão com token da API). O evento não leva dado
  nenhum além do sinal — quem entrar no grupo de outro usuário só fica sabendo que "há pedido".
- O executor 1.0.8 (sem autoatualização) para de pegar pedidos com o 426: reiniciar o executor nessa máquina faz
  ele se atualizar no primeiro sinal de vida.
