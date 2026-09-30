# Status — Feature 0036

| Fase | Descrição | Status |
|---|---|---|
| F1 | Resposta otimista na tela | ✅ concluída |
| B1 | Relay e Timeline em paralelo no answer | ✅ concluída |
| Q1 | Teste local | ✅ concluído |

## Teste local (Postgres isolado, POST da resposta atrasado 3 s no navegador)
- +120 ms após o clique: opções somem, aparece "Corrigir no backend — … — enviando…" (no aviso do topo e na etapa).
- Confirmação: resposta gravada ("Admin Teste, pelo PRMake, …"), cabeçalho atualizado; as outras perguntas seguem abertas.
- Erro simulado (500): as opções voltam e aparece o aviso "escolha de novo".
