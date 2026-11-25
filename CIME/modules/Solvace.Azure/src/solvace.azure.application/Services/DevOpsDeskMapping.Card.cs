using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using solvace.azure.domain.Models;

namespace solvace.azure.application.Services;

/// <summary>
/// Ajustes 3 da 0099 (modal do card): discussion (comentários com autor), anexos (os do card e os colados nos campos/discussion,
/// pelo mesmo leitor das evidências da 0076) e as menções do DevOps no comentário novo (<c>#123</c> e <c>@Nome</c>).
/// </summary>
public static partial class DevOpsDeskMapping
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"];

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp",
        [".bmp"] = "image/bmp", [".svg"] = "image/svg+xml", [".pdf"] = "application/pdf", [".txt"] = "text/plain", [".log"] = "text/plain",
        [".csv"] = "text/csv", [".json"] = "application/json", [".xml"] = "application/xml", [".html"] = "text/html", [".htm"] = "text/html",
        [".md"] = "text/markdown", [".sql"] = "text/plain", [".zip"] = "application/zip", [".mp4"] = "video/mp4", [".webm"] = "video/webm",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    /// <summary>
    /// Campos obrigatórios do tipo que o pedido deixou vazios e que o DevOps não preenche sozinho: não são <c>System.*</c> (título,
    /// estado, área e iteração o PRMake/DevOps preenchem), nem somente leitura, nem têm valor padrão (ex.: <c>Custom.Module</c>).
    /// </summary>
    public static IReadOnlyList<DevOpsFieldInfo> MissingRequired(IEnumerable<DevOpsFieldInfo> typeFields, IReadOnlyDictionary<string, JsonElement>? fields) =>
        typeFields.Where(f => f.Required && !f.ReadOnly && string.IsNullOrWhiteSpace(f.DefaultValue)
                              && !f.ReferenceName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                              && !HasFieldValue(fields, f.ReferenceName))
            .ToList();

    private static bool HasFieldValue(IReadOnlyDictionary<string, JsonElement>? fields, string referenceName)
    {
        if (fields is null) return false;
        var match = fields.FirstOrDefault(kv => string.Equals(kv.Key, referenceName, StringComparison.OrdinalIgnoreCase));
        if (match.Key is null) return false;
        return match.Value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(match.Value.GetString()),
            _ => true
        };
    }

    /// <summary>Imagem que a tela mostra/o modelo lê como imagem (svg fica de fora).</summary>
    public static bool IsImageName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && ImageExtensions.Contains(Path.GetExtension(name.Trim()), StringComparer.OrdinalIgnoreCase);

    /// <summary>Content-Type pelo nome (quando o DevOps devolve <c>application/octet-stream</c>).</summary>
    public static string ContentTypeFromName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && ContentTypes.TryGetValue(Path.GetExtension(name.Trim()), out var t) ? t : "application/octet-stream";

    /// <summary>
    /// <c>…/comments</c> do DevOps → discussion: sem os excluídos, mais novos primeiro; com <c>format=markdown</c> usa o
    /// <c>renderedText</c> (HTML). <c>Count</c> = <c>totalCount</c> (ou o tamanho da lista).
    /// </summary>
    public static DevOpsCommentsResponse ParseComments(JsonElement root)
    {
        var list = new List<DevOpsComment>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("comments", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var c in arr.EnumerateArray())
            {
                if (Bool(c, "isDeleted")) continue;
                var markdown = string.Equals(Str(c, "format"), "markdown", StringComparison.OrdinalIgnoreCase);
                var html = (markdown && Str(c, "renderedText") is { Length: > 0 } rendered ? rendered : Str(c, "text")) ?? string.Empty;
                var created = DateOf(c, "createdDate");
                var modified = DateOf(c, "modifiedDate");
                list.Add(new DevOpsComment(
                    c.TryGetProperty("id", out var id) && id.TryGetInt32(out var idv) ? idv : 0,
                    html,
                    PersonOf(c, "createdBy"),
                    created,
                    modified,
                    created is { } cd && modified is { } md && md - cd > TimeSpan.FromSeconds(2)));
            }
        list = list.OrderByDescending(c => c.CreatedDate ?? DateTimeOffset.MinValue).ThenByDescending(c => c.Id).ToList();
        var total = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("totalCount", out var t) && t.TryGetInt32(out var tv) ? tv : list.Count;
        return new DevOpsCommentsResponse(Math.Max(total, list.Count), list);
    }

    /// <summary>Comentários no formato do leitor de evidências (0076).</summary>
    public static List<AzureCardComment> EvidenceComments(IEnumerable<DevOpsComment> comments) =>
        comments.Select(c => new AzureCardComment
        {
            Id = c.Id, Text = c.Html, CreatedByName = c.Author?.DisplayName, CreatedDate = c.CreatedDate, ModifiedDate = c.ModifiedDate
        }).ToList();

    /// <summary>Anexos do card (work item <c>$expand=all</c> + comentários) pelo leitor das evidências: anexos, campos, discussion.</summary>
    public static List<DevOpsAttachmentInfo> ParseAttachments(JsonElement workItem, IEnumerable<AzureCardComment> comments) =>
        AzureCardEvidenceParser.Parse(workItem, comments)
            .Select(e => new DevOpsAttachmentInfo(e.Key, e.FileName, e.Url, e.Source, e.SourceDetail, e.At, e.Size, e.Comment, IsImageName(e.FileName)))
            .ToList();

    // ---------------------------------------------------------------- menções no comentário

    /// <summary>Pessoa citada como <c>@DisplayName</c> com o GUID da identidade no DevOps.</summary>
    public sealed record MentionTarget(string DisplayName, string Id);

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"^<\s*(/?)\s*([a-zA-Z0-9]+)")]
    private static partial Regex TagName();

    private const string CardMention = @"(?<![\p{L}\p{N}_&/#])#(?<card>\d{1,7})(?!\d)";

    /// <summary>
    /// Menções do DevOps no HTML do comentário, só no texto (fora de tags e de <c>&lt;a&gt;</c>, <c>&lt;code&gt;</c>, <c>&lt;pre&gt;</c>):
    /// <c>#123</c> → link de card (<c>data-vss-mention="version:1.0"</c>) e <c>@Nome</c> de cada pessoa (nome mais longo primeiro,
    /// sem diferenciar caixa) → <c>data-vss-mention="version:2.0,{guid}"</c>. Entidades (<c>&amp;#39;</c>) não viram card.
    /// </summary>
    public static string LinkMentions(string html, Func<int, string> cardUrl, IReadOnlyList<MentionTarget>? people = null)
    {
        if (string.IsNullOrEmpty(html)) return html;
        var targets = (people ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.DisplayName) && Guid.TryParse(p.Id, out _))
            .GroupBy(p => EncodeText(p.DisplayName.Trim()), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Encoded: g.Key, Id: g.First().Id))
            .OrderByDescending(p => p.Encoded.Length)
            .ToList();
        var pattern = targets.Count == 0
            ? CardMention
            : $@"(?<person>@(?:{string.Join('|', targets.Select(t => Regex.Escape(t.Encoded)))}))(?![\p{{L}}\p{{N}}_])|{CardMention}";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var sb = new StringBuilder(html.Length + 64);
        var skip = 0;
        var last = 0;
        foreach (Match tag in HtmlTag().Matches(html))
        {
            if (tag.Index > last)
                sb.Append(skip > 0 ? html[last..tag.Index] : Replace(html[last..tag.Index]));
            sb.Append(tag.Value);
            var name = TagName().Match(tag.Value);
            if (name.Success && name.Groups[2].Value.ToLowerInvariant() is "a" or "code" or "pre")
                skip = Math.Max(0, skip + (name.Groups[1].Value == "/" ? -1 : 1));
            last = tag.Index + tag.Length;
        }
        if (last < html.Length)
            sb.Append(skip > 0 ? html[last..] : Replace(html[last..]));
        return sb.ToString();

        string Replace(string text) => regex.Replace(text, m =>
        {
            if (m.Groups["person"].Success)
            {
                var target = targets.First(t => string.Equals("@" + t.Encoded, m.Value, StringComparison.OrdinalIgnoreCase));
                return $"<a href=\"#\" data-vss-mention=\"version:2.0,{target.Id.ToLowerInvariant()}\">{m.Value}</a>";
            }
            var card = int.Parse(m.Groups["card"].Value, CultureInfo.InvariantCulture);
            return card <= 0 ? m.Value : $"<a href=\"{WebUtility.HtmlEncode(cardUrl(card))}\" data-vss-mention=\"version:1.0\">#{card}</a>";
        });
    }

    /// <summary>Texto como o Markdig escreve no HTML (só <c>&amp; &lt; &gt; "</c>; acentos ficam como estão).</summary>
    private static string EncodeText(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>HTML de um comentário → texto curto (para o modelo).</summary>
    public static string HtmlToText(string? html, int max = 1500)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = Regex.Replace(html, @"<\s*(br|/p|/div|/li|/h\d)\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<img[^>]*>", "[imagem]", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(HtmlTag().Replace(text, string.Empty));
        text = Regex.Replace(text, @"[ \t ]+", " ");
        text = Regex.Replace(text, @"\n\s*\n+", "\n").Trim();
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }

    private static DevOpsPersonInfo? PersonOf(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Object) return null;
        var display = Str(p, "displayName")?.Trim();
        if (string.IsNullOrEmpty(display)) return null;
        var image = Str(p, "imageUrl")
                    ?? (p.TryGetProperty("_links", out var l) && l.ValueKind == JsonValueKind.Object && l.TryGetProperty("avatar", out var a) ? Str(a, "href") : null);
        return new DevOpsPersonInfo(display, Str(p, "uniqueName") ?? string.Empty, Str(p, "id") ?? string.Empty, image);
    }

    private static DateTimeOffset? DateOf(JsonElement e, string name) =>
        Str(e, name) is { Length: > 0 } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
