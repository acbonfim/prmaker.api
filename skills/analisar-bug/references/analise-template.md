# Modelo do texto da analise (passo 4) e publicacao (passo 5)

Lido ao montar a analise.

### 4. Montar o texto da analise

Plano: etapa `causa-raiz` (log `finding` por hipotese, com o porque) e depois `montar-analise`: ao gravar
a analise e os scripts na pasta do card, rode `sync <card> montar-analise` — o usuario le a analise e baixa os
`.sql` direto na tela do card.
Monte a analise em `$CARD_DIR/analises/analise-inicial.md` (a pasta do passo 1b) seguindo esta
estrutura (Markdown, objetivo e claro, em portugues). Se gerar scripts (ex.: SQL de correcao),
salve-os em `$CARD_DIR/scripts/` — quando a **ordem de execucao importa**, prefixe `01_nome.sql`,
`02_nome.sql`, ...; use `99_rollback_*.sql` para rollback (ou embuta o rollback como bloco comentado
no proprio script). Evidencias/saidas de consulta vao em `$CARD_DIR/dados/`.

```markdown
**Analise inicial — Card <card>: <titulo>**

**Problema relatado**
<resumo do comportamento descrito nos repro steps>

**Fluxo/Reproducao provavel**
<o caminho do usuario ate o erro, do que se entende>

**Investigacao no codigo**
<em qual mundo/repo esta o codigo (legado edv-solvace ou revamp-<modulo>); arquivos/metodos relevantes em `caminho:linha`>

**Contexto do usuario/ambiente (Cognito)** *(incluir so se consultado no passo 3b)*
<achados relevantes: status/enabled/grupos/atributos que expliquem o comportamento; sem PII desnecessaria>

**Banco de dados** *(obrigatorio)*
<uma linha: **consultado** (host/banco e o que confirmou) · **nao necessario** (por que o caso nao depende de dados) ·
**nao consultado — o usuario pediu para seguir sem o banco** (resposta #n; o que ficou como hipotese por isso)>

**Dados relevantes (SQL)** *(incluir so se consultado no passo 3c)*
<achados no banco que expliquem/confirmem o bug: registro faltando, status inesperado, inconsistencia — resumido, sem PII desnecessaria>

**Causa raiz provavel (hipoteses)**
<hipotese(s) priorizada(s), com o porque; deixe claro o que e hipotese vs. confirmado>

**Pontos suspeitos**
<lista de arquivos/metodos a investigar/corrigir — `caminho:linha`>

**Padrao de tratamento provavel** *(catalogo: A codigo · B dados/script · C acesso/Cognito · D configuracao · E user education · F change request · G nao reproduz · H duplicado)*
<padrao(oes) e por que; o que isso implica (ex.: sem PR; chamado de script; orientar o cliente)>

**Proximos passos / a confirmar**
<o que falta validar e a correcao proposta PARA O CASO DO CARD (usuario/registro/fluxo relatado)>

**Observacao: outros casos** *(opcional; so se a investigacao encontrou outros afetados)*
<uma ou duas linhas, apenas como aviso — sem plano de correcao em lote>
```

### 5. Publicar na Timeline
Poste a analise completa na timeline via a skill `prmake-timeline` (acao aditiva, baixo risco — nao
precisa de confirmacao previa; publique direto apos montar):
```bash
bash ~/.claude/skills/prmake-timeline/scripts/prmake-timeline.sh <card> < "$CARD_DIR/analises/analise-inicial.md"
```
(A descricao vai via STDIN, entao textos longos/multilinha passam inteiros.)

O script imprime o HTTP code e a resposta; `OK` (HTTP 2xx) significa que a entrada foi gravada.

Plano: etapa `publicar` (running → completed). O plano de analise **ainda nao termina** — segue para
`propor-solucoes` (passo 6), que o conclui depois das respostas.
