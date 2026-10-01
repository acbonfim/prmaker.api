# Operação e configuração (0040) — mapear e resolver perguntas

Objetivo: a Base Solvace responder perguntas de **operação** de qualquer módulo — como habilitar na planta, como dar
acesso, onde se configura (cadastros, tipos, parâmetros, notificações), quem pode fazer o quê e por que algo não
aparece. Leia só a parte que vai usar.

## Onde fica cada coisa
- `operacao-plataforma` (projeto transversal): o que vale para todos os módulos — seção técnica `operacao` (habilitar
  módulo na planta, menu/Home, perfis e papéis, módulos privados, parâmetros e cache, troca de planta, Global × Local,
  diagnóstico "não aparece" com SQL), `catalogo-modulos` (todas as aplicações, alias, versão e telas das que não têm
  projeto) e `parametros` (globais e de planta, com descrição). Guia: `guia-como-configurar`.
- Cada módulo: seção técnica `operacao` (085, "Configuração e operação" — telas do menu com rota e quem vê, papéis,
  parâmetros por planta, versões nova e legada) e Guia `guia-como-configurar` (545, passo a passo para suporte/QA).

## Mapear (parque inteiro) — barato primeiro
1. Catálogo (sem LLM, só leitura): `python3 ~/.claude/skills/base-solvace/scripts/operacao.py catalogo --host <alias>
   --db <DB_..._GLOBAL> --out catalogo.json` — use um banco de **teste/sandbox** (o catálogo de menus/papéis/parâmetros
   é do produto; nunca lê valores de parâmetro). Credencial: `prmake-skills.sh db-credentials list`.
2. Seções: `curl .../Architecture/projects > projetos.json` e `operacao.py secoes catalogo.json <saida> --projetos
   projetos.json --fonte "<ambiente>"` → `<saida>/<projeto>/085-operacao.md` e, no `operacao-plataforma`,
   `090-catalogo-modulos.md` / `095-parametros.md`. Publique com `arch.sh section <projeto> operacao <arquivo> --title
   "Configuração e operação" --order 85` (o `operacao-plataforma` precisa existir: `arch.sh project` com
   `--kind business-rules`). Aplicação nova sem projeto → entra no catálogo; com projeto → acrescente o alias em
   `ALIAS_TO_PROJECT` (`operacao.py`).
3. Guia: com a seção `operacao` publicada, `arch.sh guia <projeto> <pasta> --instructions "Gere SOMENTE a seção
   guia-como-configurar ..."` e publique o `545-guia-como-configurar.md` com `--audience human`.
4. O que o catálogo não diz (o que cada tela faz, regras de permissão no código, notificações por evento): leia o
   código apontado pela rota (`Angular <app> <rota>` → `edv-solvace-apps/projects/<app>`; legado `.asp`/core) com um
   subagente e acrescente na seção `operacao` com `arquivo:linha`.

## Resolver perguntas sem resposta (fila do "Pergunte")
1. `bash $ARCH perguntas` — as mais perguntadas primeiro (`not-found`/`partial`).
2. Para cada uma: é de qual módulo? A resposta vale para todos (→ `operacao-plataforma`) ou para um (→ `operacao` do
   módulo)? Investigue no código (subagente Explore — tela, endpoint, tabela, perfil, `arquivo:linha`; SQL só
   leitura); confira o KC (`bash $KC search`).
3. Escreva o trecho na seção certa (técnica: objetivo, com caminhos e tabelas; Guia: passo a passo pelo nome das
   telas) — acrescente, não apague o que está certo — e publique (`arch.sh section ...`).
4. `bash $ARCH pergunta-respondida <id> <projeto> <secao> "<o que publicou>"`. Fora do escopo:
   `pergunta-descartar <id> "<motivo>"`. Pergunte de novo na tela para conferir.

## Regras
- Nada inventado: o que o código não confirma vira "a confirmar". Nunca credenciais nem valores de parâmetro.
- Achado de segurança (ex.: endpoint que só o menu protege) → registre em `armadilhas` do módulo e avise o usuário.
