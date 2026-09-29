---
name: prmake-timeline
description: Registra uma entrada na Timeline de um card no PRMake (POST /api/v1/Timeline), autenticando com a api-key do usuario configurada. Use para anotar contexto/progresso de uma analise, teste ou correcao no card. IMPORTANTE - use proativamente - sempre que comecar uma analise/investigacao/teste sobre um card (branch hotfix/<card> ou bugfix/<card>), registre uma entrada curta na timeline descrevendo o que esta sendo analisado/feito, e va adicionando novas entradas conforme avanca.
---

# prmake-timeline

> **Atualização:** instalada e atualizada pelo PRMake (tela *Skills*). Antes de usar, rode
> `bash ~/.claude/skills/.prmake/prmake-skills.sh update --quiet prmake-timeline 2>/dev/null || true` — se imprimir
> "Skills do PRMake atualizadas", releia esta SKILL.md. A fonte fica no repositório do PRMake (`skills/prmake-timeline`).

Escreve uma entrada na **Timeline** de um card no PRMake
(`POST https://api.softhouse.app.br/api/v1/Timeline`), autenticando via header `x-api-key` com a
api-key do usuario. Serve para ir registrando o **contexto do que esta sendo analisado, testado ou
feito** em um card, criando um historico do trabalho.

> **Esta skill vive no nivel do usuario** (`~/.claude/skills/prmake-timeline`), portanto esta
> disponivel em qualquer projeto/repositorio da maquina. Reusa o mesmo token do `gerar-prmake`.

## Configuracao e token

- **API:** `POST https://api.softhouse.app.br/api/v1/Timeline` — header `x-api-key: <TOKEN>`.
  (Sobrescreva a base por env `PRMAKE_TIMELINE_BASE` se necessario.)
- **Body:** `{"cardNumber":"<card>","description":"<texto>"}`. O `userName` e irrelevante — o backend
  resolve o usuario a partir da propria api-key.
- **Token:** o mesmo do `gerar-prmake`, resolvido nesta ordem: env `PRMAKE_TOKEN`, senao
  `~/.claude/prmake-token.txt`, senao `.claude/prmake-token.txt` do projeto. O script ja faz essa
  resolucao. Em caso de HTTP 401/403, o token expirou.

## Uso proativo (importante)

**Sempre que voce comecar uma analise, investigacao, teste ou correcao ligada a um card**, registre
na timeline o que esta sendo feito — e va adicionando novas entradas conforme o trabalho avanca
(ex.: hipotese levantada, causa encontrada, teste executado, correcao aplicada). Isso vai
enriquecendo o card com o contexto do que foi analisado/testado/feito.

- Descubra o card pela branch atual: `hotfix/<card>` ou `bugfix/<card>` (o script deriva sozinho).
- Se nao houver branch nesse padrao, peca/receba o numero do card antes de registrar.
- Escreva mensagens curtas e objetivas, em linguagem clara. Uma entrada por marco relevante.

## Como registrar

```bash
# card explicito + descricao
bash ~/.claude/skills/prmake-timeline/scripts/prmake-timeline.sh 72517 "Iniciando analise do erro ao salvar o formulario; investigando a validacao do campo X."

# card derivado da branch atual (hotfix/<card> ou bugfix/<card>)
bash ~/.claude/skills/prmake-timeline/scripts/prmake-timeline.sh "" "Reproduzido o bug localmente; parece vir do handler de submit."

# descricao longa via STDIN (card explicito, 2o arg omitido)
bash ~/.claude/skills/prmake-timeline/scripts/prmake-timeline.sh 72517 <<'EOF'
Causa raiz identificada: o metodo nao tratava lista vazia.
Correcao aplicada e testada com sucesso.
EOF
```

O script imprime o HTTP code e a resposta. Saida `OK` (HTTP 2xx) significa que a entrada foi gravada.

## Notas

- Nao ha confirmacao previa: registrar na timeline e uma acao aditiva e de baixo risco (nao apaga
  nem sobrescreve nada). Registre livremente conforme avanca na analise.
- `cardNumber` e enviado como string, igual ao payload original do PRMake.
