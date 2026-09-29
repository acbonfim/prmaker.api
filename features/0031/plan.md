# Feature 0031 — Comentários e anexos do usuário no plano de execução

## Modelo
- **`ExecutionNote`** (tabela `execution.ExecutionNotes`): plano, card, **número por card** (`#n`, o mesmo nas abas Análise e Correção), etapa opcional, texto (markdown, até 20 mil caracteres), autor (id/nome), `FromExecutor` (comentário do Claude), criado/editado/removido (remoção lógica).
- **`ExecutionArtifact`** ganha `Number` (**número por card**, `anexo #n`, também para os arquivos da skill; os existentes são numerados na migração por ordem de criação) e `NoteId` (anexo de um comentário). Anexo de comentário nunca substitui outro arquivo: nome repetido vira `nome (2).ext`.
- A resposta do plano traz `notes` do **card inteiro** (com os anexos, que podem ser do outro plano); o `control` traz `lastUserNoteNumber`/`userNotesChangedAt` — o vigia (`watch`) acorda o Claude quando o usuário comenta.

## API (`ExecutionPlan`)
- `POST {id}/notes` (multipart: `text`, `stepKey`, `files[]`) — cria o comentário e os anexos juntos; Timeline recebe "💬 Comentário #n" com os anexos.
- `PATCH {id}/notes/{noteId}` (texto) e `DELETE {id}/notes/{noteId}` (remove também os anexos) — só o autor.
- `GET card/{card}/notes` — comentários do card com anexos (para a skill).
- Tempo real: ação `note`.

## Front (tela do card → plano de execução)
- Seção **Comentários e anexos** abaixo das etapas: lista (autor, `#n`, quando, etapa, texto em markdown, miniaturas das imagens e chips dos arquivos com `anexo #n`) e compositor.
- Compositor: texto; **arrastar e soltar** em qualquer parte do painel; **colar** imagem (Ctrl+V); **ícone de anexo** (seletor de arquivos, vários); prévia com remover; etapa (geral ou uma etapa; padrão = etapa aberta); Ctrl+Enter envia.
- Editar/remover os próprios comentários; clique na miniatura abre o visualizador de arquivos.

## Skill analisar-bug
- `prmake-plan.sh notes <card> [n]` — lista os comentários do card (novos marcados) e **baixa os anexos** para `$CARD_DIR/anexos/` (imprime o caminho local para o Claude abrir com Read).
- `prmake-plan.sh attachment <card> <ref>` — resolve `#12`, `12`, `imagem 2`, nome ou parte do nome em todos os arquivos do card e baixa.
- `prmake-plan.sh note <card> "texto" [arquivos…]` — o Claude responde/comenta no plano.
- `watch` mostra "comentários novos do usuário" e sai para o Claude ler.
- SKILL.md: comentários e anexos são entrada da análise (como os repro steps) — ler no início, a cada vigia e antes de decidir; toda referência ("veja a imagem 2", "anexo x", "comentário 3") → `attachment`/`notes` + abrir o arquivo.
