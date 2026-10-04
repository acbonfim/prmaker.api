# Feature 0056 — Ajustes da engenharia reversa: tela da Base Solvace e ferramentas da skill (piloto SA3)

> **Status: especificada — não iniciada.** Pedido do usuário em 2026-10-04: gravar numa demanda os pontos que o Claude
> achou no teste da 0054 (tela da Base Solvace) e os problemas das ferramentas relatados pela sessão de engenharia
> reversa do SA3 (`legado-rca`).

## 1. Tela da Base Solvace com o módulo coberto pela engenharia reversa (achados do T1 da 0054)

### 1.1 Guia antigo na árvore do modo Simples
- **Hoje:** com a visão prática publicada, as abas do Simples mostram só ela, mas a árvore lateral continua listando
  as seções `guia-*` substituídas (ex.: "Guia — começo") embaixo do módulo. Ao abrir, aparece a faixa de histórico,
  mas quem não é técnico não entende por que existem duas fontes.
- **Esperado:** no Simples, as seções substituídas saem da árvore, ou ficam num grupo "Histórico" recolhido no fim
  da lista do módulo. Também não aparecem na busca simples da árvore. No Técnico, continuam como hoje: aba apagada
  com a faixa.

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

### 2.3 Traduções não lidas no `re.sh banco` — a diagnosticar
- O `re_banco.py` (`read_interface`) lê `TB_WCM_LANGUAGE` (`TERM_NAME`, `LANG_PORTUGUESE/ENGLISH/SPANISH`) no banco
  global. O legado local (clone de 2026-09-25, `inc_language.asp:474`) ainda usa essa tabela e essas colunas, pelo
  `ConnGlobal`. Ou seja, "não existe mais" pode ser outro erro:
  - tabela movida ou renomeada só na DEMO;
  - permissão do usuário de leitura;
  - limite de linhas (`max_rows=400000`);
  - guarda de somente leitura do `sql-query.sh`.
- **Esperado:**
  1. Diagnóstico com a VPN: o texto exato do `AVISO: traduções (TB_WCM_LANGUAGE) não lidas: …` e
     `sys.tables`/`sys.views` do `DB_DEMO_PRD_GLOBAL` com `%LANG%`.
  2. Fonte das traduções **configurável** (Config no PRMake: chave nova no "Skills Configurations", semeada por
     migração) com tabela e colunas. O padrão continua `TB_WCM_LANGUAGE`.
  3. Se a tabela não existir, a etapa mostra o motivo e o que fazer, e o glossário segue com os rótulos do código
     (`GetLanguageByName`, i18n), como já faz.
  4. Ler só os termos que o código do módulo usa (`WHERE TERM_NAME IN (…)` em lotes), em vez da tabela inteira.

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

## Fora do escopo
- Refazer a engenharia reversa do SA3. O piloto continua pelo "melhorar"; esta demanda só corrige as ferramentas e
  a tela.
- Mudanças no Knowledge Center ou no executor.

## Perguntas em aberto
1. Na árvore do modo Simples (1.1): **esconder** as seções substituídas ou mostrar num grupo **"Histórico"** recolhido?
2. A diagnose da 2.3 precisa da VPN ligada. Rodar no início da execução, ou o usuário já sabe para onde as traduções
   foram?
