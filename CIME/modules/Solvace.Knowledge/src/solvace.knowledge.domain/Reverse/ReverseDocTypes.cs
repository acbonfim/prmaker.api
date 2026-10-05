namespace solvace.knowledge.domain.Reverse;

/// <summary>Cabeçalho <c>##</c> obrigatório num documento: título sugerido e o trecho (sem acento/caixa) que o identifica.</summary>
public sealed record ReverseHeading(string Title, string Match);

/// <summary>
/// Um documento da engenharia reversa de um módulo (0052). Publicado, vira a seção <see cref="SectionKey"/> do projeto
/// na Base Solvace (espelho, índice, busca). <see cref="Kinds"/> são os itens que ele define (<c>GAP</c> vale em todos).
/// </summary>
/// <param name="Audience">llm (técnico, vai para as análises) | human (0054: a visão prática — Simples, busca de pessoas, Pergunte).</param>
public sealed record ReverseDocType(string Key, string Title, string SectionKey, int Order, IReadOnlyList<string> Kinds,
    IReadOnlyList<ReverseHeading> Headings, string Purpose, string Template, string Audience = "llm")
{
    /// <summary>Documento derivado só do que já foi publicado (0054: visão prática) — depende dos demais exigidos.</summary>
    public bool Derived => Key == ReverseDocTypes.Practical;
}

public static class ReverseDocTypes
{
    public const string SectionPrefix = "re-";

    /// <summary>0054: a visão prática (não técnica), gerada do que foi publicado — último documento.</summary>
    public const string Practical = "pratica";

    /// <summary>Padrão de documentos exigidos para o módulo contar como "completo" (a config pode mudar).</summary>
    public static readonly IReadOnlyList<string> DefaultRequired = ["funcional", "arquitetura", "visao", "spec-arquitetura", "design", Practical];

    private const string ItemRules = """
        ## Como escrever um item (vale para todo o documento)
        - Cada coisa que alguém pode procurar é um **item com ID**: cabeçalho `###` começando pelo ID, travessão e o
          título — `### RN-012 — Etapa só avança com todos os campos obrigatórios`. O ID é **único no módulo** (entre
          todos os documentos) e **estável**: ao melhorar/refazer, mantenha o ID do mesmo assunto; item que deixou de
          existir fica como `### RN-012 — (removido) <motivo>`. Nunca renumere.
        - Linhas de metadados logo abaixo do cabeçalho (o índice do PRMake lê): `- **Onde:** `arquivo:linha`, …`
          (evidência no código — obrigatória em RN, UC, API, DB, EVT, JOB, INT e TELA), `- **Tabelas:** TB_…`,
          `- **Módulos:** revamp-users, legado-usuarios` (outros módulos envolvidos — chave do projeto na Base Solvace),
          `- **Tags:** termos que alguém usaria para procurar (PT e EN, nomes de tela, siglas)`, `- **KC:** ART-n`,
          `- **Sinônimos:** …` (nos `GLO`: todos os nomes do mesmo conceito — a busca das análises usa).
        - Regra ou comportamento que vive **no banco** (procedure, view, function, trigger, job): a evidência é o banco
          da DEMO — `- **Onde:** banco DEMO <global|local> · dbo.STP_X (linha 42)` — e o item diz em qual banco está.
        - Referencie outros itens pelo ID (`RN-012`, `TELA-003`) e itens de outro módulo por `<módulo>#<ID>`
          (`revamp-users#API-004`).
        - **Literal e exato**: valores, limites, mensagens de erro como estão no código (entre aspas), nomes de campos,
          status e enums com os valores reais. Nada de "valida alguns campos" — liste quais e como.
        - Não sabe? Escreva "a confirmar" e registre um `GAP`. **Nunca invente.** Nunca credenciais, connection strings,
          tokens ou dados de cliente — só nomes de recursos e chaves.
        - Diagramas em ```mermaid``` (pequenos e corretos).
        """;

    public static readonly IReadOnlyList<ReverseDocType> All =
    [
        new("funcional", "Levantamento funcional", "re-funcional", 110,
            ["FN", "UC", "RN", "PRF", "EST", "NTF", "CFG", "REL", "GLO"],
            [
                new("Resumo do módulo", "resumo"),
                new("Perfis e permissões", "perfis"),
                new("Funcionalidades", "funcionalidades"),
                new("Casos de uso", "casos de uso"),
                new("Regras de negócio", "regras de negocio"),
                new("Estados e ciclo de vida", "estados"),
                new("Notificações", "notificac"),
                new("Configurações e parâmetros", "configurac"),
                new("Relatórios e indicadores", "relatorios"),
                new("Integrações com outros módulos", "integrac"),
                new("Glossário", "glossario"),
                new("Lacunas e pontos a confirmar", "lacunas")
            ],
            "O que o módulo faz, para quem, com TODAS as regras de negócio e casos de uso — em nível que dispensa abrir o código.",
            """
            # Levantamento funcional — <módulo>

            ## Resumo do módulo
            O que é, para quem, problema que resolve, principais telas e o ciclo de vida da entidade principal em 10–15
            linhas. Diga se é legado ou revamp e qual é o par do outro mundo.

            ## Perfis e permissões
            `### PRF-001 — <perfil ou permissão>` — quem é, o que pode ver/fazer em cada tela e ação, onde é checado
            (back e front), como se concede (tela/papel/parâmetro).

            ## Funcionalidades
            `### FN-001 — <funcionalidade>` — o que faz para o usuário, telas (TELA-…), casos de uso (UC-…), regras
            (RN-…), perfis (PRF-…). Uma por funcionalidade que o usuário reconhece (cadastrar, aprovar, exportar, filtrar…).

            ## Casos de uso
            `### UC-001 — <ator> <objetivo>` com: **Ator**, **Pré-condições**, **Fluxo principal** numerado (cada passo
            com a TELA, a API e as RN que se aplicam), **Fluxos alternativos e de erro** (mensagens literais),
            **Pós-condições** (o que fica gravado, quem é notificado, que evento sai). Cubra criar, editar, excluir,
            aprovar/reprovar, transições de status, importação/exportação, ações em lote e o que roda sozinho.

            ## Regras de negócio
            **Todas, sem exceção** — cada validação do back e do front, cálculo, prazo, obrigatoriedade condicional,
            restrição por perfil/status/planta, regra de visibilidade, unicidade, valor padrão, arredondamento, fuso.
            `### RN-001 — <regra numa frase>` com **Onde** (back e front quando houver os dois), **Quando** (gatilho),
            **Regra** (condição → resultado, valores exatos, mensagem literal), **Exceções**, Telas/Tabelas/Módulos/Tags,
            **KC** (ART-n quando o Knowledge Center documenta). Agrupe por assunto com `####` se ajudar — o ID continua no
            cabeçalho do item. Regra só no front (o back não valida) ou divergente entre front e back: diga explicitamente.

            ## Estados e ciclo de vida
            `### EST-001 — <entidade>`: tabela de estados (valor real no banco, nome na tela), transições (de → para,
            quem, condição, RN), diagrama ```mermaid stateDiagram-v2```.

            ## Notificações
            `### NTF-001 — <notificação>`: gatilho, destinatários (regra exata), canal (e-mail, push, sino, Teams),
            modelo/assunto, onde se configura, por onde sai (fila/serviço).

            ## Configurações e parâmetros
            `### CFG-001 — <parâmetro ou cadastro base>`: onde se configura (tela/menu), escopo (global, planta, área),
            valor padrão, efeito em cada regra (RN-…), cache.

            ## Relatórios e indicadores
            `### REL-001 — <relatório, gráfico ou KPI>`: fórmula exata, filtros, fonte (tabelas/views/SP), exportação.

            ## Integrações com outros módulos
            Visão funcional: o que este módulo usa de outros (usuários, masterdata, plano de ação, notificações…) e o que
            fornece — cada linha apontando o `INT-…` do levantamento de arquitetura e o módulo (`**Módulos:**`).

            ## Glossário
            **Todos os termos do módulo**, sem número fixo: cada conceito que aparece na tela (rótulos, títulos, menus,
            status, tipos), nas siglas e nos nomes de tabela/objeto. `### GLO-001 — <termo como o usuário vê>` com
            **Sinônimos** (todos os nomes do mesmo conceito: sigla, nome antigo, PT/EN/ES das traduções, nome da tabela —
            ex.: `SA3, A3, RCA, RCA 1-pager, root cause analysis, TB_SA3_A3`), significado no domínio e onde aparece
            (TELA-…, tabelas). A lista de termos vem do inventário (traduções, menus, siglas); termo genérico de interface
            fica fora; termo deixado de fora de propósito vai justificado em `GAP`.

            ## Lacunas e pontos a confirmar
            `### GAP-001 — …`: o que não foi possível confirmar no código, contradições, código morto, comportamento suspeito.
            """),

        new("arquitetura", "Levantamento de arquitetura", "re-arquitetura", 120,
            ["TEC", "CMP", "API", "DB", "EVT", "JOB", "INT", "CFG", "SQL", "TRG", "INF"],
            [
                new("Visão técnica", "visao tecnica"),
                new("Tecnologias e versões", "tecnologias"),
                new("Componentes e camadas", "componentes"),
                new("Endpoints e contratos", "endpoints"),
                new("Dados — tabelas e entidades", "tabelas"),
                new("Eventos, filas e mensagens", "eventos"),
                new("Jobs e rotinas", "jobs"),
                new("Banco de dados: views, procedures, functions, triggers e jobs", "banco de dados"),
                new("Integrações", "integrac"),
                new("Configuração e segredos (só nomes)", "configurac"),
                new("Segurança e autenticação", "seguranca"),
                new("Observabilidade e diagnóstico", "observabilidade"),
                new("Lacunas e pontos a confirmar", "lacunas")
            ],
            "Como o módulo é construído: tecnologias, componentes, cada endpoint, cada tabela, eventos, jobs e cada integração com evidência.",
            """
            # Levantamento de arquitetura — <módulo>

            ## Visão técnica
            Repositórios e pastas (back, front, banco), como sobe (host, porta, pipeline, ambientes), camadas, fluxo de
            uma requisição da tela até o banco (diagrama ```mermaid```).

            ## Tecnologias e versões
            `### TEC-001 — <tecnologia> <versão>` — onde é usada, por quê, pacote/arquivo de onde veio a versão.

            ## Componentes e camadas
            `### CMP-001 — <componente>` — projeto/pasta, responsabilidade, classes de entrada, dependências.

            ## Endpoints e contratos
            **Todos os endpoints** (e telas .asp/ações MVC no legado). `### API-001 — <VERBO> <rota>` com **Onde**
            (controller:linha → service → repositório/SP), **Autorização** (perfil/claim), **Entrada** (campos que
            importam, obrigatórios, tipos), **Saída**, **Regras** (RN-…), **Tabelas** lidas/gravadas, **Eventos** que
            publica, **Quem chama** (TELA-… e outros módulos).

            ## Dados — tabelas e entidades
            **Todas as tabelas/coleções** do módulo. `### DB-001 — <TB_…>` com banco/schema (global × local por planta),
            colunas que importam (nome, tipo, significado, valores de status/enums), chaves e índices, soft delete,
            quem grava (API/JOB), quem lê (inclusive outros módulos), diagrama ER quando ajudar.

            ## Eventos, filas e mensagens
            `### EVT-001 — <tópico/fila/evento>`: produtor, consumidores (módulos), contrato da mensagem, retry/DLQ, o que
            acontece se falhar.

            ## Jobs e rotinas
            `### JOB-001 — <job/worker/Lambda/SP agendada>`: agendamento, o que faz passo a passo, tabelas, falhas comuns.

            ## Banco de dados: views, procedures, functions, triggers e jobs
            Lido **direto do banco da DEMO** (global e locais — o catálogo da sessão), nunca de scripts versionados.
            **Todos os objetos do módulo**: `### SQL-001 — <view|procedure|function> dbo.<nome>` e
            `### TRG-001 — <trigger> em <tabela> (<INSERT|UPDATE|DELETE>)`, com **Banco** (DEMO global/local, igual nos
            locais ou divergente, data da última alteração), **o que faz passo a passo** (a regra está no corpo: condições,
            cálculos, valores), tabelas lidas/gravadas, **quem chama** (TELA/API/JOB/outro objeto/trigger), regras que
            aplica (RN-…), efeitos colaterais (o que o trigger faz "escondido") e objetos de **outros módulos** que usam
            as tabelas deste (`**Módulos:**`). Jobs do SQL Agent que tocam o módulo entram como `JOB-…` (agenda, passos,
            comando resumido — sem credencial).

            ## Infraestrutura e AWS (opcional)
            Só quando a etapa de infra foi executada (`re.sh infra` — lê a AWS pelo CLI, somente leitura; sem ela, deixe a
            seção de fora e registre `GAP` "infra não lida"). Um item por recurso do módulo:
            `### INF-001 — <serviço> <nome>` (Lambda, bucket S3, esteira CodePipeline/CodeBuild, fila SQS, tópico SNS, regra
            do EventBridge, banco RDS, grupo de log, segredo...) com **Onde:** `aws <conta>/<região> · <serviço>:<nome>`,
            para que serve, quem usa/aciona (JOB/API/EVT/INT), configuração relevante (runtime, handler, timeout, agenda,
            DLQ, retenção), **esteira de deploy** (repositório → branch → build → deploy → ambiente; arquivos `.github/
            workflows`/`buildspec` com `arquivo:linha`), **segredos consultados** (só o NOME no Secrets Manager e quem lê) e
            **onde ver os logs** (grupo do CloudWatch + comando `aws logs tail`). Nunca valores de segredo, senhas ou
            endpoints com credencial.

            ## Integrações
            **Cada** integração com outro módulo Solvace ou serviço externo, nos dois sentidos. `### INT-001 — <este> →
            <outro>: <para quê>` com **Módulos** (chave do outro projeto), **Mecanismo** (HTTP, SNS/SQS, banco
            compartilhado, pacote, arquivo), **Contrato** (rota/tópico/tabela, campos), **Onde** (evidência dos dois
            lados quando houver), **Se falhar**. Diagrama ```mermaid``` com todas as integrações.

            ## Configuração e segredos (só nomes)
            Chaves de appsettings/variáveis/parâmetros por NOME e para que servem (`### CFG-…` só se for configuração
            técnica que muda comportamento). Nunca valores secretos.

            ## Segurança e autenticação
            Como autentica (Cognito/JWT/sessão ASP), claims usadas, multi-tenant (planta/ambiente), onde cada perfil é checado.

            ## Observabilidade e diagnóstico
            Logs (onde — grupo do CloudWatch e como consultar, ver `INF-…` quando a infra foi mapeada —, que mensagens), métricas, consultas SQL úteis de diagnóstico (somente leitura), erros conhecidos.

            ## Lacunas e pontos a confirmar
            `### GAP-001 — …`
            """),

        new("uiux", "UI/UX — telas, back × front", "re-uiux", 125,
            ["TELA", "FLX"],
            [
                new("Mapa de telas", "mapa de telas"),
                new("Telas", "telas"),
                new("Fluxos de navegação", "fluxos"),
                new("Back × front — rastreabilidade", "rastreabilidade"),
                new("Figma e protótipos", "figma"),
                new("Lacunas e pontos a confirmar", "lacunas")
            ],
            "Cada tela do módulo mapeada do front ao banco, com o que o Figma/protótipo mostra e onde diverge.",
            """
            # UI/UX — telas, back × front — <módulo>

            ## Mapa de telas
            Menu → telas (árvore), rota de cada uma, quem vê (PRF-…). Diagrama ```mermaid``` da navegação.

            ## Telas
            **Todas as telas, modais e abas.** `### TELA-001 — <nome que o usuário vê>` com **Rota/URL** (e a `.asp`
            no legado), **Onde** (componente do front, template, serviço), **Quem vê** (PRF-…), **Campos** (rótulo, tipo,
            obrigatório, máscara/limite, validação no front, RN-…), **Ações/botões** → API-… (e o que acontece na tela:
            mensagens literais), **Estados** (vazio, carregando, erro, sem permissão), **Chaves de tradução** principais,
            **Figma** (link do anexo, quando houver).

            ## Fluxos de navegação
            `### FLX-001 — <fluxo do usuário>`: passos de tela em tela, com os casos de uso (UC-…) que realiza.

            ## Back × front — rastreabilidade
            Tabela: TELA → componente do front → serviço/chamada HTTP → API-… → tabelas (DB-…). Toda chamada HTTP do front
            do módulo aparece aqui; chamada para API de outro módulo cita `<módulo>#API-…`.

            ## Figma e protótipos
            Para cada anexo (link do Figma, protótipo, imagem): o que mostra, quais TELA-… cobre, divergências entre o
            protótipo e o implementado (cada divergência relevante como `GAP-…`).

            ## Lacunas e pontos a confirmar
            `### GAP-001 — …`
            """),

        new("visao", "Especificação de visão", "re-visao", 130,
            ["OBJ", "PER"],
            [
                new("Propósito e problema", "proposito"),
                new("Objetivos e escopo", "objetivos"),
                new("Personas e usuários", "personas"),
                new("Jornada e valor para o negócio", "jornada"),
                new("Legado × revamp", "legado"),
                new("Glossário", "glossario"),
                new("Métricas de sucesso", "metricas"),
                new("Lacunas e pontos a confirmar", "lacunas")
            ],
            "Para que o módulo existe, para quem, o que está dentro e fora do escopo, e o vocabulário do domínio.",
            """
            # Especificação de visão — <módulo>

            ## Propósito e problema
            Problema de negócio que o módulo resolve (metodologia WCM/TPM/lean quando for o caso), em linguagem simples.

            ## Objetivos e escopo
            `### OBJ-001 — <objetivo ou limite de escopo>`: dentro do escopo, fora do escopo (e onde isso é feito), FN-…
            que o realizam.

            ## Personas e usuários
            `### PER-001 — <persona>`: quem é (cargo/área), o que faz no módulo, com que frequência, perfis (PRF-…).

            ## Jornada e valor para o negócio
            A jornada principal ponta a ponta (FLX/UC), o valor entregue, dependências de outros módulos para a jornada.

            ## Legado × revamp
            O que existe em cada mundo, o que mudou de comportamento, o que ainda não foi migrado, convivência dos dois.

            ## Glossário
            O glossário do módulo fica no levantamento funcional (`GLO-…`, com sinônimos): aqui só os termos do negócio que
            a visão precisa explicar, **referenciando** os `GLO` (não redefina o ID).

            ## Métricas de sucesso
            Indicadores que mostram que o módulo funciona (REL-…), metas quando conhecidas ("a confirmar" se não).

            ## Lacunas e pontos a confirmar
            `### GAP-001 — …`
            """),

        new("spec-arquitetura", "Especificação de arquitetura", "re-spec-arquitetura", 140,
            ["ADR", "NFR", "SEQ", "GAP"],
            [
                new("Visão de contexto", "contexto"),
                new("Contêineres e componentes", "conteineres"),
                new("Fluxos de sequência", "sequencia"),
                new("Modelo de dados", "modelo de dados"),
                new("Decisões de arquitetura", "decisoes"),
                new("Requisitos não funcionais", "nao funcionais"),
                new("Débitos, riscos e lacunas", "debitos")
            ],
            "A arquitetura especificada a partir do levantamento: contexto C4, sequências dos fluxos críticos, modelo de dados, decisões e NFRs.",
            """
            # Especificação de arquitetura — <módulo>

            ## Visão de contexto
            C4 nível 1 em ```mermaid```: o módulo, os usuários, os outros módulos Solvace e os serviços externos (cada seta
            com o INT-… do levantamento).

            ## Contêineres e componentes
            C4 níveis 2–3: front, API, workers, Lambdas, bancos, filas — responsabilidades e tecnologias (TEC-…, CMP-…).

            ## Fluxos de sequência
            `### SEQ-001 — <fluxo crítico>`: ```mermaid sequenceDiagram``` da tela ao banco e às integrações, com as RN-…
            aplicadas em cada passo e o que acontece em erro. Cubra os UC principais e tudo que cruza módulos.

            ## Modelo de dados
            ```mermaid erDiagram``` das tabelas do módulo e das tabelas de outros módulos que ele lê/grava (DB-…), com as
            views/procedures/triggers (SQL-…, TRG-…) que as tocam; as sequências incluem triggers e jobs (JOB-…).

            ## Decisões de arquitetura
            `### ADR-001 — <decisão observada>`: contexto, decisão, consequências (inferidas do código — marque "inferido").

            ## Requisitos não funcionais
            `### NFR-001 — <requisito>`: desempenho (paginação, cache, limites), segurança, multi-tenant, disponibilidade,
            auditoria, i18n — com a evidência de como o código atende (ou não).

            ## Débitos, riscos e lacunas
            `### GAP-001 — <débito/risco>`: impacto, onde, sugestão.
            """),

        new("design", "Especificação de design", "re-design", 150,
            ["UI"],
            [
                new("Princípios e padrões visuais", "principios"),
                new("Componentes de UI", "componentes"),
                new("Layout e navegação", "layout"),
                new("Estados de tela", "estados"),
                new("Formulários e validações", "formularios"),
                new("Acessibilidade e internacionalização", "acessibilidade"),
                new("Aderência ao Figma e protótipos", "aderencia"),
                new("Lacunas e pontos a confirmar", "lacunas")
            ],
            "Como as telas do módulo são (e devem ser) desenhadas: componentes, padrões, estados, mensagens e aderência ao Figma.",
            """
            # Especificação de design — <módulo>

            ## Princípios e padrões visuais
            Design system usado (biblioteca de componentes, tema, Tailwind/Material/Bootstrap…), padrões de grid, cores
            de status, ícones — com os arquivos de onde vêm.

            ## Componentes de UI
            `### UI-001 — <componente/padrão>`: onde é usado (TELA-…), comportamento, variações, arquivo do componente.

            ## Layout e navegação
            Estrutura das páginas (cabeçalho, filtros, grade, painel lateral, modais), navegação entre telas (FLX-…).

            ## Estados de tela
            Vazio, carregando, erro, sem permissão, somente leitura — como cada TELA-… trata.

            ## Formulários e validações
            Padrão de formulário, quando valida (ao digitar, ao salvar), mensagens literais por campo (RN-…).

            ## Acessibilidade e internacionalização
            Idiomas suportados, chaves de tradução (arquivos), formatos de data/número por cultura, foco/teclado/contraste.

            ## Aderência ao Figma e protótipos
            Comparação com os anexos de UI/UX: o que segue, o que diverge (GAP-…).

            ## Lacunas e pontos a confirmar
            `### GAP-001 — …`
            """),

        new(Practical, "Visão prática (não técnica)", "re-pratica", 160,
            ["TUT", "FAQ"],
            [
                new("O que é e onde fica", "o que e"),
                new("Como chegar", "como chegar"),
                new("Como fazer", "como fazer"),
                new("Perguntas práticas", "perguntas"),
                new("Regras em linguagem simples", "regras"),
                new("Como configurar e dar acesso", "configurar"),
                new("Como testar", "testar"),
                new("Glossário", "glossario")
            ],
            "O guia do sistema para quem usa: o que é, onde fica, como chegar em cada tela, como fazer cada tarefa e as respostas às perguntas reais — gerado só do que foi publicado.",
            """
            # Visão prática — <módulo>

            > Escrito para quem usa o sistema (QA, suporte, gestores, clientes): **sem** tabela, classe, endpoint, procedure
            > ou caminho de arquivo; nome de tela e de menu como o usuário vê. Gerado **só a partir do que está publicado**
            > na engenharia reversa do módulo — **toda** frase, passo e resposta termina com a fonte:
            > `<!-- fonte: RN-012, TELA-003 -->` (IDs publicados; de outro módulo: `revamp-users#FN-002`). O que a engenharia
            > reversa não cobre **não entra aqui**: vira `GAP` no documento técnico certo (sugestão).

            ## O que é e onde fica
            Para que serve, quem usa, se é **legado ou revamp** (e onde convivem — o par do outro mundo), como habilitar
            na planta. <!-- fonte: … -->

            ## Como chegar
            Para cada tela: o caminho de menu ("Menu → Melhoria → A3 → Novo"), quem vê. <!-- fonte: TELA-…, PRF-… -->

            ## Como fazer
            `### TUT-001 — Como <tarefa>` (criar, editar, aprovar, reabrir, exportar, configurar…): passos numerados com a
            tela e o que o usuário vê, mensagens de erro comuns e o que fazer em cada uma. Cada passo com a fonte.

            ## Perguntas práticas
            `### FAQ-001 — <pergunta como o usuário faz>` (ex.: "O SA3 é legado ou revamp?", "Como saber se um usuário logou
            com sucesso?", "Por que o botão X não aparece?", "Quem recebe o e-mail?"): resposta direta em 2–6 frases com a
            fonte. As perguntas vêm das perguntas reais do "Pergunte" sobre o módulo (no pacote da sessão), das lacunas e
            das dúvidas dos cards. Pergunta que depende de outro módulo: responda com o que a engenharia reversa dele
            publicou e aponte o módulo.

            ## Regras em linguagem simples
            Quem pode o quê, prazos, aprovações, validações — uma frase por regra, com a fonte (RN-…).

            ## Como configurar e dar acesso
            Onde se configura, quem configura, como dar acesso, o que conferir quando "não aparece". <!-- fonte: CFG-…, PRF-… -->

            ## Como testar
            Cenários para QA: pré-requisitos, passos, resultado esperado e onde conferir. <!-- fonte: UC-… -->

            ## Glossário
            Os termos do módulo em linguagem simples, com os sinônimos — referenciando os `GLO-…` do funcional.
            """, "human")
    ];

    public static readonly IReadOnlyDictionary<string, ReverseDocType> ByKey = All.ToDictionary(d => d.Key, StringComparer.Ordinal);

    /// <summary>Texto comum a todos os modelos (como escrever um item).</summary>
    public static string ItemGuide => ItemRules;

    public static ReverseDocType Get(string? key) =>
        ByKey.TryGetValue((key ?? string.Empty).Trim().ToLowerInvariant(), out var type)
            ? type
            : throw new Entities.DomainException($"Documento inválido: '{key}' ({string.Join(", ", All.Select(d => d.Key))}).");

    public static ReverseDocType? BySection(string? sectionKey) => All.FirstOrDefault(d => d.SectionKey == sectionKey);
}
