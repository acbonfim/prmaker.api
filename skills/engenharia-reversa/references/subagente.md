# Cartão do subagente da engenharia reversa (0066)

Você escreve **uma área** de um documento da engenharia reversa. Tudo o que precisa está neste cartão e no **pacote da
área** (caminhos no seu prompt). O documento é lido por pessoas e por LLMs que **não vão abrir o código**: cada item
precisa responder sozinho o que é, quando vale, o valor exato, onde está e quem mais é afetado.

## Como trabalhar (barato e rápido — cada resposta sua relê todo o contexto)
1. **Leia o pacote inteiro numa resposta** (se ele for grande, várias chamadas `Read` com `offset`/`limit` na MESMA
   resposta). O pacote traz o código da área com o **número real de cada linha** — use-o no `**Onde:** arquivo:linha`.
2. **Não explore.** O pacote já traz, em "Apoio", os métodos de outros arquivos que a área chama (ação do controller e o
   service/repositório por trás). Só abra outro arquivo quando algo essencial para a regra não está no pacote — e, nesse
   caso, leia todos de uma vez (`Read` com `offset`/`limit`, várias na mesma resposta). Nada de `grep` um a um.
3. **Escreva** os itens no formato do modelo (abaixo), nas mesmas seções `##` do modelo, no arquivo de saída do prompt.
4. **Checkpoint:** grave a cada ~10 itens (a primeira vez com `Write`; depois acrescente com `Edit` no fim da seção ou
   `cat >> arquivo <<'EOF'`). **Se o arquivo de saída já existe quando você começa, leia-o e continue de onde parou** —
   não reescreva o que já está lá (você pode ser uma retomada depois de uma queda).
5. **Ao terminar:** rode o comando `faltando` do prompt e cubra o que faltar (ou registre `GAP` com o motivo).
6. **Responda só o resumo** (até 8 linhas): itens por tipo, GAPs, dúvidas, armadilhas vistas (elas não entram no documento).

## Regras do conteúdo
- **Só os tipos que o cabeçalho do pacote lista.** O que é do módulo inteiro tem subagente próprio (glossário; banco:
  tabelas, objetos, triggers e jobs; módulo: tecnologias, configuração, segurança, observabilidade, infra) ou é da sessão
  principal (perfis `PRF`, resumo) — **não crie**: cite pelo nome; liste no resumo final os perfis que viu (`arquivo:linha`).
  Se você é um subagente especial (`glossario`, `banco…`, `modulo`), siga o cabeçalho do seu pacote.
- **IDs só da faixa da sua área** (ex.: 1301 a 1400 — a mesma faixa vale para todos os tipos: RN-1301, UC-1301, TELA-1301).
  Item que já existe no publicado mantém o ID dele (o pacote/sessão mostram). A junção renumera as faixas depois.
- **Nada inventado.** Regra com condição, valores e mensagem **literais** (entre aspas, como no código) e `**Onde:**`.
  Não confirmou no código → "a confirmar" + `GAP`.
- Um assunto por item, item enxuto (5–12 linhas), título que diz a regra (é o que a busca acha). Referencie outros itens
  pelo ID em vez de repetir texto. Regra que vale em várias telas: **um item** com a tabela das variações por tela.
- **Integrações (`INT`)**: `**Módulos:**` só com chaves da lista abaixo (ou `ext:<serviço>` — `ext:redis`, `ext:s3`,
  `ext:sns`, `ext:sql-agent`…); `**Mecanismo:**` do vocabulário (http · evento · fila (SQS) · banco compartilhado ·
  pacote/biblioteca · arquivo/S3 · cache (Redis) · job/agendamento · trigger · front-end · externo) com o nome real
  (fila, tópico, tabela, chave de cache); `**Contrato:**` síncrono/assíncrono e quem inicia; dúvida vai em `**Confirmar:**`,
  nunca no `**Módulos:**`. É isso que desenha o mapa de ligações.
- Eventos, filas, cache, triggers e jobs são parte da regra: diga quando a ação é assíncrona, o que fica em cache (e
  quando invalida) e o que um trigger/job faz "escondido".
- Sem segredo (senha, connection string, token, chave). Somente leitura no código e no banco. Nada de commit.
