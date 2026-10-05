# Feature 0056 — Ajustes da engenharia reversa: tela da Base Solvace e ferramentas da skill (piloto SA3)

> **Status: implementada em 2026-10-04** (ver `status.md`). Pedido do usuário: gravar numa demanda os pontos que o
> Claude achou no teste da 0054 (tela da Base Solvace) e os problemas das ferramentas relatados pela sessão de
> engenharia reversa do SA3 (`legado-rca`). Pendente: credencial/host reais do Multilingual da DEMO.

## 1. Tela da Base Solvace com o módulo coberto pela engenharia reversa (achados do T1 da 0054)

### 1.1 Guia antigo na árvore do modo Simples
- **Hoje:** com a visão prática publicada, as abas do Simples mostram só ela, mas a árvore lateral continua listando
  as seções `guia-*` substituídas (ex.: "Guia — começo") embaixo do módulo. Ao abrir, aparece a faixa de histórico,
  mas quem não é técnico não entende por que existem duas fontes.
- **Esperado (resposta 1):** no Simples, as seções substituídas vão para um grupo **"Histórico" recolhido** no fim da
  lista do módulo (fechado por padrão, com a contagem). Ao abrir uma delas, aparece a faixa de histórico com o link
  para a engenharia reversa. Elas não aparecem na busca simples da árvore. No Técnico, continuam como hoje: aba
  apagada com a faixa.

### 1.2 Armadilhas novas na página do módulo
- **Hoje:** as armadilhas ligadas aos itens (`ReverseTrap`, 0054) aparecem na tela da Engenharia reversa, no MCP,
  no `for-card` e no espelho (`090-armadilhas.md`). Na Base Solvace, a página do módulo mostra só a seção antiga
  `armadilhas`, que depois da migração vira histórico. As novas não aparecem ali.
- **Esperado:** no modo Técnico, uma aba "Armadilhas" gerada das armadilhas do módulo, igual ao espelho:
  - título;
  - "a conferir" quando for o caso;
  - itens com link para o item na engenharia reversa;
  - cards e texto.

  Sem armadilhas, a aba não aparece. É só leitura: conferir e remover continuam na tela da Engenharia reversa, com
  um link para lá. Precisa de um endpoint de leitura das armadilhas por projeto que já venha no detalhe do projeto,
  ou de reuso do `GET ReverseEngineering/traps?module=`.

## 2. Ferramentas da skill `engenharia-reversa` (relatado pela sessão do SA3)
Avisos da sessão (texto do usuário):
> - A checagem ignora termos com menos de 3 letras. Por isso "A3" aparece como faltando, mesmo estando no GLO-001.
> - Termos listados numa lacuna não contam como cobertos, ao contrário do que diz o guia do banco.
> - re.sh banco busca a tabela TB_WCM_LANGUAGE, que não existe mais, e não encontrou as traduções.
> - O --path do inventário só aceita pastas. Os arquivos .NET do helpers (Sa3Service, FishBoneService, WhyWhyService)
>   eu conferi lendo à mão.

### 2.1 Termo curto ("A3") nunca conta como coberto — confirmado
- `re_tool.py` → `needles()` termina com `return [n for n in out if len(n) >= 3]`. O termo `A3` fica sem nenhuma
  agulha e nunca casa, mesmo estando no título do `GLO-001`.
- O mesmo corte de 3 letras existe em `words()` (cobertura de perguntas reais da visão prática, `[a-z0-9]{3,}`) e
  no filtro do `kb.sh index <termos>`. "kb.sh index a3" não acha nada.
- **Esperado:**
  - termo de 2 letras conta quando tem dígito ou é sigla em maiúsculas (`A3`, `5S`, `OK` fica nas exclusões);
  - a comparação continua por palavra inteira (`contains` com bordas), para "a3" não casar dentro de "ba3x";
  - o `kb.sh index` aceita siglas de 2 caracteres com dígito.

### 2.2 Termo listado numa lacuna não conta — confirmado (contradiz o guia)
- `references/banco.md` diz que o termo fora do domínio (ou de outro módulo) vai num `GAP-…` "termos fora do
  glossário" com o motivo, e que conta como coberto.
- Mas `coverage()` só procura termos no texto dos `GLO` (`glossary_text`: títulos e linhas de sinônimos).
- **Esperado:**
  - `glossary_text` passa a incluir os `GAP` cujo título fale de termos fora do glossário (ex.: "termos fora do
    glossário", "termos de outro módulo"), ou que tenham uma linha `**Termos:**`;
  - o guia diz exatamente o formato aceito;
  - na revisão, esses termos aparecem como "fora do glossário (GAP-…)", não como cobertos sem explicação.

### 2.3 Traduções: vêm do Multilingual do revamp, não do `TB_WCM_LANGUAGE`
- **Hoje:** o `re_banco.py` (`read_interface`) lê `TB_WCM_LANGUAGE` (`TERM_NAME`, `LANG_PORTUGUESE/ENGLISH/SPANISH`) no
  SQL Server global da DEMO e não acha as traduções.
- **Resposta do usuário:** as traduções são do módulo **Multilingual**, que agora fica no **revamp**, num **PostgreSQL
  separado**. O `TB_WCM_LANGUAGE` é legado. No `revamp-multilingual` ele só aparece como entidade `LanguageLegacy` de
  migração, e o `inc_language.asp` local ainda o lê, o que explica a confusão.
- **Fonte nova** (Base Solvace, `revamp-multilingual`, `Infra.Data.AuroraPostgreSQL`): tabelas `term_key` (o termo),
  `translation` (texto por idioma), `language`, `term_key_application` (termo ↔ aplicação/módulo) e
  `custom_translation` (tradução customizada por cliente).
- **Esperado:**
  1. **Leitura do Multilingual no Postgres** no mesmo modelo do Knowledge Center (0033):
     - credencial só local, em `~/.claude/multilingual-credentials.json`, um bloco por ambiente; nunca impressa;
     - sessão `default_transaction_read_only=on`;
     - driver `psycopg` do venv que a skill `base-solvace` já instala.
  2. **Ambiente, host, database e schema configuráveis** no PRMake: chave nova no "Skills Configurations", semeada por
     migração, sem segredo. O ambiente de referência acompanha a `ReverseEngineeringReferenceDatabase` (DEMO).
  3. **Só os termos do módulo:** pela `term_key_application` da aplicação do módulo (a sigla, que o `re_banco` já
     acha no `TB_SYS_Application`) mais os termos que o código cita (`GetLanguageByName`, chaves de i18n), em lotes.
     Nada de ler a tabela inteira.
  4. **Glossário:** pt/en/es de cada termo entram como sinônimos sugeridos; a tradução customizada do cliente
     (`custom_translation`) fica de fora, porque a referência é o produto. O `inventario-termos.json` passa a citar a
     fonte (`multilingual`).
  5. **Sem acesso** (sem credencial ou fora da VPN): a etapa "Banco de dados (DEMO)" mostra o motivo e o que fazer,
     e o glossário segue com os rótulos do código, como já faz. Sai a leitura do `TB_WCM_LANGUAGE`; se o legado
     ainda tiver termos só lá, isso vira item à parte, decidido no T1.
  6. **`references/banco.md` e SKILL.md:** de onde vêm as traduções e como pedir a credencial.

### 2.4 `--path` só aceita pastas — confirmado
- `re.sh resolve_sources` (`[[ -d "$dir" ]]`, linhas 112 e 119) e o `re_tool.py inventario` (`os.path.isdir`,
  linha 640) recusam arquivo. Os serviços .NET do SA3 ficam em `helpers/` junto com os de outros módulos
  (`Sa3Service`, `FishBoneService`, `WhyWhyService`). Apontar a pasta inteira traz o que não é do módulo, e não
  apontar deixa os arquivos fora da cobertura. Hoje a sessão confere à mão.
- **Esperado:**
  - `--path repo=caminho` aceita arquivo e padrão glob (`helpers/**/Sa3*.cs`, `helpers/Controllers/FishBone*.cs`),
    várias vezes;
  - o inventário e o `trabalho` (diff por commit) tratam arquivo e glob como fonte;
  - as fontes do módulo na tela (`ReverseModule.Sources`) aceitam o mesmo formato, gravado pelo aprovador.

## 3. Correções de tela pedidas pelo usuário (2026-10-04)

### 3.1 Glossário recolhido por padrão
- **Hoje:** no módulo, o painel "N termos do glossário ainda não são apelido nem palavra-chave" abre sozinho para quem
  aprova (`reverse-engineering.component.html`, `<details class="terms" [open]="m.canApprove">`). Com ~300 termos, os
  chips ocupam a tela inteira antes das abas do documento. O grupo "Glossário" do sumário lateral também lista todos os
  `GLO` sem recolher.
- **Esperado:**
  - o painel de termos vem **sempre recolhido**, com a contagem no título e um tooltip ("Abra para ver os N termos e
    decidir apelido, palavra-chave ou dispensar");
  - o estado aberto/fechado fica lembrado por usuário (localStorage);
  - no sumário lateral, o grupo "Glossário" vem recolhido com a contagem e abre ao clicar. Ir a um `GLO` pelo link
    (`?i=GLO-012`) abre o grupo.

### 3.2 Botão de copiar nos comandos do "Como gerar"
- **Hoje:** só os comandos de instalação e de repositórios têm o botão de copiar (`re-how-to.component.ts`, `.cmd`). Os
  comandos de gerar e melhorar cada documento (`/engenharia-reversa <módulo> <doc>`, `… tudo`, `… melhorar <doc>`) e os
  das telas de documento (aviso "desatualizada", visão prática bloqueada, documento não gerado) são texto solto.
- **Esperado:** todo comando exibido para o usuário rodar no Claude tem o botão de copiar, no mesmo componente `.cmd`
  (código + ícone, tooltip "Copiar", aviso "Copiado"). Um componente único `app-copy-command` é usado no "Como gerar",
  nos avisos da aba do documento e no painel da sessão.

### 3.3 Tabelas em markdown espremidas (uma letra por linha)
- **Hoje:** em tabelas largas do documento (ex.: rastreabilidade do Registro do A3, colunas `TELA | Componente/trecho do
  front | Chamada (JS → URL/hdnAction) | API | Tabelas (DB)`), as colunas encolhem até caber uma letra e o texto quebra
  letra a letra. **Causa:** o container do documento usa `overflow-wrap: anywhere` (`.md` em
  `reverse-engineering.component.css` e `.kb__md` em `architecture.component.css`), que zera a largura mínima das
  células. A tabela já tem rolagem horizontal (`display: block; overflow-x: auto`), mas nunca precisa dela, porque as
  colunas aceitam ficar com 1 caractere.
- **Esperado, nas duas telas (Engenharia reversa e Base Solvace) e no preview da revisão:**
  - células com `overflow-wrap: normal` e `word-break: normal` (quebra só entre palavras — nunca "anywhere", que
    reduz a largura mínima da coluna a 1 caractere), com uma largura mínima por coluna (~9ch);
  - **código/identificador inline nunca quebra** (`white-space: nowrap` no `code`) — um token sem espaços (caminho,
    `hdnAction=…`) forçando quebra no meio da palavra ficava tão ruim quanto o bug original; sem couber, é a
    **tabela** que rola na horizontal (já tinha `overflow-x: auto`, só nunca precisava por causa do bug);
  - cabeçalho sem quebra (`white-space: nowrap` no `th`) e sem a quebra estranha de antes.
- **Validado** (antes de aplicar) com uma página HTML isolada reproduzindo a tabela do Registro do A3 citada pelo
  usuário, em duas larguras — confirmou que a primeira tentativa (`overflow-wrap: break-word` + pontos de quebra
  extras) ainda forçava quebra no meio de identificadores longos; a versão final (`nowrap` no `code`) não quebra
  nenhuma palavra, só ativa a rolagem da tabela quando preciso.
- **Teste:** o documento real do SA3 com a tabela de rastreabilidade, em 1366px e 1920px, com print.

## Fora do escopo
- Refazer a engenharia reversa do SA3. O piloto continua pelo "melhorar"; esta demanda só corrige as ferramentas e
  a tela.
- Mudanças no Knowledge Center ou no executor.

## Respostas do usuário (2026-10-04)
1. Árvore do modo Simples: seções substituídas num grupo **"Histórico" recolhido**.
2. Traduções: são do módulo **Multilingual**, que fica **no revamp**, num **PostgreSQL separado** (não no
   `TB_WCM_LANGUAGE`).

## Perguntas em aberto
1. Multilingual da DEMO: qual cluster/host e database (o mesmo Aurora do Knowledge Center, `solvace-pstgdev`, ou
   outro), e quem fornece a credencial de leitura para `~/.claude/multilingual-credentials.json`?
