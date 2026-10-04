# Feature 0054 — Engenharia reversa como fonte; Base Solvace como leitura e aprendizado

> **Status: especificada — não iniciar.** Depende da 0053 (banco da DEMO, glossário com sinônimos, correção do
> sumário), que por sua vez espera o fim do piloto do SA3 (`legado-rca`).

## Problema
Desde a 0052 a engenharia reversa (ER) e a Base Solvace convivem **sem hierarquia**: o documento publicado vira a seção
`re-<tipo>` **ao lado** das seções antigas do mesmo projeto (`010-visao-geral`, `020-modulos`, `030-dados`,
`080-regras-de-negocio`, `085-operacao`, `090-armadilhas`…). Análise, busca, "Pergunte", MCP e espelho leem tudo junto —
quando a seção antiga está desatualizada ou diz outra coisa, a informação cruza e atrapalha a análise. Além disso:
- o modo **Simples** da Base Solvace (Guia `guia-*`) é escrito à parte e não aproveita a análise profunda da ER;
- aprendizados e divergências dos bugs e as lacunas do "Pergunte" caem numa fila de sugestões que o admin aplica nas
  seções antigas — não chegam à ER.

## Pedido (usuário, 2026-10-04)
> A engenharia deveria ser a fonte e a Base Solvace deveria trazer o que de fato foi analisado lá, para não ter
> informação cruzada. A visão Simples deveria ser considerada na engenharia reversa para o usuário menos técnico se
> beneficiar. A Base Solvace não precisa morrer, mas precisa fazer sentido e funcionar em conjunto com a ER, ainda se
> beneficiando do aprendizado com os bugs tratados e das perguntas que o usuário não encontrou.

## Decisões

### 1. Papéis
| Camada | Papel |
|---|---|
| **Engenharia reversa** | Fonte única do "como o sistema é" de cada módulo (técnico e funcional), com aprovação. |
| **Base Solvace** | Camada de **leitura** (técnico e Simples, busca, Pergunte, MCP, espelho) e de **aprendizado** (bugs, perguntas, Knowledge Center). Mostra o que a ER publicou + o que a operação ensinou. |

### 2. Uma fonte por módulo (fim da informação cruzada)
- Mapa seção antiga → documento da ER que a substitui:

  | Seção antiga | Substituída por |
  |---|---|
  | `010-visao-geral` | `re-visao` + `re-arquitetura` (visão técnica) |
  | `020-modulos` | `re-funcional` + `re-uiux` |
  | `030-dados` | `re-arquitetura` (dados + banco da 0053) |
  | `040-integracoes` | `re-arquitetura` (`INT`) |
  | `050-infra`, `060-autenticacao`, `070-jobs` | `re-arquitetura` |
  | `080-regras-de-negocio` | `re-funcional` (`RN`) |
  | `085-operacao` | `re-funcional` (`CFG`, `PRF`) |
  | `090-armadilhas` | **não** substituída — vira a camada de armadilhas (§4) |
  | `guia-*` (Simples) | documento `guia` da ER (§3) |

  O mapa é configurável (plugin "Skills Configurations", chave `ReverseEngineeringSupersedes`) — nada fixo.
- Quando **todos** os documentos que substituem uma seção estão publicados, a seção antiga fica **substituída**: sai do
  espelho, do índice compacto, do MCP, da busca e do "Pergunte"; continua na tela como histórico, com a faixa
  "substituída pela engenharia reversa (link)". Publicação parcial não substitui nada (sem buracos).
- Módulo sem ER continua com a base antiga, igual a hoje (transição módulo a módulo).
- O índice compacto (`kb.sh index`) e o `for-card` dizem por módulo qual fonte vale (`ER` ou `base antiga`).

### 3. Visão Simples dentro da ER — documento `guia`
- 7º tipo de documento: **"Guia (visão simples)"**, seção `re-guia`, público `human` (fica fora das análises; vai para o
  modo Simples, a busca de pessoas e o "Pergunte").
- Gerado na mesma skill (`/engenharia-reversa <módulo> guia`, último do `tudo`), **só a partir do que está publicado**
  na ER do módulo — sem ler código: linguagem simples, sem tabela/classe/endpoint, frases curtas, passo a passo.
- Seções: o que é e para que serve · como funciona (passo a passo do usuário, status) · regras (quem pode o quê,
  prazos, aprovações) · como configurar e dar acesso · com quem conversa · como testar · perguntas frequentes ·
  glossário.
- Cada parágrafo/pergunta guarda os IDs de origem (`<!-- fonte: RN-012, UC-003 -->`); na tela, "ver detalhe técnico"
  abre o item na ER. Lint: parágrafo sem fonte = aviso; termo técnico proibido (nome de tabela/classe/endpoint) = erro.
- **Perguntas frequentes** vêm das perguntas reais do "Pergunte" sobre o módulo (fila `ArchitectureQuestion`) — a
  resposta cita os IDs; **glossário** vem dos `GLO` (0053).
- Aprovado e publicado como os outros; **substitui** o Guia antigo (`guia-*`) do projeto no modo Simples.
- Ao republicar um documento técnico, o guia fica marcado "desatualizado" (lista os IDs que mudaram) até a próxima
  sessão do guia.

### 4. Armadilhas — o que a operação ensinou
- A ER descreve o sistema "como é"; armadilhas (o que quebra, causas raiz já vistas, configurações que confundem,
  consultas de diagnóstico somente leitura) são conhecimento de operação e **ficam na Base Solvace**, mas ligadas aos
  itens da ER: entidade `ReverseTrap` (módulo, itens relacionados `RN-…`/`TELA-…`, texto, cards de origem, quem
  confirmou, data).
- Origem: aprendizados dos bugs (analisar-bug passo 9) e o "Aprender com um card", agora apontando para o item.
- As análises recebem a armadilha **junto** com o item (`prmake_base_get` e `for-card` mostram "armadilhas: …").
- Tela: aba "Armadilhas" do módulo (Engenharia reversa) e no detalhe do item; na Base Solvace, a seção técnica
  "Armadilhas" do projeto passa a ser gerada a partir delas.

### 5. Aprendizado desemboca na ER
- Sugestões (`ArchitectureSuggestion`) de módulo com ER passam a apontar o **documento + item** (`re-funcional` +
  `RN-020`): `learning`/`divergence` de card, `gap` do "Pergunte", divergência KC × código.
- Entram no pacote da próxima sessão `melhorar` do documento (já é assim na 0052) e aparecem na aba do documento;
  aplicadas só depois de revisão/publicação — nunca editam a seção direto.
- Sugestão que é armadilha (não muda o "como é") vira `ReverseTrap` com um clique na tela (aprovador).
- Lacunas do "Pergunte" sem resposta na ER: o documento certo ganha um `GAP` na próxima sessão e, se respondida, a
  pergunta vai para as perguntas frequentes do guia.

### 6. Knowledge Center
- Continua como documentação oficial de regra de negócio (sincronização e filtros como hoje).
- Item `RN` cita o artigo (`**KC:** ART-n`); divergência artigo × código vira `GAP` no documento e aviso no artigo
  (lista "artigos com divergência" na tela, para o time de produto).

### 7. O que as análises leem
- Módulo com ER publicada: itens da ER + armadilhas ligadas + KC. Seções antigas substituídas não entram (nem no
  espelho).
- Módulo sem ER: base antiga, como hoje.
- `analisar-bug` passo 9: aprendizado com o ID do item → sugestão no documento ou armadilha.

## Migração do que já existe
- Seções antigas: nada é apagado; viram "substituídas" só quando a ER do módulo cobrir (§2).
- Guia antigo (`guia-*`): continua no modo Simples até o `guia` da ER ser publicado.
- Armadilhas antigas (`090-armadilhas`) e sugestões pendentes: ver pergunta 1.

## Fora do escopo
- Apagar a base antiga; mudar o Knowledge Center (fonte externa); rodar a ER pelo executor.

## Perguntas em aberto
1. Armadilhas existentes (`090-armadilhas` de cada projeto) e sugestões pendentes: migrar **automaticamente** para
   `ReverseTrap`/sugestões por item quando o módulo tiver ER (a IA liga cada uma ao item mais provável, com revisão), ou
   **só na próxima sessão "melhorar"** de cada módulo?
2. O `guia` entra nos documentos **exigidos** para o módulo contar como completo (`ReverseEngineeringRequiredDocs`)?
3. Seções antigas substituídas: manter visíveis na tela como histórico (proposta) ou esconder de vez?
4. Projetos transversais (`ecossistema`, `operacao-plataforma`, `infra-aws`, `login`, `regras-de-negocio`): ficam na
   base antiga (proposta) ou ganham ER própria depois?
