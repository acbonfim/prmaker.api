# Feature 0052 — Engenharia reversa por módulo (base primeiro, de verdade)

## Pedido (usuário, 2026-10-04)
> Precisava que a base solvace fosse a consulta principal, os métodos MCP fossem prioridade, mas a aplicação continua
> fazendo análises priorizando código (cards 75091 e 75067: "Base Solvace: 0 consultas · buscas no código: 3/1 — foi
> direto ao código, sem a base").

1. As análises devem usar preferencialmente a Base Solvace; a engenharia reversa deve ser mais profunda e extremamente
   assertiva.
2. Tela nova **Engenharia reversa**: cada módulo (legado ou revamp) separado, cada um com
   - Levantamento funcional
   - Levantamento de arquitetura
   - Especificação de visão
   - Especificação de arquitetura
   - Especificação de design
3. Comandos/skills para rodar no Claude aberto em cada módulo, fazendo cada análise profunda. Ao fim de cada sessão um
   usuário **aprova no PRMake e publica**; só então fica visível e ativo na Base Solvace.
4. Levantar **todas** as regras de negócio, sem exceção, todas as integrações com outros módulos, tecnologias,
   funcionalidades, casos de uso — num nível que dispense ir ao código. Legível para pessoas e para LLM.
5. Integrada à Base Solvace: refazer, melhorar, sugerir melhorias; a Base Solvace consulta essa engenharia reversa.
6. Módulo com engenharia reversa completa → `analisar-bug` prioriza a engenharia reversa, não o código.
7. Sugerir como as skills usam a engenharia de forma rápida, assertiva e barata.
8. Seção de UI/UX: links e arquivos do Figma e protótipo; mapear back e front de cada módulo.
9. Módulos separados mas integrados — tudo isso analisado entre módulos.
10. Índices para achar partes específicas de um módulo ou de vários.

## Diagnóstico (por que a análise ia direto ao código)
- O MCP do PRMake (24 ferramentas, "MCP primeiro" na skill) **não tinha nenhuma ferramenta da Base Solvace**: a base só
  existia por `kb.sh` no Bash — o caminho que a própria skill manda evitar.
- O `contexto` mostrava só a linha do índice (resumo de 180 caracteres); o conteúdo útil exigia um passo a mais
  (`kb.sh show`) que o modelo pulava. Nada obrigava a consultar: era só texto na SKILL.md.
- A base atual é rasa (~3–4 mil tokens por módulo: telas → arquivos, tabelas, armadilhas). Não tem regras de negócio
  completas nem casos de uso — então, mesmo consultada, não respondia e o código era inevitável.
- O contador do executor só reconhecia `kb.sh`; consulta por MCP não contaria.

## Solução (resumo)
- **Documentos por módulo** com ciclo de aprovação: rascunho (sessão do Claude) → em revisão → aprovado → publicado.
  Publicado vira seção `re-<doc>` do projeto na Base Solvace (espelho, índice, busca, Pergunte) — o que já existe passa
  a enxergar a engenharia reversa sem duplicar.
- **Itens com ID estável** dentro dos documentos (`### RN-012 — título`): regras, casos de uso, funcionalidades,
  telas, endpoints, tabelas, eventos, integrações... O PRMake indexa cada item (módulo, documento, tipo, tabelas,
  referências, evidências) → **índice por item**, entre módulos.
- **MCP da base** (`prmake_base_search`, `prmake_base_get`, `prmake_base_module`, `prmake_base_impact`): a análise
  pega só os itens que interessam (centenas de tokens), não o documento inteiro nem o código.
- **`contexto` já traz os itens** do módulo do card (busca pelo título/repro) e registra que o card tem engenharia
  reversa; a etapa `investigar-codigo` só conclui citando um item (`RN-…`, `UC-…`) ou `lacuna:` (configurável).
- **Skill `engenharia-reversa`** (Claude aberto no repositório do módulo): inventário determinístico do código
  (endpoints, validações, tabelas, rotas e componentes do front, eventos, jobs) → documento → checagem de estrutura e
  de **cobertura do inventário** (sem exceção) → envio para aprovação. Modos novo, melhorar (usa sugestões, lacunas e o
  pedido de ajustes do revisor) e refazer.
- **UI/UX**: links do Figma/protótipo e arquivos por módulo; o documento de UI/UX mapeia tela → componente do front →
  endpoint → tabela.

## Fora do escopo
- Rodar a engenharia reversa pelo executor (sem terminal) — pode vir depois; aqui é no Claude aberto no módulo.
- Gerar o conteúdo dos ~90 módulos (operação depois do deploy, módulo a módulo, com aprovação).
