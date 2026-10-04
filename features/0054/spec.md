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
  | `guia-*` (Simples) | documento `pratica` da ER — visão prática (§3) |

  O mapa é configurável (plugin "Skills Configurations", chave `ReverseEngineeringSupersedes`) — nada fixo.
- Quando **todos** os documentos que substituem uma seção estão publicados, a seção antiga fica **substituída**: sai do
  espelho, do índice compacto, do MCP, da busca e do "Pergunte"; continua na tela como histórico, com a faixa
  "substituída pela engenharia reversa (link)". Publicação parcial não substitui nada (sem buracos).
- Módulo sem ER continua com a base antiga, igual a hoje (transição módulo a módulo).
- O índice compacto (`kb.sh index`) e o `for-card` dizem por módulo qual fonte vale (`ER` ou `base antiga`).

### 3. Visão prática (não técnica) — 7º documento, depois de todos os obrigatórios
Ideia do usuário (2026-10-04): depois dos 6 documentos, uma última etapa que **depende de todos os documentos
obrigatórios publicados** e gera a visão amigável que fica na Base Solvace — responde as perguntas práticas do dia a
dia e é o guia do sistema.
- Tipo `pratica` — **"Visão prática (não técnica)"**, seção `re-pratica`, público `human` (vai para o modo Simples, a
  busca de pessoas e o "Pergunte"; não entra nas análises de bug). **Exigido** para o módulo contar como completo
  (resposta 2), mas só pode ser iniciado quando os demais obrigatórios estão publicados — a API recusa a sessão antes
  disso, e a tela/`Como gerar` mostra o que falta. No `tudo`, é o último.
- Gerado **só a partir do que está publicado** na ER do módulo (sem ler código), com linguagem de quem usa o sistema:
  sem tabela, classe, endpoint ou caminho de arquivo; nome de tela e de menu como o usuário vê.
- Conteúdo e tipos de item (novos no contrato do índice):
  - **O que é e onde fica**: para que serve, quem usa, se é **legado ou revamp** (e onde convivem), como habilitar.
  - **Como chegar**: caminho de menu até cada tela (`TELA-…` da ER) — "Menu → Melhoria → A3 → Novo".
  - **Como fazer** — itens **`TUT-…`** (passo a passo por tarefa: criar um RCA, aprovar, reabrir, exportar, configurar
    um tipo…), cada passo com a tela e o que o usuário vê, incluindo mensagens de erro comuns e o que fazer.
  - **Perguntas práticas** — itens **`FAQ-…`**: "o SA3 é legado ou revamp?", "como saber se um usuário logou com
    sucesso?", "por que o botão X não aparece?", "quem recebe o e-mail?" — vindas das perguntas reais do "Pergunte"
    sobre o módulo (`ArchitectureQuestion`), das lacunas registradas e das dúvidas recorrentes nos cards (sugestões).
    Pergunta que depende de outro módulo responde com o que a ER dele publicou e aponta o módulo (ex.: login).
  - **Regras em linguagem simples**, **como configurar e dar acesso**, **como testar** (QA), **glossário** (dos `GLO`
    da 0053).
- **Assertividade (o core da Solvace)**: toda afirmação, passo de `TUT` e resposta de `FAQ` cita os IDs da ER de onde
  veio (`<!-- fonte: RN-012, TELA-003 -->`); lint: item sem fonte = **erro**, termo técnico proibido = erro, fonte que
  aponta para item removido = erro. Nada de "a confirmar" escondido: o que a ER não cobre vira `GAP` na ER (não é
  inventado na visão prática). Na tela, "ver detalhe técnico" abre o item de origem.
- **Validação por perguntas reais**: antes do envio, a skill roda as perguntas do "Pergunte" já feitas sobre o módulo
  (e uma bateria mínima por módulo, como a da 0040) contra o documento e mostra quais ficam sem resposta — o revisor vê
  essa cobertura de perguntas na revisão, como a cobertura do código.
- Aprovado e publicado como os outros; **substitui** o Guia antigo (`guia-*`) do projeto no modo Simples e alimenta o
  "Pergunte" (respostas citando `FAQ`/`TUT`).
- Ao republicar um documento técnico, a visão prática fica marcada "desatualizada" (lista os IDs de origem que mudaram)
  até a próxima sessão.

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
- Guia antigo (`guia-*`): continua no modo Simples até a visão prática (`pratica`) da ER ser publicada.
- Armadilhas antigas (`090-armadilhas`) e sugestões pendentes: **migração automática** (resposta 1) — quando o módulo
  publica o documento que as cobre, a IA (no Claude Code da sessão, sem custo de API do PRMake) liga cada armadilha/
  sugestão ao item mais provável (`ReverseTrap` ou sugestão por item); o que não tiver item claro fica numa lista
  "sem item" no módulo, e tudo que foi ligado automaticamente aparece marcado para conferência na tela.

## Fora do escopo
- Apagar a base antiga; mudar o Knowledge Center (fonte externa); rodar a ER pelo executor.
- ER dos projetos transversais (`ecossistema`, `operacao-plataforma`, `infra-aws`, `login`, `regras-de-negocio`) —
  **vão ganhar ER própria depois** (resposta 4), numa feature seguinte com modelos de documento próprios; até lá ficam
  na base antiga, e as perguntas práticas que dependem deles (ex.: login) apontam para essa base.

## Respostas do usuário (2026-10-04)
1. Armadilhas e sugestões existentes: migração **automática** (ver "Migração").
2. A visão prática é **exigida** para o módulo ser completo — e só começa com os demais obrigatórios publicados.
3. Seções substituídas ficam **visíveis como histórico** na tela.
4. Projetos transversais **ganham ER própria depois** (feature seguinte). "Isso aqui é muito importante que de fato
   seja muito assertivo, pois é o core da Solvace como um todo" → regras de fonte obrigatória e validação por perguntas
   reais na visão prática (§3).
