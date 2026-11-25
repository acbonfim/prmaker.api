# 0076 — Evidências do card lidas e guardadas no tratamento

## Pedido (do usuário, 2026-11-04)
Vários cards são analisados sem acesso às evidências — na **discussion**, na **descrição** ou nos **anexos** do card.
Elas precisam ser lidas na análise. No momento da análise, **todos os anexos devem ser lidos e guardados no
tratamento**.

## Hoje (master 65c770e)
- A skill lê o card por `GET Azure/card/{id}` (`bug-fetch.sh`) e **tira o HTML** dos repro steps/descrição: as imagens
  coladas (`<img src="https://dev.azure.com/.../_apis/wit/attachments/{guid}?fileName=image.png">`) somem sem aviso.
- A **discussion** do card não entra no `contexto` da análise (só o handover lê `Azure/card/{id}/full`).
- Os **arquivos anexados** ao card no DevOps (relações `AttachedFile`) nunca são listados nem baixados — e baixar
  exige a credencial do DevOps, que só o PRMake tem (Minhas integrações).
- O plano já guarda arquivos (`ExecutionArtifacts`, 10 MB/arquivo, 50 MB/plano), abre imagem para o Claude
  (`prmake_attachment` devolve a imagem) e a skill baixa arquivos do plano (`prmake-plan.sh attachment`).

## Como fica
### E1. O PRMake lista e importa as evidências do card
- **Lista** (`GET ExecutionPlan/card/{card}/evidence`): tudo o que o card tem —
  1. imagens/arquivos colados nos campos de texto (Repro Steps, Descrição e demais campos HTML), na ordem em que aparecem;
  2. imagens/arquivos colados nos **comentários da discussion** (autor e data de cada um);
  3. **arquivos anexados** ao card (nome, tamanho, quem anexou, quando, comentário do anexo).
  Sem repetir (o mesmo anexo citado em dois lugares conta uma vez, com a primeira origem).
- **Importa** (`POST ExecutionPlan/{id}/evidence/import`, idempotente): baixa cada evidência com a integração do
  DevOps de quem chama e guarda nos **arquivos do plano** (o "tratamento"), com nome que diz a origem
  (`ev01-descricao-image.png`, `ev04-discussion-image.png`, `ev06-anexo-log_erro.txt`) e descrição completa
  ("Evidência do DevOps — discussion: Fulano em 03/11/2026 14:20"). Já importada (pela chave do anexo no DevOps) não
  baixa de novo; evidência nova (comentário novo) entra na próxima importação. Maior que 10 MB ou estourando o limite
  do plano: fica listada como **não importada**, com o motivo.
- Só baixa de `dev.azure.com/<org>` / `<org>.visualstudio.com` em `/_apis/wit/attachments/` (sem URL arbitrária).

### E2. A análise lê tudo
- O `contexto` da skill **importa as evidências** e mostra a lista numerada (número do anexo no plano, origem, tipo) e
  o **texto da discussion** (autor, data, sem HTML; onde havia imagem, `[imagem: evidência #n]`); os repro steps também
  marcam onde havia imagem.
- O PRMake registra quando a skill **abriu** cada evidência (conteúdo lido pelo executor: `prmake_attachment` ou
  `prmake-plan.sh attachment`).
- **Regra**: `montar-analise` (config `EvidenceGateSteps`) só conclui com **todas as evidências legíveis abertas** —
  a recusa lista as que faltam e como abrir. Legíveis: imagem, PDF, texto/log/CSV/JSON/XML/HTML/e-mail, planilha e
  documento do Office (a skill converte para texto). Vídeo e compactados não travam: ficam como "abrir à mão" e a análise
  diz isso.
- A análise ganha a seção **Evidências do card**: cada evidência (link `anexo:` para imagem), o que ela mostrou e se
  confirma ou contradiz a hipótese.
- Retomada/correção: o `contexto`/`contexto-correcao` importa as novas (comentário com print novo depois da análise).

## Fora de escopo
- OCR/transcrição de vídeo; anexos de cards relacionados (só o próprio card); editar/apagar no DevOps.

## Critérios de aceite
1. Card com print colado no repro steps, outro num comentário e um `.txt` anexado: o `contexto` importa os três para o
   plano com a origem no nome/descrição e mostra a discussion; a análise não conclui sem abrir os três.
2. Reimportar não duplica; comentário novo com imagem entra na reimportação.
3. Arquivo grande demais aparece como não importado com o motivo; vídeo não trava a análise.
4. Sem integração do DevOps: a importação devolve o erro claro e a análise registra que as evidências não puderam ser
   lidas (bloqueia `montar-analise` até o usuário resolver ou autorizar seguir sem).

## Melhorias pedidas antes do merge (2026-11-04)
1. **O sistema conversa**: a tela mostra os textos que o Claude/PRMake gerou no card (análise, soluções, descrição do PR,
   root cause, orientação, passo a passo, chamado, resumo da situação), com "Ver" e um aviso discreto quando um texto novo
   aparece. A publicação do resumo da situação na discussion é **configurável por usuário** (automática ou manual; padrão
   global `SituationPostToDevOps`): manual = fica "pronto para publicar" com Ver/Publicar; a tela mostra o que foi
   publicado, quando e por quem.
2. **Itens da engenharia reversa** citados no texto (`UC-121`, `revamp-kaizen#RN-012`) viram link com popover do item
   (texto, armadilhas, quem cita) — só administradores leem (regra da 0074); os demais veem o aviso.
3. **Visual de sistema**: tabelas trabalhadas, selos no lugar de emojis (Alterar, Criar, Remover, Só leitura, Suspeito,
   Confirmado, Hipótese...), callouts discretos; a skill para de gerar emojis e usa os rótulos; notas e callouts com moderação.
