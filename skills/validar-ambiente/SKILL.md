---
name: validar-ambiente
description: Valida a demanda de um card num ambiente Solvace (feature 0092) — entra no ambiente pelo navegador da maquina (Chrome sem janela, com a senha guardada SO nesta maquina), segue o roteiro do que mudou e grava a sequencia de prints mostrando o funcionamento como evidencia na sub-tarefa `env-validation` do item de validacao no PRMake. Sabe achar a URL do ambiente (configuracao ValidationEnvironments e banco corporativo), as plantas e os bancos de cada planta (TB_WCM_SITE) e o admin da planta (admin<sigla>@solvacelabs.com); sem a senha, orienta a pessoa a grava-la no proprio terminal. DEMO e producao sao somente leitura (o navegador bloqueia gravacoes). Use quando a sub-tarefa for `env-validation`, quando o prompt pedir para "validar em <ambiente>", "validar em dev/qa/sandboxefeso/sandboxrt/producao", "tirar prints da validacao", "gravar as evidencias da validacao", ou para criar o item de validacao de um card.
---

# validar-ambiente

Valida **uma demanda num ambiente** e deixa a prova: uma sequencia curta de prints, na ordem, mostrando que funciona
(ou o que falhou), gravada como **evidencia da sub-tarefa** do item de validacao. Instalada e atualizada pelo PRMake
(fonte: `skills/validar-ambiente`). Token: `PRMAKE_TOKEN` ou `~/.claude/prmake-token.txt`.

```bash
ENV=~/.claude/skills/validar-ambiente/scripts/env.sh   # envs · resolve · plants · admin · user · has-password · browser · evidence
AB=~/.claude/skills/analisar-bug/scripts               # sql-query.sh (so leitura), prmake-plan.sh
```
Rode sempre com o caminho literal (`bash ~/.claude/skills/validar-ambiente/scripts/env.sh ...`), um comando por vez —
e assim que a permissao do Claude Code casa (`prmake-skills.sh permissions`).

## Regras que nao mudam
1. **Senha nunca passa pelo chat.** Ela fica so em `~/.claude/solvace-env-credentials.json`, gravada pela **pessoa, no
   terminal dela**: `bash ~/.claude/skills/.prmake/prmake-skills.sh env-credentials set <ambiente> <usuario>`. Voce nao
   le esse arquivo, nao roda esse comando (ele recusa sem terminal) e nunca pede a senha. Senha colada na conversa:
   nao use, nao repita, diga que ficou exposta e que o certo e trocar e gravar pelo comando.
2. **DEMO jamais tem dado alterado** e producao de cliente e **somente leitura**: so navegar, abrir e consultar. A
   sessao desses ambientes bloqueia gravacoes na rede (aparece `BLOQUEADO (ambiente somente leitura)`); nunca tente
   contornar. Validacao que exige criar/alterar dado nesses ambientes → `wait` explicando o que a pessoa precisa
   autorizar/fazer ou valide num ambiente de teste (dev/qa/sandbox).
3. **Valide o que esta no ambiente.** Antes, confira que a mudanca chegou la (PR do destino do ambiente mesclado e
   publicado: dev ← `development`/`-dev`, qa ← `qa`/`-qa`, sandboxefeso/sandboxrt ← `release-version`/`hotfix-version`,
   producao ← `master`; `prmake_case_context`/`prmake_card`/`prmake_work_items`). Nao chegou → `wait` dizendo qual
   PR/deploy falta e **encerre a vez** (o executor retoma quando a pessoa agir).
4. Banco **somente leitura** (`sql-query.sh` so SELECT). Nada de comentario em codigo, commit ou PR aqui.
5. **Prints sem dado sensivel alem do necessario**: nada de tela de senha, token, dados pessoais que nao provem a
   validacao. Prefira a tela com o resultado (mensagem de sucesso, registro na lista, valor calculado).

## Onde esta cada coisa
- **Ambientes conhecidos**: `bash $ENV envs` (configuracao `ValidationEnvironments` do PRMake: chave, URL, usuario padrao,
  somente leitura, banco). `bash $ENV resolve <chave|url|cliente>` resolve qualquer um; fora da configuracao, procura
  no **banco corporativo** (`corporate` / `CORPORATE_SOLVACE_DB.TB_WCM_ENVIRONMENT`: nome, URL, `CATALOG_GLOBAL`) —
  ambiguo (ex.: "novelis" = sandbox e producao) lista as URLs: use a URL.
- **Plantas e bancos**: `bash $ENV plants <ambiente> [filtro]` (`TB_WCM_SITE` do banco global: `SITE_ACRONYM`,
  `SITE_NAME`, `INSTANCE_NAME` = banco local da planta). Host do banco: o da configuracao ou o servidor das credenciais
  desta maquina que tem o banco global (regra de hosts por cliente da `analisar-bug`).
- **Usuario**: dev/qa usam o admin do ambiente (`admin@ewcm.com.br`); nos outros, o **admin da planta** —
  `admin<sigla minuscula>@solvacelabs.com` (ex.: `admincor@solvacelabs.com` = planta corporativa da Mondelez):
  `bash $ENV admin <ambiente> <sigla>` confere se existe/ativo e se ha senha nesta maquina. A sub-tarefa pode trazer
  `params.user`/`params.plant` — use-os. Bug: a planta do cliente que reportou (repro steps/discussion); US: a planta
  que a pessoa indicar (ou pergunte).

## Fluxo da sub-tarefa `env-validation`
Params: `environment` (chave ou URL), `plant?`, `user?`, `scenario?` (o que validar), `expected?`.
1. `prmake_work_task(card, taskNumber, start)`.
2. **Roteiro**: `scenario`/`expected` da sub-tarefa; vazios → o que o card pede (`$CARD_DIR/dados/card-resumo.txt`,
   criterios de aceite da US, o "Como validar" da analise/plano). Escreva 3–8 passos curtos (onde ir, o que fazer, o
   que deve aparecer). Regra 3: a mudanca esta no ambiente?
3. **Ambiente e usuario**: `bash $ENV resolve <environment>`; usuario = `params.user` → admin da planta
   (`bash $ENV admin <amb> <sigla>`) → usuario padrao do ambiente.
4. **Abrir**: `bash $ENV browser start <amb> --user <email> [--plant <sigla>] --card <card> --task <taskNumber>`.
   - exit 4 (**sem senha**) → `prmake_work_task(wait)` com: "Falta a senha de `<email>` em `<amb>` nesta maquina. No
     seu terminal (fora do chat): `bash ~/.claude/skills/.prmake/prmake-skills.sh env-credentials set <amb> <email>`
     e depois clique em *Ja resolvi*." — e **encerre a vez**.
   - exit 3 (**login recusado**) → `wait` igual, pedindo para regravar a senha (expirada/errada) ou outro usuario.
   - exit 2 (sem Chrome/Playwright) → `wait` pedindo instalar o Google Chrome (ou `PRMAKE_CHROME=<caminho>`).
   - Abriu: anota a sessao (`<amb>-<email>` em minusculas, separado por `-`) e a pasta dos prints.
5. **Executar o roteiro**, passo a passo, olhando a tela:
   - `bash $ENV browser snap <sessao>` → titulos, elementos clicaveis com o alvo sugerido e o texto; o arquivo da
     tela (`_snap.jpg`) voce abre com `Read` quando precisar ver.
   - `go <sessao> <url|/caminho>` · `click <sessao> "<alvo>"` · `fill <sessao> "<alvo>" "<valor>"` ·
     `select <sessao> "<alvo>" "<opcao>"` · `press <sessao> Enter` · `wait <sessao> "<alvo>|ms"` · `text <sessao>`.
     Alvo = o do `snap` (`#id`, `[formcontrolname=...]`, `text=Salvar`, `role=button[name="Salvar"]`) ou o texto visivel.
   - **Print** em cada marco do roteiro: `bash $ENV browser shot <sessao> "<nome-curto>" --caption "<o que mostra>"`
     (`--full` = pagina inteira). Numera sozinho (`01-...jpg`, `02-...`). 4–10 prints: tela inicial do fluxo → acao →
     resultado. Abra (`Read`) os que provam o resultado antes de concluir.
   - Algo nao funciona: tire o print do erro, confira se nao e o roteiro (outro caminho na tela) e so entao conclua
     como **falha de validacao**.
6. **Fechar**: `bash $ENV browser stop <sessao>`.
7. **Evidencias**: escreva `validacao-<amb>.md` na pasta dos prints (ambiente, URL, usuario, planta, data, roteiro e,
   por print, o numero + a legenda + o que ele prova; veredito **aprovado / reprovado** com o motivo) e suba tudo:
   `bash $ENV evidence <card> <item> <taskNumber> <pasta-dos-prints> --report <pasta>/validacao-<amb>.md`.
8. **Concluir**:
   - Aprovado → `prmake_work_task(complete, result: "<veredito em 2–5 linhas: o que foi validado, onde, como quem, prints
     #1–#n>", output: {environment, url, user, plant?, shots: "<n>", verdict: "ok"}, undo: {action: "none", params:
     {reason: "so leitura/validacao"}})`.
   - Reprovado → `prmake_work_task(fail, result: "<o que falhou, em qual passo, print #n>")` — os prints ja estao na
     sub-tarefa; nao siga para os outros ambientes do item sem a pessoa decidir.

## Criar o item de validacao (padrao do PRMake)
Toda entrega que **altera** algo e precisa ser validada ganha um **item de validacao** (tipo `validation`), com **uma
sub-tarefa por ambiente** — ex.: dev, qa, sandboxefeso (homologacao de release) ou sandboxrt (release/hotfix) e a
producao do cliente do bug (Bug) ou de um cliente especifico (US). A pessoa tambem pode pedir um ambiente especifico.
- MCP: `prmake_env_validation(card, environments: "dev, qa, mondelez", plant?, user?, scenario?, expected?, execute?)`
  — mesma checagem de duplicidade dos itens (`prmake_work_items` antes; ja existe → use o existente). Sem
  `environments` = os sugeridos para o tipo do card (`defaults` da configuracao).
- Ferramenta: `prmake_tool_run(card, "validar-em-ambiente", {"environments": "...", "plant": "COR", "scenario": "..."})`.
- Na tela: Ferramentas → **Validar em ambiente**, ou `/validar-em-ambiente` na conversa do Especialista.
O item nasce pendente; roda quando a pessoa (ou o especialista, a pedido) clica em **Executar**. Cada sub-tarefa roda
este fluxo; prints e relatorio ficam na sub-tarefa (galeria "Evidencias").

## Exemplo — card 77434, "validar em QA e na producao da Mondelez (planta COR)"
1. `prmake_env_validation(card: "77434", environments: "qa, mondelez", plant: "COR", scenario: "Abrir o 5W2H do plano 12 e
   salvar com anexo; o anexo aparece na lista")` → item #120 com #121 (QA) e #122 (Producao Mondelez).
2. #121: `resolve qa` → `browser start qa --card 77434 --task 121` (admin@ewcm.com.br) → roteiro com 6 prints →
   `evidence 77434 120 121 ~/.prmake/cards/77434/validacao/qa-121 --report .../validacao-qa.md` → `complete`.
3. #122: `admin mondelez COR` → `admincor@solvacelabs.com`, sem senha → `wait` com o comando
   `env-credentials set mondelez admincor@solvacelabs.com`; a pessoa grava e clica *Ja resolvi* → `browser start
   mondelez --plant COR ...` (somente leitura: so consulta o plano 12 e o anexo) → prints → `complete`.
