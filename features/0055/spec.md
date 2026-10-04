# Feature 0055 — De onde a análise leu: engenharia reversa × base × código

> **Status: em execução (2026-10-04).** Pedido do usuário depois da 0054: "quando compara MCP, Base Solvace e código,
> analisa também o que foi lido direto da engenharia reversa ao invés de ficar indo ao código?" — "pode começar" e
> "pode mergear no final".

## Problema
O consumo do card (0041/0045/0047) mostra tokens por modelo, MCP × script e "Base Solvace: N consultas · buscas no
código: M". Não responde se a análise **leu da engenharia reversa (ER) em vez de ir ao código**:
1. A base é um número só: item da ER, seção antiga e artigo do Knowledge Center contam igual.
2. Leitura de código não é medida — só buscas (`Grep`/`Glob`/`grep`/`find`). O `Read` de `.asp`/`.cs`/`.ts`, onde a
   análise mais gasta, não aparece.
3. Só contagem de chamadas, sem volume: um item de 200 tokens pesa o mesmo que um arquivo de 8.000.
4. Não separa **confirmação** (abrir o `arquivo:linha` que o item da ER cita no `Onde:` — o que a skill manda) de
   **exploração** (ir ao código atrás do que a ER deveria dizer — lacuna ou desperdício).

## Decisões

### 1. Origens medidas por sessão
O mesmo leitor do transcript (`prmake-plan.sh usage`) classifica cada chamada de ferramenta e estima os tokens do
resultado que entrou no contexto (tamanho do texto ÷ 4 — estimativa, sem custo de IA):

| Origem | O que conta |
|---|---|
| `re` — Engenharia reversa | `prmake_base_get` de itens (`modulo#RN-012`), `prmake_base_search/module/impact`, `kb.sh re …`, `re.sh get/find/impact`, e o bloco `=== ENGENHARIA REVERSA` do contexto do card |
| `base` — Base antiga e KC | `prmake_base_get` de seção (`projeto/secao`) ou artigo (`ART-n`), `kb.sh show/find/index`, leitura do espelho local |
| `code-confirm` — Código: confirmação | leitura de arquivo de código citado no `Onde:`/evidência de um item da ER lido antes na sessão |
| `code-explore` — Código: exploração | leitura de arquivo de código sem item da ER que o cite; subagente de busca (`Explore`) |
| `code-search` — Código: buscas | `Grep`, `Glob`, `grep`/`rg`/`find` no Bash (o `searchCalls` de hoje) |

Leitura de código = `Read` de arquivo fora das pastas do Claude/PRMake/card (e `cat`/`sed -n`/`head`/`tail` de arquivo
de código no Bash). Arquivos do card, anexos, skills e o espelho não são código.

### 2. O que fica gravado
- Por sessão: a lista de origens (chamadas e tokens estimados) e os **arquivos explorados** (até 10, com tokens) — os
  candidatos a lacuna da ER.
- `kbCalls`/`searchCalls` continuam (compatibilidade com o comparativo de executores e sessões antigas).
- Sessão antiga (sem a lista) aparece como hoje.

### 3. Onde aparece
- **Consumo do plano** (popover do consumo): tabela por origem (chamadas, tokens, % do que foi lido), a frase
  "Engenharia reversa: X% do lido" e os arquivos explorados com "isso deveria estar na engenharia reversa?".
- **Executores** (comparativo): média de "% lido da ER" e de tokens de exploração de código por plano.
- Módulo com ER completa e exploração alta = sinal de lacuna: a lista de arquivos explorados vai junto para quem
  revisa (registrar `gap` no documento certo pelo `arch.sh suggest`).

## Fora do escopo
- Bloquear a análise pela proporção (a trava da 0052 continua sendo a citação dos itens).
- Tokens exatos por resultado (o transcript só traz o uso por resposta; a estimativa basta para comparar).
- Aprendizado entre cards (itens que resolveram cards parecidos) — proposta separada.
