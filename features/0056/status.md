# Status — Feature 0056

Branch `feature/0056` em `prform.api-0056` (API, skills) e `prform-app-0056` (front). **Implementada em 2026-10-04.**
Em aberto: host/credencial do Multilingual da DEMO (S4 fica configurável e falha com aviso claro até lá — ver status).

| Fase | Status | Responsável | Commits |
|---|---|---|---|
| S1 | ✅ concluída | Claude | 011b604 |
| S2 | ✅ concluída | Claude | 011b604 |
| S3 | ✅ concluída | Claude | ad89220 |
| S4 | ✅ concluída (host/credencial reais pendentes) | Claude | ad89220 |
| F1 | ✅ concluída | Claude | front 249386e |
| F2 | ✅ concluída | Claude | front 249386e |
| T1 | ✅ local | Claude | 0d72685 (2 bugs achados e corrigidos) |

## Decisões
- Sigla curta (2 caracteres com dígito ou maiúscula) conta como termo em toda busca (skill, `kb.sh index`, servidor),
  sempre como palavra inteira (nunca substring) — "a3" não casa dentro de "sa3_registro" nem de um GUID.
- Termo listado numa lacuna conta como coberto quando o `GAP` fala de "termos"/"glossário" no título (bloco inteiro)
  ou tem uma linha `**Termos:**` — aparece na revisão como "Fora do glossário (em lacuna)", não escondido.
- `--path` com pasta SUBSTITUI a do repositório (como antes); com arquivo, glob ou outra pasta do mesmo repo SOMA —
  cobre os serviços .NET do `helpers/` que ficam junto com os de outros módulos.
- Traduções do glossário vêm do **Multilingual do revamp** (Aurora PostgreSQL, schema `multilingual`), não mais do
  `TB_WCM_LANGUAGE` (legado): só os termos do módulo, em lotes; credencial só local
  (`~/.claude/multilingual-credentials.json`, aceita o JSON do secret como vem da AWS); sem credencial/VPN, o
  glossário segue com os rótulos do código e a etapa explica o motivo.
- Tabela markdown: célula só quebra entre palavras (nunca "anywhere" — reduzia a largura mínima da coluna a 1
  caractere); código/identificador inline nunca quebra (`white-space: nowrap`) — sem couber, a tabela rola na
  horizontal. (Uma primeira tentativa com pontos de quebra via `<wbr>` em `/ . _ =` foi testada isoladamente antes
  de aplicar e descartada: ainda forçava quebra no meio de identificadores longos.)
- Armadilhas na Base Solvace são só leitura (conferir/remover continuam na Engenharia reversa); aba só aparece
  quando o módulo tem armadilhas.

## T1 (local)
Harness com Postgres isolado (porta 55457), API 5083, front 4200, skill `re.sh` contra a API real e um Multilingual
PostgreSQL falso (porta 55456, schema `multilingual` com `term_key`/`translation`/`language`).
- **S1**: "A3" (GLO-001, sinônimos SA3/RCA/5S) encontrado pela busca do servidor (`Architecture/search?q=A3`) e pelo
  índice da engenharia reversa (`index/search?q=A3`), casando só como palavra inteira.
- **S2**: termos reais extraídos do inventário do SA3 ("Kaizen", "Módulo") casados contra um `GAP-001` de texto livre
  e contados como cobertos, aparecendo em `outsideGlossary` com o ID da lacuna.
- **S3**: `re.sh fontes`/`inventario` com a pasta `solvace-asp/systems/sa3` + um arquivo
  (`helpers/Services/Sa3Service.cs`) + um glob (`helpers/**/FishBone*.cs`) — os três somaram (30 arquivos no
  inventário, confirmado que a pasta não foi substituída).
- **S4**: `re.sh traducoes` rodou de ponta a ponta contra o `/settings` real e o Multilingual falso — 4 termos
  reais do SA3 traduzidos (pt/en/es), mesclados no `inventario-termos.json` pelo `re.sh termos`.
- **F1**: aba "Armadilhas" no Técnico mostrando o item linkado e o card; grupo "Histórico (1)" recolhido no modo
  Simples depois de publicar a visão prática (supersede do `guia-inicio`) — prints conferidos.
- **F2**: painel de termos e grupo "Glossário" do sumário recolhidos por padrão (confirmado via DOM); 14 botões de
  copiar no "Como gerar"; tabela de rastreabilidade (2 colunas de código, igual ao exemplo do usuário) sem nenhuma
  célula espremida (alturas uniformes, 58px — 2 linhas, não dezenas).
- **Achados e corrigidos durante o teste** (fora do escopo original, mas bloqueavam a própria verificação):
  - `ReverseTrap`/`ReverseRevisionMode.Normalize`: `origin`/`mode` nulo (campo opcional de uma requisição real)
    derrubava com `NullReferenceException` — o `!` não troca `null` pelo default em tempo de execução. 2 testes
    novos, confirmados falhando no código anterior e passando com a correção.
  - `re.sh check`: `"${EXTRA[@]}"` com array vazio sob `set -u` trava no `/bin/bash` 3.2 do macOS (sem bash
    atualizado) — mesmo idioma já usado em `overrides[@]` no mesmo arquivo.

## Log
- 2026-10-04 — demanda escrita a pedido do usuário: 2 achados do Claude no T1 da 0054 (Guia antigo na árvore do
  Simples; armadilhas novas fora da página do módulo) + 4 problemas relatados pela sessão do SA3 (termo curto,
  termo em lacuna, traduções, `--path` só pasta). Itens 2.1, 2.2 e 2.4 confirmados no código; 2.3 precisa de
  diagnóstico na DEMO (o legado ainda usa `TB_WCM_LANGUAGE` no global). Worktree `prform.api-0056`
  (branch `feature/0056` a partir de `origin/master`).
- 2026-10-04 — respostas do usuário: árvore do Simples com "Histórico" recolhido; traduções vêm do Multilingual do
  revamp (PostgreSQL separado), não do `TB_WCM_LANGUAGE` — 2.3 reescrita no modelo de acesso do Knowledge Center.
- 2026-10-04 — acrescentadas correções de tela pedidas pelo usuário (seção 3): glossário recolhido por padrão, botão de
  copiar em todos os comandos, tabelas em markdown espremidas (causa: `overflow-wrap: anywhere` no container).
- 2026-10-04 — executada a pedido ("pode começar"): S1–S4, F1, F2 e T1 local; 2 bugs achados e corrigidos durante o
  teste (origin/mode nulo, bash 3.2 do macOS). 102 + 4 testes .NET, 15 testes de skill, tudo verde.
