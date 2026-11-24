using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0099 (Mesa de gestão): tarefas administrativas no "Skills Configurations" — idempotente e só acrescenta (por chave):
    /// <list type="bullet">
    /// <item>tipos de item (<c>WorkItemTypes</c>): report, duplicates, understanding, rca, devops, planning;</item>
    /// <item>tipos de sub-tarefa (<c>WorkItemTaskKinds</c>): devops-query, devops-create, devops-update, devops-move, report,
    /// duplicates, understanding, rca-doc, pdf — params com os nomes que a skill <c>tarefas-de-gestao</c> espera;</item>
    /// <item>modelos (<c>WorkItemTemplates</c>): relatorio-bugs, itens-duplicados, entendimento-card, rca-pdf, criar-historias
    /// (<c>when.type</c> com <c>epic</c> — a chave E… do épico).</item>
    /// </list>
    /// Chave ausente ou JSON inválido do admin: a lista fica como está (sem <c>WorkItemTypes</c>, o código usa os padrões, que
    /// já trazem os tipos novos — <c>WorkItemToolsService.ItemTypes</c>). Down tira só o que ainda está igual ao semeado.
    /// </summary>
    public partial class SeedDesk0099 : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        public const string ItemTypes = """
[
  { "key": "report", "label": "Relatório", "icon": "summarize", "color": "#5c6bc0", "description": "Relatório de bugs/cards do DevOps: consulta, números, tendências e recomendações, em markdown e PDF." },
  { "key": "duplicates", "label": "Duplicados", "icon": "content_copy", "color": "#8d6e63", "description": "Itens duplicados no DevOps: grupos, confiança e recomendação (nunca fecha card sem a pessoa decidir)." },
  { "key": "understanding", "label": "Entendimento", "icon": "psychology", "color": "#26a69a", "description": "Entendimento de bug/história com as regras da Base Solvace, para quem não é desenvolvedor." },
  { "key": "rca", "label": "RCA", "icon": "troubleshoot", "color": "#ef6c00", "description": "RCA no Modelo do prompt de Bug, em documento e PDF." },
  { "key": "devops", "label": "Gestão de cards", "icon": "view_kanban", "color": "#1e88e5", "description": "Criar, editar e mover cards no DevOps pelo PRMake (com a integração de quem pede)." },
  { "key": "planning", "label": "Planejamento", "icon": "account_tree", "color": "#7a5cff", "description": "Planejar e criar histórias e organizar os cards do épico." }
]
""";

        public const string NewKinds = """
[
  { "kind": "devops-query", "label": "Consultar cards no DevOps", "icon": "manage_search", "executor": "claude",
    "description": "Consulta somente leitura (WIQL ou filtros) pelo PRMake; grava consulta-<nome>.json nos arquivos do item com total, estados e as primeiras linhas.",
    "params": [
      { "key": "name", "label": "Nome da consulta (vira consulta-<nome>.json)", "input": "text" },
      { "key": "wiql", "label": "WIQL (opcional — senão os filtros)", "input": "textarea" },
      { "key": "types", "label": "Tipos (ex.: Bug, User Story)", "input": "text" },
      { "key": "states", "label": "Estados", "input": "text" },
      { "key": "areaPath", "label": "Área (sob)", "input": "text" },
      { "key": "tags", "label": "Tags", "input": "text" },
      { "key": "createdFrom", "label": "Criados desde (ex.: @today - 90)", "input": "text" },
      { "key": "changedFrom", "label": "Alterados desde", "input": "text" },
      { "key": "fields", "label": "Campos extras", "input": "text" },
      { "key": "top", "label": "Máximo de cards", "input": "text" }
    ] },
  { "kind": "devops-create", "label": "Criar card(s) no DevOps", "icon": "add_card", "executor": "claude",
    "description": "Cria cards pelo PRMake depois de procurar duplicado e conferir campos/tags pelas picklists; lote com mais de 10 cards pede confirmação. Card do épico entra no épico (created).",
    "params": [
      { "key": "type", "label": "Tipo do card", "input": "text" },
      { "key": "title", "label": "Título (um card)", "input": "text" },
      { "key": "cards", "label": "Cards (lote: um por linha)", "input": "textarea" },
      { "key": "source", "label": "Arquivo do item com a lista", "input": "text" },
      { "key": "fields", "label": "Campos ({ref: valor})", "input": "textarea" },
      { "key": "tags", "label": "Tags", "input": "text" },
      { "key": "newTags", "label": "Pode criar tag nova", "input": "select", "options": ["não", "sim"] },
      { "key": "parentId", "label": "Card pai", "input": "text" },
      { "key": "column", "label": "Coluna", "input": "text" },
      { "key": "lane", "label": "Lane", "input": "text" },
      { "key": "allowDuplicate", "label": "Criar mesmo parecido com outro", "input": "select", "options": ["não", "sim"] },
      { "key": "confirmed", "label": "Lote confirmado", "input": "select", "options": ["não", "sim"] }
    ] },
  { "kind": "devops-update", "label": "Editar card(s) no DevOps", "icon": "edit_note", "executor": "claude",
    "description": "Edita campos, tags e comentário pelo PRMake, anotando o valor anterior (De → Para); fechamento só com a decisão da pessoa; lote com mais de 10 cards pede confirmação.",
    "params": [
      { "key": "ids", "label": "Cards", "input": "text" },
      { "key": "source", "label": "Arquivo do item com os cards", "input": "text" },
      { "key": "fields", "label": "Campos ({ref: valor})", "input": "textarea" },
      { "key": "addTags", "label": "Tags a pôr", "input": "text" },
      { "key": "removeTags", "label": "Tags a tirar", "input": "text" },
      { "key": "newTags", "label": "Pode criar tag nova", "input": "select", "options": ["não", "sim"] },
      { "key": "comment", "label": "Comentário na discussion", "input": "textarea" },
      { "key": "confirmed", "label": "Lote confirmado", "input": "select", "options": ["não", "sim"] },
      { "key": "authorization", "label": "Decisão de quem autorizou (fechamento)", "input": "textarea" }
    ] },
  { "kind": "devops-move", "label": "Mover card(s) no board", "icon": "view_kanban", "executor": "claude",
    "description": "Move de coluna/lane pelo PRMake (coluna + estado mapeado do tipo), validando no board; coluna de fechamento só com a decisão da pessoa.",
    "params": [
      { "key": "ids", "label": "Cards", "input": "text" },
      { "key": "source", "label": "Arquivo do item com os cards", "input": "text" },
      { "key": "column", "label": "Coluna", "required": true, "input": "text" },
      { "key": "lane", "label": "Lane", "input": "text" },
      { "key": "done", "label": "Metade Done da coluna", "input": "select", "options": ["não", "sim"] },
      { "key": "team", "label": "Time (padrão: o do épico)", "input": "text" },
      { "key": "board", "label": "Board (padrão: o do épico)", "input": "text" },
      { "key": "confirmed", "label": "Lote confirmado", "input": "select", "options": ["não", "sim"] },
      { "key": "authorization", "label": "Decisão de quem autorizou (fechamento)", "input": "textarea" }
    ] },
  { "kind": "report", "label": "Relatório", "icon": "summarize", "executor": "claude",
    "description": "Relatório em markdown (sumário executivo, números, tabelas por estado/área/responsável, tendências e riscos) a partir da consulta do item.",
    "params": [
      { "key": "source", "label": "Consulta (arquivo do item ou #sub-tarefa)", "input": "text" },
      { "key": "title", "label": "Título", "input": "text" },
      { "key": "focus", "label": "O que destacar", "input": "textarea" },
      { "key": "weeks", "label": "Semanas no gráfico de ritmo", "input": "text" },
      { "key": "staleDays", "label": "Dias para considerar parado", "input": "text" }
    ] },
  { "kind": "duplicates", "label": "Itens duplicados", "icon": "content_copy", "executor": "claude",
    "description": "Agrupa possíveis duplicados (título normalizado, termos, repro, área, cliente, janela de datas), confirma e recomenda — nunca fecha nem liga card.",
    "params": [
      { "key": "source", "label": "Consulta (arquivo do item ou #sub-tarefa)", "input": "text" },
      { "key": "windowDays", "label": "Janela entre as criações (dias; 0 = sem janela)", "input": "text" },
      { "key": "min", "label": "Similaridade mínima (0 a 1)", "input": "text" }
    ] },
  { "kind": "understanding", "label": "Entendimento do card", "icon": "psychology", "executor": "claude",
    "description": "O que o bug/história pede, contexto, regras da Base Solvace afetadas, dúvidas e próximo passo — documento para quem não é desenvolvedor.",
    "params": [
      { "key": "card", "label": "Card", "input": "text" },
      { "key": "cards", "label": "Cards (vários, separados por vírgula)", "input": "text" },
      { "key": "focus", "label": "O que olhar", "input": "textarea" }
    ] },
  { "kind": "rca-doc", "label": "RCA em documento", "icon": "troubleshoot", "executor": "claude",
    "description": "RCA no Modelo do RCA do prompt de Bug (inglês), conferido pelo rca-check; não grava no card (isso é do fechamento).",
    "params": [
      { "key": "card", "label": "Card", "required": true, "input": "text" },
      { "key": "language", "label": "Também em português", "input": "select", "options": ["en", "pt"] }
    ] },
  { "kind": "pdf", "label": "PDF de um markdown", "icon": "picture_as_pdf", "executor": "claude",
    "description": "Gera no servidor o PDF de um markdown do item (cabeçalho, página, tabelas, imagens do item) e grava nos arquivos do item.",
    "params": [
      { "key": "file", "label": "Markdown do item (padrão: o da sub-tarefa anterior)", "input": "text" },
      { "key": "name", "label": "Nome do PDF", "input": "text" },
      { "key": "title", "label": "Título", "input": "text" }
    ] }
]
""";

        public const string NewTemplates = """
[
  {
    "key": "relatorio-bugs", "type": "report", "group": "Gestão",
    "title": "Relatório de bugs: {ask:title}",
    "description": "Consulta os bugs no DevOps pelo PRMake, monta o relatório (sumário executivo, números, tendências e riscos) e gera o PDF nos arquivos do item.",
    "when": { "type": ["epic"] },
    "ask": [
      { "key": "title", "label": "Título do relatório", "input": "text", "default": "Bugs dos últimos 90 dias", "required": true },
      { "key": "states", "label": "Estados (vazio = todos)", "input": "text", "required": false },
      { "key": "areaPath", "label": "Área (vazio = todas)", "input": "text", "required": false },
      { "key": "createdFrom", "label": "Criados desde", "input": "text", "default": "@today - 90", "required": true },
      { "key": "focus", "label": "O que destacar", "input": "textarea", "required": false }
    ],
    "tasks": [
      { "title": "Consultar os bugs no DevOps", "kind": "devops-query",
        "params": { "name": "bugs", "types": "Bug", "states": "{ask:states}", "areaPath": "{ask:areaPath}", "createdFrom": "{ask:createdFrom}" } },
      { "title": "Relatório: {ask:title}", "kind": "report",
        "params": { "source": "consulta-bugs.json", "title": "{ask:title}", "focus": "{ask:focus}" } },
      { "title": "PDF do relatório", "kind": "pdf",
        "params": { "name": "relatorio-bugs.pdf", "title": "{ask:title}" } }
    ]
  },
  {
    "key": "itens-duplicados", "type": "duplicates", "group": "Gestão",
    "title": "Itens duplicados: {ask:types}",
    "description": "Consulta os cards com o texto (repro/descrição), agrupa os possíveis duplicados com confiança e recomendação e gera o PDF. Nada é fechado nem ligado no DevOps.",
    "when": { "type": ["epic"] },
    "ask": [
      { "key": "types", "label": "Tipos", "input": "text", "default": "Bug", "required": true },
      { "key": "areaPath", "label": "Área (vazio = todas)", "input": "text", "required": false },
      { "key": "createdFrom", "label": "Criados desde", "input": "text", "default": "@today - 180", "required": true },
      { "key": "windowDays", "label": "Janela entre as criações (dias)", "input": "text", "default": "120", "required": true },
      { "key": "min", "label": "Similaridade mínima (0 a 1)", "input": "text", "default": "0.55", "required": true }
    ],
    "tasks": [
      { "title": "Consultar os cards (com o texto)", "kind": "devops-query",
        "description": "Traga o texto (repro/descrição) dos cards para comparar.",
        "params": { "name": "duplicados", "types": "{ask:types}", "areaPath": "{ask:areaPath}", "createdFrom": "{ask:createdFrom}" } },
      { "title": "Agrupar os duplicados", "kind": "duplicates",
        "params": { "source": "consulta-duplicados.json", "windowDays": "{ask:windowDays}", "min": "{ask:min}" } },
      { "title": "PDF dos duplicados", "kind": "pdf",
        "params": { "name": "itens-duplicados.pdf", "title": "Itens duplicados" } }
    ]
  },
  {
    "key": "entendimento-card", "type": "understanding", "group": "Gestão",
    "title": "Entendimento: {ask:cards}",
    "description": "Entendimento de bug/história para quem não é desenvolvedor (o que pede, contexto, regras da Base Solvace, dúvidas e próximo passo) e o resumo em PDF.",
    "when": { "type": ["epic", "bug", "user-story"] },
    "ask": [
      { "key": "cards", "label": "Card(s) (separados por vírgula)", "input": "text", "required": true },
      { "key": "focus", "label": "O que olhar", "input": "textarea", "required": false }
    ],
    "tasks": [
      { "title": "Entender {ask:cards}", "kind": "understanding",
        "params": { "cards": "{ask:cards}", "focus": "{ask:focus}" } },
      { "title": "Resumo do entendimento em PDF", "kind": "pdf",
        "params": { "name": "entendimento.pdf", "title": "Entendimento: {ask:cards}" } }
    ]
  },
  {
    "key": "rca-pdf", "type": "rca", "group": "Gestão",
    "title": "RCA do card {ask:card}",
    "description": "RCA no Modelo do prompt de Bug (inglês; opcionalmente em português) conferido pelo rca-check, com o PDF nos arquivos do item. Não grava no card.",
    "when": { "type": ["epic", "bug"] },
    "ask": [
      { "key": "card", "label": "Card", "input": "text", "required": true },
      { "key": "language", "label": "Também em português", "input": "select", "options": ["en", "pt"], "default": "en", "required": true }
    ],
    "tasks": [
      { "title": "RCA do card {ask:card}", "kind": "rca-doc",
        "params": { "card": "{ask:card}", "language": "{ask:language}" } },
      { "title": "PDF do RCA", "kind": "pdf",
        "params": { "file": "rca-{ask:card}.md", "name": "rca-{ask:card}.pdf", "title": "RCA {ask:card}" } }
    ]
  },
  {
    "key": "criar-historias", "type": "planning", "group": "Gestão",
    "title": "Criar histórias: {ask:focus}",
    "description": "Planeja as histórias (título, descrição, critérios de aceite, tags), espera a confirmação da lista, cria os cards no DevOps dentro do épico e move para a coluna escolhida.",
    "when": { "type": ["epic"] },
    "ask": [
      { "key": "focus", "label": "O que planejar", "input": "textarea", "required": true },
      { "key": "type", "label": "Tipo dos cards", "input": "text", "default": "User Story", "required": true },
      { "key": "parentId", "label": "Card pai (opcional)", "input": "text", "required": false },
      { "key": "column", "label": "Coluna de destino", "input": "text", "required": true }
    ],
    "tasks": [
      { "title": "Planejar as histórias", "kind": "task",
        "description": "Escreva a lista das histórias (título, descrição, critérios de aceite, tipo, tags) em planejamento-historias.md nos arquivos do item, procure duplicados e espere a confirmação da pessoa (wait).",
        "params": { "focus": "{ask:focus}" } },
      { "title": "Criar as histórias no DevOps", "kind": "devops-create",
        "params": { "source": "planejamento-historias.md", "type": "{ask:type}", "parentId": "{ask:parentId}" } },
      { "title": "Mover as histórias para {ask:column}", "kind": "devops-move",
        "params": { "column": "{ask:column}" } }
    ]
  }
]
""";

        private static string Block(string field, string id, string tag, string seed, bool up) => $$"""
                    BEGIN
                        arr := NULLIF(btrim(COALESCE(opts -> 0 ->> '{{field}}', '')), '')::jsonb;
                    EXCEPTION WHEN others THEN
                        arr := NULL;
                    END;
                    IF arr IS NOT NULL AND jsonb_typeof(arr) = 'array' THEN
                        seed := ${{tag}}${{seed}}${{tag}}$::jsonb;
                        touched := false;
                        IF {{(up ? "true" : "false")}} THEN
                            FOR e IN SELECT x FROM jsonb_array_elements(seed) x LOOP
                                IF NOT EXISTS (SELECT 1 FROM jsonb_array_elements(arr) y WHERE y ->> '{{id}}' = e ->> '{{id}}') THEN
                                    arr := arr || jsonb_build_array(e);
                                    touched := true;
                                END IF;
                            END LOOP;
                        ELSE
                            SELECT COALESCE(jsonb_agg(t.y ORDER BY t.o), '[]'::jsonb) INTO tmp
                            FROM jsonb_array_elements(arr) WITH ORDINALITY AS t(y, o)
                            WHERE NOT EXISTS (SELECT 1 FROM jsonb_array_elements(seed) s WHERE s = t.y);
                            IF tmp <> arr THEN
                                arr := tmp;
                                touched := true;
                            END IF;
                        END IF;
                        IF touched THEN
                            opts := jsonb_set(opts, '{0,{{field}}}', to_jsonb(jsonb_pretty(arr)), true);
                            changed := true;
                        END IF;
                    END IF;
            """;

        private static string Swap(bool up) => $$"""
            DO $mig$
            DECLARE
                r record; opts jsonb; arr jsonb; seed jsonb; tmp jsonb; e jsonb; touched boolean; changed boolean;
            BEGIN
                FOR r IN
                    SELECT c."Id", c."Options" FROM prform."PluginConfigurations" c
                    JOIN prform."Plugins" p ON p."Id" = c."PluginId"
                    WHERE p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted" AND NULLIF(c."Options", '') IS NOT NULL
                LOOP
                    BEGIN
                        opts := r."Options"::jsonb;
                    EXCEPTION WHEN others THEN
                        CONTINUE;
                    END;
                    IF jsonb_typeof(opts) <> 'array' OR jsonb_typeof(opts -> 0) <> 'object' THEN CONTINUE; END IF;
                    changed := false;
            {{Block("WorkItemTypes", "key", "ty", ItemTypes, up)}}
            {{Block("WorkItemTaskKinds", "kind", "ki", NewKinds, up)}}
            {{Block("WorkItemTemplates", "key", "tp", NewTemplates, up)}}
                    IF changed THEN
                        UPDATE prform."PluginConfigurations" SET "Options" = opts::text WHERE "Id" = r."Id";
                    END IF;
                END LOOP;
            END
            $mig$;
            """;

        /// <summary>SQL do Up (para o teste conferir a montagem).</summary>
        public static string UpSql => Swap(true);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Swap(true));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Swap(false));
    }
}
