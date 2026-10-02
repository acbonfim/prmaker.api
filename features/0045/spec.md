# Feature 0045 — Base Solvace primeiro: mapa do legado, os dois mundos no contexto e MCP na retomada

## Contexto (avaliação do card 69795)
- A análise **não consultou a Base Solvace**: foi direto a grep em todos os repositórios (3 tentativas, uma parada 2,5 min).
  Das análises desde que a base existe, só 3 abriram alguma seção com `kb.sh`.
- Motivos: (1) o legado `edv-solvace` (onde estão muitos bugs de produção — o 69795 era `Sa3Service.GetRcaAgesData`)
  só tinha uma visão geral de ~2 mil tokens, sem "tela → arquivo"; (2) o `contexto` casou Module=RCA só com o
  `revamp-rca` (mundo errado); (3) o KC trazia artigos sem relação (ruído).
- "8 chamadas pelo script": 4 por desenho (`contexto`, 2× `sync`, `status`) e 4 evitáveis — o texto de retomada do
  PRMake e a SKILL.md mandavam `resume-info`/`notes`/`answers`/`devops config` pelo script, com MCP disponível.

## Objetivo
1. Mapa do legado na base: projetos `legado-<modulo>` (telas → `.asp`/controller → service/SP → tabelas, fluxos,
   armadilhas) e o glossário sigla/pasta → projeto em `edv-solvace/020-modulos`.
2. `kb.sh index` (e o `contexto`) mostra o par do outro mundo (legado ↔ revamp, pela relação "versão nova").
3. Skill: base obrigatória antes da primeira busca; sem o caso → busca só na pasta do módulo + lacuna; KC só com
   artigo que cobre a pergunta (palavra inteira e cobertura mínima).
4. Medir: consultas à base × buscas no código por sessão (skill e executor 1.0.4) — plano e relatório.
5. Retomada e configuração pelo MCP (`prmake_plan`/`prmake_notes`/`prmake_answers`/`prmake_devops_config`), script
   só de reserva.
