# Modelo do texto da analise (passo 4) e publicacao (passo 5)

Leia ao montar a analise (`bash ~/.claude/skills/analisar-bug/scripts/ref.sh analise <4|5>`).

### 4. Montar o texto da analise

Plano: etapa `causa-raiz` (log `finding` por hipotese, com o porque) e depois `montar-analise`: ao gravar
a analise e os scripts na pasta do card, rode `sync <card> montar-analise` — o usuario le a analise e baixa os
`.sql` direto na tela do card.
Monte a analise em `$CARD_DIR/analises/analise-inicial.md` (a pasta do passo 1b) seguindo esta
estrutura (Markdown, em portugues). Na analise, os scripts sao so as **consultas somente
leitura** que levaram ao problema: `$CARD_DIR/scripts/00_consulta-<assunto>.sql`, com um comentario no topo dizendo
o que cada uma mostrou (0050). **Script que altera dados (SQL de correcao) nao e da analise** — ele e o texto do
chamado vao no plano de correcao, na etapa do chamado (`ref.sh correcao 7`); aqui so descreva o que ele fara.
Evidencias/saidas de consulta vao em `$CARD_DIR/dados/`.

**Escreva para quem nao vai abrir o codigo (0075).** O leitor precisa entender em 30 segundos o problema, a causa e o
que sera feito; o detalhe tecnico fica nos popovers das fotografias. Regras:
- **Sempre a mesma ordem e os mesmos titulos `###`** (abaixo) — o usuario acha cada coisa no mesmo lugar.
- Abra com o callout `> [!SUMMARY]` (3 linhas: problema, causa, o que vamos fazer). Use `> [!WARNING]` para risco
  real e `> [!NOTE]` para contexto; nao abuse (no maximo 3 callouts).
- **Confirmado x hipotese** sempre marcados: `✅ **Confirmado:**` / `❓ **Hipotese:**`, com a evidencia em 1 linha.
- **Todo codigo citado e um link para a fotografia** (`[caminho:linhas](ref:<id>)`, passo 4b) — nunca `caminho:linha`
  solto, nunca bloco de codigo colado (o popover mostra o codigo real, o diff e as notas). O PRMake recusa concluir
  `montar-analise` com citacao sem fotografia.
- Frases curtas, sem jargao desnecessario; nada de "Opcao 1/A" — nomeie as coisas.
- Mudanca **visivel** (tela, layout, texto, relatorio, e-mail): **Antes x Depois** com imagens (passo 4c).

```markdown
**Analise — Card <card>: <titulo>**

> [!SUMMARY]
> **Problema:** <o que o usuario ve, em uma frase>
> **Causa (confirmada|provavel):** <a causa em uma frase>
> **O que vamos fazer:** <a correcao recomendada em uma frase (N arquivos, mundo legado/revamp)>

### O que vai ser feito
| # | Onde | Mudanca | Por que |
|---|---|---|---|
| 1 | [`<arquivo>:<linhas>`](ref:<id>) | ✏️ alterar · ➕ criar · 🗑️ remover | <1 linha> |
| 2 | [`<arquivo>:<linhas>`](ref:<id>) | 👁️ so leitura | <por que e citado e nao muda> |
<so a recomendada; as outras opcoes ficam em solucoes.md. Sem codigo (dados/config/orientacao): diga o que muda e onde>

### Onde parou
<o estado real do plano em 1 linha: ✅ o que ja foi feito · ⏳ o que esta aguardando e de quem · proximo passo>

### Problema relatado
<resumo do comportamento descrito nos repro steps e nos comentarios>

### Como reproduz
<o caminho do usuario ate o erro, numerado, do que se entende>

### Causa raiz
- ✅ **Confirmado:** <fato + evidencia (consulta, linha de codigo com ref, item RN/UC)>
- ❓ **Hipotese:** <o que falta confirmar e como>

### Evidencias
- **Codigo:** mundo/repositorio (legado `edv-solvace` ou `revamp-<modulo>`), commit da master lido; os trechos sao as refs acima
- **Base Solvace:** itens usados (`<modulo>#RN-012`) e lacunas registradas
- **Regras de negocio (Knowledge Center):** ART-n usados (ou "sem artigo — lacuna")
- **Banco de dados** *(obrigatorio)*: **consultado** (host/banco e o que confirmou; consultas `00_consulta-*.sql`) ·
  **nao necessario** (por que) · **nao consultado — o usuario pediu para seguir sem o banco** (resposta #n)
- **Cognito** *(so se consultado)*: achados sem PII desnecessaria

### Pontos suspeitos / de alteracao
- [`<caminho completo>:<linhas>`](ref:<id>) — <o que e e por que importa, 1 linha>

### Antes x Depois *(so mudanca visivel — passo 4c)*
![Antes — <o problema>](anexo:mock-antes.png) ![Depois — <o resultado esperado>](anexo:mock-depois.png)
*Simulacao a partir do codigo — nao e print do sistema.*

### Impacto e riscos
<quem mais usa os pontos de alteracao (prmake_base_impact), o que testar, risco de regressao>

### Padrao de tratamento provavel
<catalogo: A codigo · B dados/script · C acesso/Cognito · D configuracao · E user education · F change request · G nao
reproduz · H duplicado — o(s) padrao(oes) e o que isso implica (ex.: sem PR; chamado de script; orientar o cliente)>

### Proximos passos
<o que falta validar e a correcao proposta PARA O CASO DO CARD>

> [!NOTE]
> **Outros casos** *(opcional)*: <uma ou duas linhas, so como aviso — sem plano de correcao em lote>
```

### 4b. Fotografias do codigo (coderef) — todo codigo citado

Cada trecho que a analise (ou `solucoes.md`, checkpoint, perguntas) cita vira uma **fotografia** no PRMake: o codigo
real do commit lido, o foco destacado, a mudanca proposta (diff) e notas explicando cada trecho. O usuario clica no
link e ve o popover — por isso o texto nao precisa (nem deve) colar codigo.

```bash
# Leitura/suspeito (nao muda): so o foco
bash $PLAN coderef <card> sa3-init --repo edv-solvace --file solvace-asp/systems/sa3/sa3_busca_a3.asp --lines 239-289 \
  --change suspect --title "Ordem da inicializacao"

# Alteracao proposta: escreva SO as linhas novas que substituem A-B num arquivo (o script faz o diff)
bash $PLAN coderef <card> sa3-init --repo edv-solvace --file solvace-asp/systems/sa3/sa3_busca_a3.asp --lines 241-243 \
  --change change --title "Inicializar o seletor antes de restaurar o filtro" \
  --why "O filtro salvo e restaurado antes de o daterangepicker existir; sem formato, a data some." \
  --after $CARD_DIR/dados/sa3-init.novo --notes $CARD_DIR/dados/sa3-init.notas.json
# remover: --change remove (sem --after) · arquivo novo: --change create --after <conteudo inteiro>
```
- `--repo` le na **copia da master** (`revamp-repos.sh master`, 0067) e grava o commit; `--dir <pasta>` le outra pasta.
- `<ref>`: slug curto e estavel (`sa3-init`, `restore-date`); o mesmo id em `[texto](ref:<id>)` no texto.
- **`--why`** (obrigatorio em change/create/remove): por que este trecho muda, em 1–3 frases de quem le sem o codigo.
- **Notas** (`--notes`, JSON `[{"side":"old|new","from":241,"to":243,"text":"..."}]`): em cada parte relevante, **por que
  este codigo** e **por que faz sentido** — `old` = linhas do arquivo atual; `new` = linhas do arquivo proposto. 2–5 notas
  por ref; sem nota obvia ("aqui tem um if").
- Contexto em volta do foco = `CodeRefContextLines` do PRMake (padrao 30); foco grande demais → cite trechos menores.
- Ajustar sem nova fotografia (why, notas, change): `prmake_code_ref(card, ref, ...)`. As refs do card aparecem em
  `prmake_plan` (`codeRefs`).
- **Correcao**: depois do commit, `bash $PLAN coderef <card> <ref> --implemented <sha> --file <caminho> --dir "$WT"` grava
  o diff real na mesma ref (o popover mostra a aba *Feito*).
- O PRMake recusa concluir `montar-analise`/`propor-solucoes` com citacao `caminho:linha` ou `ref:` sem fotografia
  (a mensagem lista quais) — fotografe e conclua de novo.

### 4c. Mock visual — Antes x Depois (mudanca visivel)

Quando a correcao muda algo que o usuario **ve** (tela, layout, texto, mensagem, relatorio, e-mail), anexe imagens:
**Antes** (o problema reproduzido) e **Depois** (o resultado esperado com a solucao recomendada; uma por opcao se as
opcoes mudam a tela de jeitos diferentes).
1. Monte um HTML de simulacao **a partir do codigo real da tela**, lido na copia da master:
   - **Legado (ASP)**: o markup da pagina/partial (`.asp`) e os CSS do tema que ela carrega (ex.:
     `solvace-core/view_shared/wwwroot/templates/limitlessfull/...`): copie os `<link>` para caminhos absolutos
     `file://` da copia da master ou inline o CSS necessario.
   - **Revamp (Angular)**: o template do componente com os estilos globais compilados (`npx --yes sass <styles.scss>
     /tmp/x.css --load-path=node_modules` quando houver SCSS) e o CSS do componente; troque bindings por valores.
   - Textos/labels reais (traducoes do modulo), dados ficticios **sem PII**, e no Depois um destaque discreto da
     area alterada (contorno `outline: 3px solid #f5a623` e uma etiqueta "alterado").
2. Tire o print: `bash ~/.claude/skills/analisar-bug/scripts/mock-shot.sh <mock.html> $CARD_DIR/imagens/mock-depois.png
   [--width 1440] [--height 900] [--mobile]` (Chrome/Chromium/Edge headless; Windows tambem). Abra o PNG com Read e
   confira se parece com a tela real antes de anexar.
3. Anexe na fase da analise: `bash $PLAN upload <card> $CARD_DIR/imagens/mock-depois.png image montar-analise
   "Depois (simulacao)"` (e o `.html` fonte como `data`) — e cite no texto com `![Depois — ...](anexo:mock-depois.png)`;
   duas imagens na mesma linha ficam lado a lado no PRMake.
4. Sempre a legenda *"Simulacao a partir do codigo — nao e print do sistema"*. Sem fidelidade possivel (componente que
   nao roda isolado, tela montada no servidor), um wireframe anotado — e diga isso.

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
