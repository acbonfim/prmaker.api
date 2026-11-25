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

## Ajustes 3 (modal do card) — harness `.t0099c`
- `fakedevops.py` agora tem discussion (`GET/POST workItems/{id}/comments`, `order=desc`), anexos do card 90001 (arquivo +
  imagem nos Repro Steps + imagem na discussion), `GET …/_apis/wit/attachments/{guid}` (PNG/texto) e `_apis/identities`
  (GUID para a menção `@`).
- Plugin global criado por `POST PluginConfiguration` (admin) com `Organization/Project/PersonalAccessToken`.
- Roteiro `modal.mjs` (fora do git, em `.t0099c/e2e`): abre `/auth/desk?card=90001`, edita o título → "Alterado: 1 campo"
  (antes × depois) → Salvar pede confirmação → PATCH; Discussion com `@br` + `#9000` → comentário com `data-vss-mention`
  da pessoa (GUID) e do card; Anexos (3, miniaturas, visualizador); Relacionados; Esc fecha.

## Ajustes 4 (coluna no Test in production, Histórico, modal único, galeria, PDF) — harness `.t0099c`
- `fakedevops.py`:
  - `GET workItems/{id}/updates` (revisão 1 + troca de estado/link/comentário no 90001; cada PATCH vira revisão);
  - `WEF_AAA_System.ExtensionMarker` (board do card);
  - anexo `manual.pdf` servido como octet-stream.
- Plugins por `POST PluginConfiguration` (admin):
  - "AzureDevOps Configurations";
  - "AI Configurations" com `BugTestInProduction{Area,State,Column,Comment}`.
- Token de dev: `mktok.py … user` → `user.tok` (sem papel da Mesa: só os endpoints `[CardAccess]`).
- Roteiro `modal2.mjs` (fora do git, em `.t0099c/e2e`):
  - Mesa: Histórico, PDF, ← →;
  - Bancada (dev): "Ver tudo" = modal único.
