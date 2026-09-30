# Feature 0032 — Correções do plano/timeline e integrações mais intuitivas

## Item 2 — anexos duplicados (causa raiz)
Não era a tela: a skill **reenviou** os anexos. O `notes` baixa os anexos do comentário para `anexos-prmake/`; a
sessão copiou para `imagens/` (a SKILL.md mandava "imagens que o usuário mandar vão em imagens/") e o `sync` subiu
como arquivos novos (`5-colado-…png` #11–#13 na análise); o `pull` da correção trouxe de novo para `imagens/` e o
`sync` da correção criou #28–#30. Mesmo sha256 dos anexos #5–#7.
- **B**: `UploadArtifactAsync` — envio da skill com conteúdo idêntico a um anexo de comentário do card devolve o
  próprio anexo (não cria arquivo).
- **S**: `sync` pula conteúdo igual a anexo do usuário; `pull` ignora anexos de comentário; SKILL.md: anexos do
  PRMake ficam só em `anexos-prmake/`.
- **F**: o plano esconde as cópias já existentes (arquivo da skill com o sha256 de um anexo de comentário).

## Item 3 — perguntas
- **F**: cada opção vira um cartão com rótulo + descrição visível (recomendada destacada); resposta mostra a
  descrição da opção escolhida.
- **B**: Timeline da pergunta lista `**rótulo** — descrição`; a da resposta traz a descrição da opção escolhida.
- **S**: SKILL.md — rótulo é a opção em si (nunca "Opção 1"); `ask` imprime as descrições no terminal.

## Item 4 — placeholder da Timeline
- **F**: placeholder "Escreva um registro..."; "Enter envia · Shift+Enter quebra linha" no tooltip/aria-label.

## Itens 1 e 5 — integrações
Modelo inalterado (retrocompatível): `Plugin.FieldSettings` (JSON) ganha `Suggestions` (lista) e `Help` (texto) —
opcionais; JSON antigo continua válido. Sem migração.
- **B**: `UserIntegrationFieldResponse.Suggestions` (valor global + sugestões do admin, sem repetir; nunca para
  sensível/fixo) e `Help`.
- **F (usuário) — Minhas integrações**: resumo de progresso; integrações em painéis (pendentes abertas primeiro);
  campos do usuário separados dos fixos do admin (recolhidos); ajuda por campo; **lista de escolha (autocomplete)**
  com as sugestões; indicador de alterações não salvas.
- **F (admin) — Plugins**: um editor único por plugin (abas Geral e Campos) no lugar dos diálogos soltos; por campo:
  valor, nome amigável, quem preenche (usuário/fixo), obrigatório/opcional, usar valor como padrão, oculto,
  sugestões (uma por linha) e ajuda. Botões legados "URLs"/"Autenticação" saem (o backend nunca gravou esses dados).
  "Duplicar" passa a copiar as configurações e os campos.

## Fases
| Fase | Descrição | Depende |
|---|---|---|
| B1 | Upload sem duplicar anexo; Timeline das perguntas com o texto das opções | — |
| B2 | `Suggestions`/`Help` em FieldSettings e em Minhas integrações | — |
| S1 | Skill: sync/pull/ask + SKILL.md | — |
| F1 | Plano: esconder cópias, opções visíveis; placeholder da Timeline | — |
| F2 | Minhas integrações (pick list + layout) | B2 |
| F3 | Editor de plugins (admin) | B2 |
| Q1 | Builds, teste local, PRs back + front | todas |
