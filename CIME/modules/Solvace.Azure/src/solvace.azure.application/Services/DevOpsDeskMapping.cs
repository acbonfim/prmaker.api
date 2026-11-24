using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using solvace.azure.domain.Exceptions;
using solvace.azure.domain.Models;

namespace solvace.azure.application.Services;

/// <summary>Operação de JSON Patch do work item (<c>op</c>, <c>path</c>, <c>value</c>; sem value no remove).</summary>
public sealed record DevOpsPatchOp(string Op, string Path, object? Value = null);

/// <summary>
/// 0099 (Mesa de gestão): conversões puras entre o JSON do Azure DevOps e o contrato da Mesa — board → configuração (com o
/// hash estável), JSON Patch de criar/editar/mover, tags, WIQL dos filtros, árvore de áreas/iterações. Sem I/O: os testes
/// conferem tudo isso sem o DevOps.
/// </summary>
public static class DevOpsDeskMapping
{
    public const string FieldId = "System.Id";
    public const string FieldTitle = "System.Title";
    public const string FieldState = "System.State";
    public const string FieldType = "System.WorkItemType";
    public const string FieldTags = "System.Tags";
    public const string FieldAssignedTo = "System.AssignedTo";
    public const string FieldParent = "System.Parent";
    public const string FieldChangedDate = "System.ChangedDate";
    public const string FieldBoardColumn = "System.BoardColumn";
    public const string FieldBoardColumnDone = "System.BoardColumnDone";
    public const string FieldBoardLane = "System.BoardLane";
    public const string FieldPriority = "Microsoft.VSTS.Common.Priority";
    public const string ParentLink = "System.LinkTypes.Hierarchy-Reverse";

    /// <summary>Campos padrão da consulta quando a pessoa não escolhe.</summary>
    public static readonly IReadOnlyList<string> DefaultQueryFields =
    [
        FieldId, FieldType, FieldTitle, FieldState, FieldAssignedTo, FieldTags, "System.AreaPath", "System.IterationPath",
        "System.CreatedDate", FieldChangedDate, FieldPriority, FieldBoardColumn
    ];

    // ---------------------------------------------------------------- metadados

    /// <summary><c>wit/workitemtypes</c> → tipos (cor com #, ícone pelo id).</summary>
    public static List<DevOpsTypeInfo> ParseTypes(JsonElement root) =>
        Values(root)
            .Select(t => new DevOpsTypeInfo(
                Str(t, "name") ?? string.Empty,
                Color(Str(t, "color")),
                t.TryGetProperty("icon", out var icon) && icon.ValueKind == JsonValueKind.Object ? Str(icon, "id") : null,
                Str(t, "description"),
                Bool(t, "isDisabled")))
            .Where(t => t.Name.Length > 0)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Definição do campo no projeto (<c>wit/fields</c>): tipo, somente leitura, picklist.</summary>
    public sealed record ProjectField(string Type, bool ReadOnly, bool IsPicklist);

    /// <summary><c>wit/fields</c> → referenceName → definição.</summary>
    public static Dictionary<string, ProjectField> ParseProjectFields(JsonElement root)
    {
        var result = new Dictionary<string, ProjectField>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Values(root))
            if (Str(f, "referenceName") is { Length: > 0 } reference)
                result[reference] = new ProjectField(Str(f, "type") ?? "string", Bool(f, "readOnly"), Bool(f, "isPicklist"));
        return result;
    }

    /// <summary>
    /// <c>wit/workitemtypes/{t}/fields?$expand=allowedValues</c> + definições do projeto → campos do tipo, na ordem do DevOps.
    /// </summary>
    public static List<DevOpsFieldInfo> ParseTypeFields(JsonElement root, IReadOnlyDictionary<string, ProjectField> projectFields)
    {
        var result = new List<DevOpsFieldInfo>();
        foreach (var f in Values(root))
        {
            var reference = Str(f, "referenceName");
            if (string.IsNullOrEmpty(reference)) continue;
            projectFields.TryGetValue(reference, out var def);
            var allowed = f.TryGetProperty("allowedValues", out var av) && av.ValueKind == JsonValueKind.Array
                ? av.EnumerateArray().Select(ValueText).Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToList()
                : [];
            result.Add(new DevOpsFieldInfo(
                reference,
                Str(f, "name") ?? reference,
                MapFieldType(def?.Type, def?.IsPicklist == true),
                Bool(f, "alwaysRequired"),
                Bool(f, "readOnly") || def?.ReadOnly == true,
                allowed,
                f.TryGetProperty("defaultValue", out var dv) ? ValueText(dv) : null,
                Str(f, "helpText")));
        }
        return result;
    }

    /// <summary>Tipo do campo no DevOps → contrato (string|integer|double|dateTime|html|plainText|boolean|treePath|identity|picklist*).</summary>
    public static string MapFieldType(string? devOpsType, bool isPicklist)
    {
        var t = (devOpsType ?? "string").Trim();
        if (t.Equals("picklistString", StringComparison.OrdinalIgnoreCase)) return "picklistString";
        if (t.Equals("picklistInteger", StringComparison.OrdinalIgnoreCase)) return "picklistInteger";
        if (t.Equals("picklistDouble", StringComparison.OrdinalIgnoreCase)) return "double";
        var mapped = t.ToLowerInvariant() switch
        {
            "integer" => "integer",
            "double" => "double",
            "datetime" => "dateTime",
            "html" or "history" => "html",
            "plaintext" => "plainText",
            "boolean" => "boolean",
            "treepath" => "treePath",
            "identity" => "identity",
            _ => "string"
        };
        if (!isPicklist) return mapped;
        return mapped switch
        {
            "string" => "picklistString",
            "integer" => "picklistInteger",
            _ => mapped
        };
    }

    /// <summary>
    /// Árvore de áreas/iterações (<c>classificationnodes?$depth=10</c>) achatada no formato do campo: a raiz é o projeto
    /// (<c>Projeto</c>, profundidade 0), os filhos <c>Projeto\Time</c>... <paramref name="structureType"/>: area | iteration.
    /// </summary>
    public static List<DevOpsNodeInfo> FlattenNodes(JsonElement root, string structureType)
    {
        var result = new List<DevOpsNodeInfo>();
        IEnumerable<JsonElement> roots = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray()
            : root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray()
            : [root];
        foreach (var node in roots)
        {
            var kind = Str(node, "structureType");
            if (kind is not null && !kind.Equals(structureType, StringComparison.OrdinalIgnoreCase)) continue;
            Walk(node, null, 0);
        }
        return result;

        void Walk(JsonElement node, string? parent, int depth)
        {
            var name = Str(node, "name");
            if (string.IsNullOrEmpty(name)) return;
            var path = parent is null ? name : $"{parent}\\{name}";
            result.Add(new DevOpsNodeInfo(path, name, depth));
            if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
                foreach (var child in children.EnumerateArray())
                    Walk(child, path, depth + 1);
        }
    }

    /// <summary>Membros de um time (<c>teams/{t}/members</c>) — sem grupos.</summary>
    public static IEnumerable<DevOpsPersonInfo> ParseMembers(JsonElement root) =>
        Values(root)
            .Select(m => m.TryGetProperty("identity", out var i) && i.ValueKind == JsonValueKind.Object ? i : m)
            .Where(i => !Bool(i, "isContainer"))
            .Select(i => new DevOpsPersonInfo(Str(i, "displayName") ?? string.Empty, Str(i, "uniqueName") ?? string.Empty,
                Str(i, "id") ?? string.Empty, Str(i, "imageUrl")))
            .Where(p => p.DisplayName.Length > 0);

    /// <summary>Lista <c>{id, name}</c> (times, boards).</summary>
    public static List<DevOpsNamedRef> ParseNamed(JsonElement root) =>
        Values(root)
            .Select(v => new DevOpsNamedRef(Str(v, "id") ?? string.Empty, Str(v, "name") ?? string.Empty))
            .Where(r => r.Name.Length > 0)
            .ToList();

    /// <summary>Tags do projeto (<c>wit/tags</c>) filtradas por "contém" (ignora caixa), em ordem alfabética.</summary>
    public static List<string> ParseTags(JsonElement root, string? q)
    {
        var term = q?.Trim();
        return Values(root)
            .Select(t => Str(t, "name"))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Where(n => string.IsNullOrEmpty(term) || n.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Campo de ordem do backlog (<c>work/backlogconfiguration</c> → <c>backlogFields.typeFields.Order</c>).</summary>
    public static string? ParseOrderField(JsonElement root) =>
        root.TryGetProperty("backlogFields", out var bf) && bf.ValueKind == JsonValueKind.Object
        && bf.TryGetProperty("typeFields", out var tf) && tf.ValueKind == JsonValueKind.Object
            ? Str(tf, "Order") ?? Str(tf, "order")
            : null;

    // ---------------------------------------------------------------- board

    /// <summary>
    /// <c>work/boards/{id}</c> → configuração do board: colunas (com o mapeamento coluna→estado por tipo, limite, tipo,
    /// divisão), lanes (<c>rows</c>; a padrão vem sem nome), campos coluna/lane/done, tipos (das colunas, com a cor) e o
    /// campo de ordem; <c>ConfigHash</c> calculado no fim.
    /// </summary>
    public static DevOpsBoardConfig ParseBoard(JsonElement board, string team, IReadOnlyDictionary<string, string?> typeColors, string? orderField)
    {
        var config = new DevOpsBoardConfig
        {
            Team = team,
            Board = Str(board, "name") ?? string.Empty,
            BoardId = Str(board, "id") ?? string.Empty,
            OrderField = string.IsNullOrWhiteSpace(orderField) ? null : orderField
        };

        if (board.TryGetProperty("columns", out var columns) && columns.ValueKind == JsonValueKind.Array)
            foreach (var c in columns.EnumerateArray())
            {
                var column = new DevOpsBoardColumn
                {
                    Id = Str(c, "id") ?? string.Empty,
                    Name = Str(c, "name") ?? string.Empty,
                    ItemLimit = c.TryGetProperty("itemLimit", out var il) && il.TryGetInt32(out var limit) ? limit : 0,
                    ColumnType = ColumnType(c.TryGetProperty("columnType", out var ct) ? ct : default),
                    IsSplit = Bool(c, "isSplit"),
                    Description = string.IsNullOrWhiteSpace(Str(c, "description")) ? null : Str(c, "description")
                };
                if (c.TryGetProperty("stateMappings", out var sm) && sm.ValueKind == JsonValueKind.Object)
                    foreach (var p in sm.EnumerateObject())
                        if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } state)
                            column.StateMappings[p.Name] = state;
                config.Columns.Add(column);
            }

        if (board.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            foreach (var r in rows.EnumerateArray())
            {
                var id = Str(r, "id") ?? string.Empty;
                var name = Str(r, "name");
                var isDefault = string.IsNullOrEmpty(name) || (Guid.TryParse(id, out var g) && g == Guid.Empty);
                config.Lanes.Add(new DevOpsBoardLane(id, name ?? string.Empty, isDefault));
            }

        if (board.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
            config.Fields = new DevOpsBoardFields(RefName(fields, "columnField"), RefName(fields, "rowField"), RefName(fields, "doneField"));

        config.Types = config.Columns
            .SelectMany(c => c.StateMappings.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(t => new DevOpsBoardType(t, typeColors.TryGetValue(t, out var color) ? color : null))
            .ToList();

        config.ConfigHash = ComputeConfigHash(config);
        return config;
    }

    /// <summary>
    /// Hash estável da configuração: JSON normalizado (colunas na ordem do board, mapeamentos por tipo em ordem, lanes,
    /// campos, tipos, campo de ordem) → SHA-256, 16 primeiros hexas. Mesma configuração = mesmo hash, sempre.
    /// </summary>
    public static string ComputeConfigHash(DevOpsBoardConfig config)
    {
        var normalized = new
        {
            b = config.BoardId,
            c = config.Columns.Select(c => new
            {
                c.Id, c.Name, c.ItemLimit, c.ColumnType, c.IsSplit, c.Description,
                m = c.StateMappings.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new[] { p.Key, p.Value }).ToArray()
            }),
            l = config.Lanes.Select(l => new { l.Id, l.Name, l.IsDefault }),
            f = new[] { config.Fields.Column, config.Fields.Lane, config.Fields.Done },
            t = config.Types.Select(t => new[] { t.Name, t.Color }),
            o = config.OrderField
        };
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized)));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    /// <summary>Coluna pelo nome ou id (ignora caixa).</summary>
    public static DevOpsBoardColumn? FindColumn(DevOpsBoardConfig config, string column)
    {
        var c = column.Trim();
        return config.Columns.FirstOrDefault(x => x.Id.Equals(c, StringComparison.OrdinalIgnoreCase))
               ?? config.Columns.FirstOrDefault(x => x.Name.Equals(c, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Lane pelo nome ou id; vazio/"default"/"padrão" = a lane padrão.</summary>
    public static DevOpsBoardLane? FindLane(DevOpsBoardConfig config, string lane)
    {
        var l = lane.Trim();
        if (l.Length == 0 || l.Equals("default", StringComparison.OrdinalIgnoreCase) || l.Equals("default lane", StringComparison.OrdinalIgnoreCase)
            || l.Equals("padrão", StringComparison.OrdinalIgnoreCase))
            return config.Lanes.FirstOrDefault(x => x.IsDefault) ?? new DevOpsBoardLane(Guid.Empty.ToString(), string.Empty, true);
        return config.Lanes.FirstOrDefault(x => x.Id.Equals(l, StringComparison.OrdinalIgnoreCase))
               ?? config.Lanes.FirstOrDefault(x => x.Name.Equals(l, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Destino de um movimento: as operações do patch e o estado novo.</summary>
    public sealed record MovePlan(IReadOnlyList<DevOpsPatchOp> Ops, string Column, string State, string? Lane, bool Done);

    /// <summary>
    /// Patch de mover o card: coluna (campo do board) + <c>System.State</c> mapeado para o tipo + done (coluna dividida;
    /// fora dela, false se estava true) + lane. Coluna sem mapeamento do tipo = 409 com a explicação. Lane padrão limpa o campo (se havia).
    /// </summary>
    public static MovePlan BuildMove(DevOpsBoardConfig config, string workItemType, string currentState, IReadOnlyDictionary<string, JsonElement> currentFields,
        string column, string? lane, bool? done)
    {
        if (string.IsNullOrWhiteSpace(column))
            throw DevOpsDeskException.Invalid("Informe a coluna de destino.");
        var target = FindColumn(config, column)
                     ?? throw DevOpsDeskException.Invalid($"A coluna \"{column}\" não existe no board {config.Board} ({string.Join(", ", config.Columns.Select(c => c.Name))}).");
        if (!target.StateMappings.TryGetValue(workItemType, out var state) || string.IsNullOrWhiteSpace(state))
            throw DevOpsDeskException.NoMapping($"A coluna \"{target.Name}\" do board {config.Board} não aceita {workItemType} (o board não mapeia esse tipo para nenhum estado nela).");
        if (string.IsNullOrEmpty(config.Fields.Column))
            throw DevOpsDeskException.Invalid($"O board {config.Board} não informou o campo da coluna.");

        // Só o que muda entra no patch: reordenar dentro da coluna não gera revisão.
        var ops = new List<DevOpsPatchOp>();
        if (!string.Equals(FieldText(currentFields, config.Fields.Column), target.Name, StringComparison.Ordinal))
            ops.Add(new DevOpsPatchOp("add", $"/fields/{config.Fields.Column}", target.Name));
        if (!string.Equals(currentState, state, StringComparison.Ordinal))
            ops.Add(new DevOpsPatchOp("add", $"/fields/{FieldState}", state));

        // Done só existe na coluna dividida; fora dela, limpa o que tinha ficado (card saindo do Done de outra coluna).
        var isDone = target.IsSplit && done == true;
        var wasDone = !string.IsNullOrEmpty(config.Fields.Done) && currentFields.TryGetValue(config.Fields.Done, out var d) && d.ValueKind == JsonValueKind.True;
        if (!string.IsNullOrEmpty(config.Fields.Done) && isDone != wasDone && (target.IsSplit || wasDone))
            ops.Add(new DevOpsPatchOp("add", $"/fields/{config.Fields.Done}", isDone));

        string? laneName = null;
        if (lane is not null)
        {
            var targetLane = FindLane(config, lane)
                             ?? throw DevOpsDeskException.Invalid($"A lane \"{lane}\" não existe no board {config.Board}.");
            if (!string.IsNullOrEmpty(config.Fields.Lane))
            {
                if (targetLane.IsDefault)
                {
                    if (HasValue(currentFields, config.Fields.Lane))
                        ops.Add(new DevOpsPatchOp("remove", $"/fields/{config.Fields.Lane}"));
                }
                else if (!string.Equals(FieldText(currentFields, config.Fields.Lane), targetLane.Name, StringComparison.Ordinal))
                    ops.Add(new DevOpsPatchOp("add", $"/fields/{config.Fields.Lane}", targetLane.Name));
            }
            laneName = targetLane.Name;
        }
        return new MovePlan(ops, target.Name, state, laneName, isDone);
    }

    // ---------------------------------------------------------------- tags e patch

    /// <summary><c>System.Tags</c> ("a; b") → lista sem vazios nem repetidos.</summary>
    public static List<string> SplitTags(string? tags) =>
        (tags ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string JoinTags(IEnumerable<string> tags) =>
        string.Join("; ", tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));

    /// <summary>Tags atuais + as somadas − as tiradas (ignora caixa; mantém a grafia que já estava no card).</summary>
    public static List<string> MergeTags(IEnumerable<string> current, IEnumerable<string>? add, IEnumerable<string>? remove)
    {
        var result = current.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var t in add ?? [])
        {
            var tag = t?.Trim().Trim(';').Trim();
            if (!string.IsNullOrEmpty(tag) && !result.Contains(tag, StringComparer.OrdinalIgnoreCase))
                result.Add(tag);
        }
        var removed = new HashSet<string>((remove ?? []).Select(t => t?.Trim() ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        result.RemoveAll(removed.Contains);
        return result;
    }

    /// <summary>
    /// Patch de criar o card: título, campos informados (null = ignorado), tags e o pai (relação Hierarchy-Reverse).
    /// </summary>
    public static List<DevOpsPatchOp> BuildCreatePatch(string title, IReadOnlyDictionary<string, JsonElement>? fields, IEnumerable<string>? tags,
        int? parentId, string organizationUrl)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw DevOpsDeskException.Invalid("Informe o título do card.");
        var ops = new List<DevOpsPatchOp> { new("add", $"/fields/{FieldTitle}", title.Trim()) };
        foreach (var (reference, value) in fields ?? new Dictionary<string, JsonElement>())
        {
            if (IsNull(value) || reference.Equals(FieldTitle, StringComparison.OrdinalIgnoreCase)) continue;
            if (reference.Equals(FieldTags, StringComparison.OrdinalIgnoreCase)) continue; // vai pelas tags
            ops.Add(new DevOpsPatchOp("add", $"/fields/{CheckField(reference)}", value));
        }
        var allTags = MergeTags(fields is not null && fields.TryGetValue(FieldTags, out var ft) && ft.ValueKind == JsonValueKind.String
            ? SplitTags(ft.GetString()) : [], tags, null);
        if (allTags.Count > 0)
            ops.Add(new DevOpsPatchOp("add", $"/fields/{FieldTags}", JoinTags(allTags)));
        if (parentId is > 0)
            ops.Add(ParentRelation(parentId.Value, organizationUrl));
        return ops;
    }

    public static DevOpsPatchOp ParentRelation(int parentId, string organizationUrl) =>
        new("add", "/relations/-", new { rel = ParentLink, url = $"{organizationUrl.TrimEnd('/')}/_apis/wit/workItems/{parentId}" });

    /// <summary>
    /// Patch de editar: campo com valor = add; null = remove (só se o card tinha valor); tags = as atuais ± as pedidas (só
    /// quando mudam). Nada a mudar = lista vazia.
    /// </summary>
    public static List<DevOpsPatchOp> BuildUpdatePatch(IReadOnlyDictionary<string, JsonElement> currentFields, IReadOnlyDictionary<string, JsonElement>? fields,
        IEnumerable<string>? addTags, IEnumerable<string>? removeTags)
    {
        var ops = new List<DevOpsPatchOp>();
        var currentTags = SplitTags(currentFields.TryGetValue(FieldTags, out var ct) && ct.ValueKind == JsonValueKind.String ? ct.GetString() : null);
        var tagsTouched = (addTags?.Any() ?? false) || (removeTags?.Any() ?? false);
        foreach (var (reference, value) in fields ?? new Dictionary<string, JsonElement>())
        {
            var field = CheckField(reference);
            if (field.Equals(FieldTags, StringComparison.OrdinalIgnoreCase))
            {
                // Tags inteiras no campo: substitui as atuais (somadas/tiradas depois, se vierem).
                currentTags = IsNull(value) ? [] : SplitTags(value.ValueKind == JsonValueKind.String ? value.GetString() : ValueText(value));
                tagsTouched = true;
                continue;
            }
            if (IsNull(value))
            {
                if (HasValue(currentFields, field))
                    ops.Add(new DevOpsPatchOp("remove", $"/fields/{field}"));
            }
            else
                ops.Add(new DevOpsPatchOp("add", $"/fields/{field}", value));
        }
        if (tagsTouched)
        {
            var before = SplitTags(currentFields.TryGetValue(FieldTags, out var bt) && bt.ValueKind == JsonValueKind.String ? bt.GetString() : null);
            var after = MergeTags(currentTags, addTags, removeTags);
            var same = before.Count == after.Count && before.All(b => after.Contains(b, StringComparer.OrdinalIgnoreCase));
            if (!same)
                ops.Add(after.Count == 0
                    ? new DevOpsPatchOp("add", $"/fields/{FieldTags}", string.Empty)
                    : new DevOpsPatchOp("add", $"/fields/{FieldTags}", JoinTags(after)));
        }
        return ops;
    }

    private static string CheckField(string reference)
    {
        var r = reference.Trim();
        if (r.Length == 0 || r.Contains('/') || r.Contains(' '))
            throw DevOpsDeskException.Invalid($"Campo inválido: \"{reference}\" (use o referenceName, ex.: System.Title).");
        return r;
    }

    // ---------------------------------------------------------------- WIQL

    /// <summary>Texto entre aspas simples da WIQL (aspas dobradas).</summary>
    public static string Quote(string value) => $"'{value.Replace("'", "''")}'";

    private static string In(IEnumerable<string> values) => string.Join(", ", values.Select(Quote));

    /// <summary>
    /// Filtros da consulta → WIQL do projeto: valores do mesmo filtro em OU, filtros diferentes em E; texto = título
    /// contém (ou o id, quando é número); responsável <c>@me</c> aceito; datas em dia (<c>yyyy-MM-dd</c>).
    /// </summary>
    public static string BuildFilterWiql(DevOpsQueryFilter? filter, IReadOnlyCollection<string>? fields = null)
    {
        var where = new List<string> { "[System.TeamProject] = @project" };
        AppendCommonFilters(where, filter?.Types, filter?.States, filter?.AssignedTo, filter?.Tags, filter?.Text);
        if (!string.IsNullOrWhiteSpace(filter?.AreaPath)) where.Add($"[System.AreaPath] UNDER {Quote(filter.AreaPath.Trim())}");
        if (!string.IsNullOrWhiteSpace(filter?.IterationPath)) where.Add($"[System.IterationPath] UNDER {Quote(filter.IterationPath.Trim())}");
        if (filter?.CreatedFrom is { } cf) where.Add($"[System.CreatedDate] >= {Quote(Day(cf))}");
        if (filter?.CreatedTo is { } ct) where.Add($"[System.CreatedDate] <= {Quote(Day(ct))}");
        if (filter?.ChangedFrom is { } chf) where.Add($"[System.ChangedDate] >= {Quote(Day(chf))}");
        return $"SELECT [System.Id] FROM WorkItems WHERE {string.Join(" AND ", where)} ORDER BY [System.ChangedDate] DESC";
    }

    /// <summary>
    /// WIQL dos cards do board: cards do time (valores do campo do time — área UNDER/=) ou os ids do épico, só dos tipos do
    /// board nos estados mapeados para as colunas do grupo pedido (<paramref name="outgoing"/>: a coluna final, que no DevOps
    /// mostra só os mais recentes), com os filtros da tela. Ordem: campo de ordem do backlog (ou a última alteração na coluna
    /// final).
    /// </summary>
    public static string? BuildBoardWiql(DevOpsBoardConfig config, string? teamField, IReadOnlyList<(string Value, bool IncludeChildren)> teamValues,
        IReadOnlyCollection<int>? ids, bool? outgoing, DevOpsBoardItemsRequest filters)
    {
        var where = new List<string> { "[System.TeamProject] = @project" };
        if (ids is not null)
        {
            if (ids.Count == 0) return null;
            where.Add($"[System.Id] IN ({string.Join(", ", ids.Distinct())})");
        }
        else
        {
            if (string.IsNullOrEmpty(teamField) || teamValues.Count == 0) return null;
            where.Add("(" + string.Join(" OR ", teamValues.Select(v => $"[{teamField}] {(v.IncludeChildren ? "UNDER" : "=")} {Quote(v.Value)}")) + ")");
        }

        var typeClauses = new List<string>();
        foreach (var type in config.Types.Select(t => t.Name))
        {
            var states = config.Columns
                .Where(c => outgoing is null || (c.ColumnType == "outgoing") == outgoing)
                .Select(c => c.StateMappings.TryGetValue(type, out var s) ? s : null)
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (states.Count > 0)
                typeClauses.Add($"([System.WorkItemType] = {Quote(type)} AND [System.State] IN ({In(states)}))");
        }
        if (typeClauses.Count == 0) return null;
        where.Add("(" + string.Join(" OR ", typeClauses) + ")");

        AppendCommonFilters(where, filters.Types, filters.States, filters.AssignedTo, filters.Tags, filters.Text);
        var order = outgoing == true || string.IsNullOrEmpty(config.OrderField)
            ? "[System.ChangedDate] DESC"
            : $"[{config.OrderField}] ASC, [System.Id] ASC";
        return $"SELECT [System.Id] FROM WorkItems WHERE {string.Join(" AND ", where)} ORDER BY {order}";
    }

    private static void AppendCommonFilters(List<string> where, IEnumerable<string>? types, IEnumerable<string>? states, IEnumerable<string>? assignedTo,
        IEnumerable<string>? tags, string? text)
    {
        var t = Clean(types);
        if (t.Count > 0) where.Add($"[System.WorkItemType] IN ({In(t)})");
        var s = Clean(states);
        if (s.Count > 0) where.Add($"[System.State] IN ({In(s)})");
        var a = Clean(assignedTo);
        if (a.Count > 0)
            where.Add("(" + string.Join(" OR ", a.Select(p =>
                p.Equals("@me", StringComparison.OrdinalIgnoreCase) ? "[System.AssignedTo] = @me"
                : p.Equals("(unassigned)", StringComparison.OrdinalIgnoreCase) || p == "-" ? "[System.AssignedTo] = ''"
                : $"[System.AssignedTo] = {Quote(p)}")) + ")");
        var g = Clean(tags);
        if (g.Count > 0) where.Add("(" + string.Join(" OR ", g.Select(x => $"[System.Tags] CONTAINS {Quote(x)}")) + ")");
        if (!string.IsNullOrWhiteSpace(text))
        {
            var term = text.Trim().TrimStart('#');
            where.Add(int.TryParse(term, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? $"([System.Id] = {id} OR [System.Title] CONTAINS {Quote(term)})"
                : $"[System.Title] CONTAINS {Quote(text.Trim())}");
        }
    }

    private static List<string> Clean(IEnumerable<string>? values) =>
        (values ?? []).Select(v => v?.Trim() ?? string.Empty).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string Day(DateTimeOffset d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Ids da resposta da WIQL (lista plana ou de links — o destino de cada link), sem repetir, na ordem.</summary>
    public static List<int> ParseWiqlIds(JsonElement root)
    {
        var ids = new List<int>();
        if (root.TryGetProperty("workItems", out var items) && items.ValueKind == JsonValueKind.Array)
            ids.AddRange(items.EnumerateArray().Select(i => i.TryGetProperty("id", out var id) && id.TryGetInt32(out var v) ? v : 0));
        else if (root.TryGetProperty("workItemRelations", out var rels) && rels.ValueKind == JsonValueKind.Array)
            foreach (var r in rels.EnumerateArray())
            {
                if (r.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.Object && src.TryGetProperty("id", out var s) && s.TryGetInt32(out var sv))
                    ids.Add(sv);
                if (r.TryGetProperty("target", out var tgt) && tgt.ValueKind == JsonValueKind.Object && tgt.TryGetProperty("id", out var t) && t.TryGetInt32(out var tv))
                    ids.Add(tv);
            }
        return ids.Where(i => i > 0).Distinct().ToList();
    }

    // ---------------------------------------------------------------- cards

    /// <summary>Fields de um work item (<c>fields</c>) clonados.</summary>
    public static Dictionary<string, JsonElement> CloneFields(JsonElement workItem)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (workItem.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
            foreach (var p in fields.EnumerateObject())
                result[p.Name] = p.Value.Clone();
        return result;
    }

    /// <summary>Work item (<c>$expand=relations</c>) → detalhe do card.</summary>
    public static DevOpsCardDetail ParseCard(JsonElement workItem, string browserUrl)
    {
        var fields = CloneFields(workItem);
        var card = new DevOpsCardDetail
        {
            Id = workItem.TryGetProperty("id", out var id) && id.TryGetInt32(out var idv) ? idv : 0,
            Rev = workItem.TryGetProperty("rev", out var rev) && rev.TryGetInt32(out var rv) ? rv : 0,
            Type = FieldText(fields, FieldType) ?? string.Empty,
            Title = FieldText(fields, FieldTitle) ?? string.Empty,
            State = FieldText(fields, FieldState) ?? string.Empty,
            Fields = fields,
            Tags = SplitTags(FieldText(fields, FieldTags)),
            Url = browserUrl
        };
        if (workItem.TryGetProperty("relations", out var relations) && relations.ValueKind == JsonValueKind.Array)
            foreach (var r in relations.EnumerateArray())
            {
                var url = Str(r, "url") ?? string.Empty;
                var name = r.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object ? Str(attrs, "name") : null;
                card.Relations.Add(new DevOpsCardRelation(Str(r, "rel") ?? string.Empty, WorkItemIdFromUrl(url), url, name));
            }
        return card;
    }

    /// <summary>Id do work item no fim da URL <c>.../_apis/wit/workItems/123</c> (null para anexos/links externos).</summary>
    public static int? WorkItemIdFromUrl(string url)
    {
        const string marker = "/_apis/wit/workItems/";
        var i = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var tail = url[(i + marker.Length)..];
        var end = tail.IndexOfAny(['/', '?']);
        return int.TryParse(end < 0 ? tail : tail[..end], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    /// <summary>Work item do <c>workitemsbatch</c> → cartão do board (coluna pelo campo do board; sem ele, pelo estado).</summary>
    public static DevOpsBoardItem ParseBoardItem(JsonElement workItem, DevOpsBoardConfig config)
    {
        var fields = CloneFields(workItem);
        var type = FieldText(fields, FieldType) ?? string.Empty;
        var state = FieldText(fields, FieldState) ?? string.Empty;
        var column = ColumnOf(fields, type, state, config);
        var lane = config.Fields.Lane is { } lf ? FieldText(fields, lf) : null;
        return new DevOpsBoardItem
        {
            Id = workItem.TryGetProperty("id", out var id) && id.TryGetInt32(out var idv) ? idv : 0,
            Rev = workItem.TryGetProperty("rev", out var rev) && rev.TryGetInt32(out var rv) ? rv : 0,
            Type = type,
            Title = FieldText(fields, FieldTitle) ?? string.Empty,
            State = state,
            AssignedTo = Identity(fields, FieldAssignedTo),
            Tags = SplitTags(FieldText(fields, FieldTags)),
            Column = column,
            Done = config.Fields.Done is { } df && fields.TryGetValue(df, out var d) && d.ValueKind == JsonValueKind.True,
            Lane = string.IsNullOrEmpty(lane) ? config.Lanes.FirstOrDefault(l => l.IsDefault)?.Name ?? string.Empty : lane,
            Order = config.OrderField is { } of && fields.TryGetValue(of, out var o) && o.ValueKind == JsonValueKind.Number ? o.GetDouble() : null,
            Priority = fields.TryGetValue(FieldPriority, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var pv) ? pv : null,
            ParentId = fields.TryGetValue(FieldParent, out var pa) && pa.ValueKind == JsonValueKind.Number && pa.TryGetInt32(out var pav) ? pav : null,
            ChangedDate = fields.TryGetValue(FieldChangedDate, out var cd) && cd.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(cd.GetString(),
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var cdv) ? cdv : null
        };
    }

    /// <summary>Coluna do card no board: o campo de coluna do board; sem ele (card de outro time), a coluna do estado.</summary>
    public static string? ColumnOf(IReadOnlyDictionary<string, JsonElement> fields, string type, string state, DevOpsBoardConfig config)
    {
        var column = config.Fields.Column is { } cf ? FieldText(fields, cf) : null;
        if (!string.IsNullOrEmpty(column) && FindColumn(config, column) is not null)
            return column;
        return config.Columns.FirstOrDefault(c => c.StateMappings.TryGetValue(type, out var s) && string.Equals(s, state, StringComparison.OrdinalIgnoreCase))?.Name
               ?? column;
    }

    public static string? CurrentColumn(DevOpsCardDetail card, DevOpsBoardConfig config) => ColumnOf(card.Fields, card.Type, card.State, config);

    /// <summary>Campos que o board pede no <c>workitemsbatch</c>.</summary>
    public static List<string> BoardItemFields(DevOpsBoardConfig config)
    {
        var fields = new List<string> { FieldId, FieldType, FieldTitle, FieldState, FieldAssignedTo, FieldTags, FieldPriority, FieldParent, FieldChangedDate };
        foreach (var f in new[] { config.Fields.Column, config.Fields.Lane, config.Fields.Done, config.OrderField })
            if (!string.IsNullOrEmpty(f) && !fields.Contains(f, StringComparer.OrdinalIgnoreCase))
                fields.Add(f);
        return fields;
    }

    public static DevOpsIdentityRef? Identity(IReadOnlyDictionary<string, JsonElement> fields, string field)
    {
        if (!fields.TryGetValue(field, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Object)
            return Str(v, "displayName") is { Length: > 0 } name ? new DevOpsIdentityRef(name, Str(v, "uniqueName"), Str(v, "imageUrl")) : null;
        return v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } text ? new DevOpsIdentityRef(text, null, null) : null;
    }

    // ---------------------------------------------------------------- utilidades

    public static string? FieldText(IReadOnlyDictionary<string, JsonElement> fields, string field) =>
        fields.TryGetValue(field, out var v) ? ValueText(v) : null;

    public static bool HasValue(IReadOnlyDictionary<string, JsonElement> fields, string field) =>
        fields.TryGetValue(field, out var v) && !IsNull(v) && !(v.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(v.GetString()));

    private static bool IsNull(JsonElement v) => v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    private static string? ValueText(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Object => Str(v, "displayName") ?? v.GetRawText(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => v.GetRawText()
    };

    private static IEnumerable<JsonElement> Values(JsonElement root) =>
        root.ValueKind == JsonValueKind.Array ? root.EnumerateArray()
        : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray()
        : [];

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? RefName(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var f) && f.ValueKind == JsonValueKind.Object ? Str(f, "referenceName") : null;

    private static string? Color(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var h = hex.Trim();
        // O DevOps manda ARGB (FFCC293D) ou RGB (CC293D), sem #.
        if (h.StartsWith('#')) return h;
        if (h.Length == 8) h = h[2..];
        return "#" + h;
    }

    private static string ColumnType(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.TryGetInt32(out var n) ? n switch { 0 => "incoming", 2 => "outgoing", _ => "inProgress" } : "inProgress",
        JsonValueKind.String => (value.GetString() ?? string.Empty).ToLowerInvariant() switch
        {
            "incoming" => "incoming",
            "outgoing" => "outgoing",
            _ => "inProgress"
        },
        _ => "inProgress"
    };
}
