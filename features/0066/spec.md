# Feature 0066 — Engenharia reversa mais barata e rápida, integrações claras e Base Solvace por pergunta

Origem: análise de 2026-10-06 (card 75294 + sessões de geração do `legado-checklist`). A engenharia reversa funciona
(no 75294: -37% de tempo, -48% de leitura de cache, -50% de leituras de código), mas gerar custa caro e demora: no
checklist ~145 M de leitura de cache e mais de 1 h por módulo, mesmo com Sonnet. A medição mostrou que o custo vem da
**estrutura** da geração, não do modelo:

- áreas grandes demais: um subagente chegou a 117 chamadas com ~435 mil tokens de contexto (o custo cresce com
  chamadas × contexto — superlinear no tamanho da área);
- uma leitura de arquivo por turno (867 chamadas em 781 turnos; 596 são `grep`/`sed`);
- 40% dos bytes lidos são o mesmo arquivo relido por outro subagente;
- sem checkpoint: 4 subagentes bateram o limite de sessão e foram refeitos do zero;
- prefixo de ~30 mil tokens por subagente (instruções relidas por ferramenta);
- banco e AWS lidos de novo a cada módulo (a AWS leva ~15 min por leitura da conta).

E duas queixas de quem usa a Base Solvace (CS, negócio e analistas):

- a base antiga tinha seções por pergunta (o que é, para quem, dados, integrações…); a engenharia reversa publica
  documentos grandes por tipo de artefato, difíceis de pesquisar para quem não é técnico;
- o mapa de ligações ficou pior: as integrações (`INT`) viram arestas sem validar o campo **Módulos** (texto livre →
  nós lixo como "a", "chave", "helpers"), o tipo da ligação é adivinhado pelo texto e a aresta não leva ao item.

## Objetivo
Custar menos e gerar mais rápido **sem perder qualidade** (de preferência melhorando), integrações entre módulos e
tecnologias claras no mapa, na tela e para as LLMs, e tudo **retrocompatível** (documentos publicados, comandos da
skill, API e executor continuam funcionando).

## Escopo

### A. Geração (skill `engenharia-reversa`)
1. **Áreas por orçamento** (`re.sh areas`): o script divide o inventário em áreas pequenas (orçamento configurável) e
   reserva uma faixa de IDs por área sem colidir com os IDs já usados no módulo.
2. **Pacote de leitura por área** (`re.sh pacote`): um arquivo por área com os trechos do código que importam (o
   método inteiro em volta de cada item do inventário, arquivo pequeno inteiro, com número de linha), as tabelas da
   área (do catálogo do banco) e o cartão de instruções — o subagente lê 2 arquivos em vez de dezenas de `grep`.
3. **Cartão do subagente** (`references/subagente.md` + modelo do documento): instruções curtas, várias leituras por
   resposta, gravação incremental.
4. **Checkpoint e retomada**: o subagente grava a parte a cada ~10 itens; `re.sh faltando` lista o que a parte ainda
   não cobre; retomar continua do arquivo, não do zero.
5. **Modelo e paralelismo por configuração** (`ReverseEngineeringGeneration` no Skills Configurations): modelo dos
   subagentes por documento (padrão `sonnet`), teto de subagentes simultâneos (padrão 3), orçamento da área,
   frequência do checkpoint. A skill lê pelo `GET /ReverseEngineering/settings`.
6. **Retrato do banco e da AWS** (`re.sh retrato banco|infra|status`): baixa uma vez o catálogo inteiro da DEMO
   (objetos, dependências, definições, colunas, chaves, checks, jobs, menus) e a conta da AWS; `re.sh banco` e
   `re.sh infra` usam o retrato enquanto ele estiver dentro da idade máxima (configurável), sem VPN e sem esperar;
   `--ao-vivo` força a leitura direta. Lacunas da coleta (sem permissão no `msdb`, banco sem login) ficam explícitas.
7. **Verificador de evidência** (`re.sh evidencia`, também dentro do `check`): confere que cada `arquivo:linha`
   citado existe nas fontes e que os literais citados aparecem perto da linha — aviso para o revisor (qualidade ↑ e
   segurança para usar modelos mais baratos).
8. **Integrações estruturadas**: o pacote da sessão traz `modulos.tsv` (chaves válidas); `INT` com **Módulos:** só
   com chaves (`legado-x`, `revamp-y`, `ext:<serviço>`), **Mecanismo:** de um vocabulário fechado e "a confirmar"
   numa linha **Confirmar:**; o `check` avisa chave desconhecida.

### B. Backend (Knowledge)
1. **Integrações da engenharia reversa resolvidas**: cada `INT` publicado (qualquer documento) vira ligação com
   destino validado contra as chaves/nomes/apelidos dos projetos (ou `ext:`), tipo pelo **Mecanismo** (com reserva no
   texto) e a referência do item (`modulo#INT-001`). Calculado do índice na hora da leitura — corrige os documentos já
   publicados sem republicar; as relações `re#` antigas gravadas no projeto deixam de ser usadas (os dados ficam).
2. **Mapa e "Com quem conversa"**: arestas com os itens por trás (`items`) e a origem (`codigo`/`engenharia`);
   tipos novos `cache`, `storage`, `job`, `trigger`; nós externos novos (Redis, SNS, Lambda, SQL Agent…).
3. **Checagem**: aviso para **Módulos** não reconhecido e **Mecanismo** ausente/desconhecido em `INT` (aviso, não
   erro — documentos antigos continuam publicáveis).
4. **Configuração** `ReverseEngineeringGeneration` (padrão no código + migração idempotente).
5. **Modelos** (`modelo.md`): formato estruturado do `INT` no levantamento de arquitetura e no funcional.

### C. Front (Base Solvace)
1. **Por pergunta** (página do módulo, modos Simples e Técnico) para CS, negócio e analistas: O que é e para quem ·
   Telas e menus · Quem pode o quê · Regras · Dados e configurações · Com quem conversa · O que roda sozinho ·
   Tecnologias e infraestrutura · Perguntas e passo a passo · Problemas conhecidos — com busca dentro do módulo e o
   texto do item ao clicar. Calculado dos itens publicados (tipo do item), sem LLM.
2. **Mapa**: detalhe da ligação lista os itens da engenharia reversa (clicáveis) e a origem; filtros dos tipos novos.

## Fora de escopo (próximas)
Estudo de duplicação entre documentos; reaproveitar fatos do funcional no design/arquitetura; retrato central no
PRMake (o retrato fica na máquina de quem gera); contrato de design com o Figma na correção de bugs.

## Critérios de aceite
- Documentos publicados continuam com o mesmo índice, busca, MCP e espelho; comandos antigos da skill funcionam.
- O mapa não tem mais nós lixo vindos de `INT`; cada aresta da engenharia reversa leva ao item.
- A página do módulo mostra a navegação por pergunta para módulos com engenharia reversa publicada.
- Piloto: uma área regerada com pacote + checkpoint, comparada com o publicado (cobertura, chamadas, leitura de cache).
