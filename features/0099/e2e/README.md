# e2e local da 0099 (harness `.t0099`, fora do git)

- Postgres isolado `cime-pg-0099b1` (porta 55491, banco `cime`, prform+execution+auth migrados).
- **DevOps falso** `fakedevops.py 8199` — REST 7.1 em memória (tipos, campos, tags, áreas, times, membros, board com coluna
  dividida/WIP/lanes, WIQL com `Id IN`/área/estados, workitemsbatch, criar/PATCH com regra de estado, comentários,
  `workitemsorder`); `GET /log` mostra as escritas e o WIQL, `GET /items` o estado.
- API na 5099 com `AzureDevOps__HostOverride=http://127.0.0.1:8199` (`startapi.sh`) — a chave só existe para teste local:
  o handler troca `dev.azure.com` pelo host informado. Plugin "AzureDevOps Configurations" global (não pessoal) com
  `Organization=solvacelabs`, `Project=Solvace Product Improvement`, PAT qualquer.
- Front: `ng build --configuration development --output-path .t0099/dist`, trocar `localhost:5083` → `localhost:5099` nos
  `.js` e servir com `serve.py dist/browser 4399`. Roteiros `desk1..4.mjs` (puppeteer-core + Chrome do sistema).
