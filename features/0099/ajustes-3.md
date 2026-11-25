# 0099 — Ajustes 3 (2026-11-24): modal do card no estilo do DevOps

Pedido do usuário (resumo):

1. Separar os dados do card por categorias em abas.
2. Abrir o card como modal, com largura maior.
3. "Alterado: n campos" vira link para o detalhe do que mudou (antes × depois).
4. Confirmação (popover) depois de clicar em Salvar no modal.
5. Discussion numa aba própria: marcar pessoa (`@`) e mencionar outro card (`#`), com campo de texto e botão **Enviar**
   (diferente de Salvar).
6. Aba de anexos do card. O especialista também lê os anexos dos cards quando precisa.
7. Modal parecido com o do DevOps (familiaridade), respeitando a UI/UX do PRMake:
   - abas à direita;
   - State, Area, Iteration e Reason no cabeçalho;
   - responsável e tags no cabeçalho (adicionar/remover);
   - todos os campos clicáveis e editáveis.

## Front (`DevOpsCardPanelComponent` → modal)
- **Cabeçalho como o do DevOps:**
  - tipo e id com link;
  - título editável;
  - responsável (combo de pessoas);
  - contagem de comentários (abre a aba Discussion);
  - tags (chips com ×, e + para adicionar);
  - Salvar (com confirmação), recarregar, fechar;
  - Estado, Reason, Área, Iteração, coluna/lane/Done do board.
- **Abas:**
  - Detalhes: textos longos + Planejamento;
  - Campos (outros e somente leitura);
  - Discussion (n);
  - Anexos (n);
  - Relacionados (pai, filhos, links).
- **Alterações pendentes:** "Alterado: …" é um link que abre a lista antes × depois. Salvar abre o popover de confirmação
  com a mesma lista.
- **Comentário:** sai do formulário (não vai mais junto com Salvar). Vai pelo **Enviar** da aba Discussion.

## Contrato (backend)

### `GET api/v1/DevOps/workitems/{id}/comments?top=200`
Resposta: `{ count, comments: [{ id, html, author: {displayName, uniqueName, id, imageUrl} | null, createdDate, modifiedDate, edited }] }`
- Mais novos primeiro, sem os excluídos.
- Comentário em markdown no DevOps → `renderedText`.

### `POST api/v1/DevOps/workitems/{id}/comment { text, mentions?: [{ displayName, uniqueName, id? }] }`
- Markdown → HTML.
- `#123` → menção de card do DevOps: `<a href=".../_workitems/edit/123" data-vss-mention="version:1.0">`.
- `@Nome` de cada menção → `<a href="#" data-vss-mention="version:2.0,{guid}">` (notifica a pessoa no DevOps).
  - GUID = `id`, quando já é um GUID; senão, a busca de identidades pelo login.
  - Sem GUID, o `@Nome` fica como texto.
- Nada é convertido dentro de `<a>`, `<code>`, `<pre>` ou de entidades HTML.

### `GET api/v1/DevOps/workitems/{id}/attachments`
Resposta: `[{ id (guid), name, url, source: anexo|campo|discussion, sourceDetail, at, size, comment, isImage }]`
- Usa o leitor das evidências da 0076, com a integração pessoal.

### `GET api/v1/DevOps/workitems/{id}/attachments/{guid}?name=&download=false`
- Devolve os bytes do anexo. Só o GUID entra na URL do DevOps.
- Limite de 25 MB (acima disso: 413 `too_large`).
- `download=true` baixa o arquivo com o nome.

### MCP (somente leitura, com a integração de quem age)
- `prmake_devops_card_comments(id, top?)`
- `prmake_devops_card_attachments(id)`
- `prmake_devops_card_attachment(id, "#n" | nome)`: a imagem volta como imagem.
- As skills `especialista-de-gestao`, `tarefas-de-gestao` (understanding) e `especialista-do-caso` já orientam o uso.

Testes: `DevOpsCardModal0099Tests` (menções, discussion, anexos, referência do MCP).
