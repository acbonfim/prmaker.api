# 0099 — Ajustes 4 (2026-11-25): coluna no Test in production, Histórico, modal único, galeria e PDF

Pedido do usuário (resumo):

1. "Mover para Test in production" muda o **estado** (ex.: Test QA) e também a **coluna do board** (ex.: QA Done) —
   no DevOps são coisas diferentes. A coluna precisa ser configurável, como o estado.
2. Aba **Histórico** no modal do card (como a do "Ver tudo" da Bancada).
3. **Um modal só**: o "Ver tudo" da Bancada abre o mesmo modal da Mesa, sem "Abrir na Bancada" e "Adicionar ao épico".
4. Visualizador de arquivos/imagens: **← →** do teclado e setas nas bordas para ir e voltar (apagadas nas pontas).
5. **Prévia de PDF** (antes: "Pré-visualização indisponível").

## Back
- `BugTestInProductionColumn` no "AI Configurations":
  - chave e `DevOpsTestInProductionConfig.Column`;
  - `MoveToTestInProductionAsync` grava `WEF_…_Kanban.Column` do board do time do card, no mesmo PATCH do estado/área/comentário;
  - com coluna configurada e o card fora de qualquer board → 409;
  - entrada no catálogo de configurações.
- Migração `SeedTestInProductionColumn0099`:
  - valor global `QA Done`;
  - campo opcional com `UseGlobalDefault`;
  - entra nos `PersonalFields` quando o estado já é pessoal;
  - só acrescenta o que falta (valor editado fica).
- `GET DevOps/workitems/{id}/history`:
  - revisões de `…/updates`, mais novas primeiro, com quem, quando, `created`, campos antes → depois, links incluídos/removidos e o comentário da revisão;
  - sem os campos que mudam a cada revisão nem os `WEF_` (repetem `System.BoardColumn`).
- `GET DevOps/workitems/{id}/board`:
  - `{team, board}` em que o card aparece, pelo campo `WEF_…_Kanban.Column` ativo (`ExtensionMarker`);
  - procura primeiro o time da área do card;
  - cache de 1 h por campo.
- `[CardAccess]`:
  - endpoints do CARD liberados também para o perfil dev (`user`/`support`), que abre o modal pela Bancada: ler, editar, mover no board, comentar, anexos, histórico, metadados e consulta;
  - criar card, `teams`/`boards`/`board/items` e épicos seguem só admin/management.
- Testes:
  - `TestInProductionColumn0099Tests` (4);
  - `ModalUnico0099Tests` (acesso, histórico, board).

## Front
- `DevOpsCardHistoryComponent` (aba Histórico):
  - resumo de uma linha por revisão;
  - campos antes → depois, com texto longo recolhido;
  - criação com os campos iniciais recolhidos;
  - links clicáveis (#card abre no modal);
  - comentário da revisão;
  - "Esconder comentários".
- `DevOpsCardPanelComponent`:
  - `context` = `desk` | `workbench`;
  - na Bancada some "Abrir na Bancada"/"Adicionar ao épico";
  - sem time/board da página, acha o board pelo card (`cardBoard`) para coluna/lane/Done.
- `DevOpsCardDialogComponent` + `openDevOpsCardDialog`:
  - o modal fora da Mesa;
  - Bancada (`openDetails`), painel de PRs e a tela de PR antiga;
  - `CardDetailsDialogComponent` removido;
  - `DEVOPS_CARD_DIALOG`/`cardDialogEscape` compartilhados com a Mesa.
- Visualizador:
  - `FileViewerDialogData.items/index` + `openFileViewer`;
  - setas nas bordas e ← → (fora de campos de texto);
  - contador "n de N";
  - adianta a imagem vizinha.
  - Usado em:
    - anexos do card;
    - evidências;
    - arquivos do item (só os com prévia);
    - imagens do markdown (`md-image-viewer` com galeria);
    - imagens coladas no card (diretiva abre o visualizador da aba, não mais nova aba).
- PDF:
  - `fileViewKind`/`filePreviewKind` → `pdf`;
  - iframe com blob `application/pdf` (o leitor do navegador);
  - sem "Copiar" para PDF.

## e2e (`.t0099c`, `modal2.mjs`)
- DevOps falso: `…/updates` (com troca de estado, link e comentário), `ExtensionMarker`, `manual.pdf` (octet-stream).
- Mesa:
  - Histórico com 2 revisões;
  - PDF no iframe;
  - ← → de 4 anexos, com as pontas desabilitadas;
  - Esc fecha só o visualizador;
  - imagem dos Repro Steps abre o visualizador.
- Bancada (dev):
  - "Ver tudo" abre o modal sem os botões da Mesa;
  - coluna do board achada pelo card;
  - Histórico;
  - Esc fecha.
- API: `Azure/card/{id}/actions/test-in-production` → PATCH com `System.State` + `WEF_AAA_Kanban.Column` + comentário.
- Migração conferida no banco do harness com o plugin pessoal.
