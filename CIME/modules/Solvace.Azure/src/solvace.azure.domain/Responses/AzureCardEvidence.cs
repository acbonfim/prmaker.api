using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace solvace.azure.domain.Models;

/// <summary>
/// 0076: uma evidência do card no DevOps — arquivo anexado ao card ou imagem/arquivo colado num campo de texto
/// (Repro Steps, Descrição...) ou num comentário da discussion. <see cref="Key"/> é o id do anexo no DevOps (o mesmo
/// arquivo citado em dois lugares é uma evidência só).
/// </summary>
public class AzureCardEvidence
{
    public const string FromAttachment = "anexo";
    public const string FromField = "campo";
    public const string FromDiscussion = "discussion";

    public string Key { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    /// <summary>anexo | campo | discussion.</summary>
    public string Source { get; set; } = FromAttachment;
    /// <summary>Onde: nome do campo ("Repro Steps"), autor do comentário ou de quem anexou.</summary>
    public string? SourceDetail { get; set; }
    public DateTimeOffset? At { get; set; }
    public long? Size { get; set; }
    public string? Comment { get; set; }
}

/// <summary>0076: conteúdo de um anexo baixado do DevOps.</summary>
public record AzureAttachmentDownload(byte[] Data, string ContentType, bool TooLarge, long? Size);

/// <summary>0076: lê as evidências do JSON do work item (<c>$expand=all</c>) e dos comentários.</summary>
public static partial class AzureCardEvidenceParser
{
    [GeneratedRegex(@"(?:src|href)\s*=\s*[""']([^""']*/_apis/wit/attachments/([0-9a-fA-F-]{36})[^""']*)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlAttachment();

    [GeneratedRegex(@"/_apis/wit/attachments/([0-9a-fA-F-]{36})", RegexOptions.IgnoreCase)]
    private static partial Regex AttachmentId();

    /// <summary>Campos de texto lidos primeiro e na ordem (o resto dos campos HTML vem depois, por nome).</summary>
    private static readonly (string Field, string Label)[] PreferredFields =
    [
        ("Microsoft.VSTS.TCM.ReproSteps", "Repro Steps"),
        ("System.Description", "Descrição"),
        ("Microsoft.VSTS.TCM.SystemInfo", "System Info"),
        ("Microsoft.VSTS.Common.AcceptanceCriteria", "Critérios de aceite"),
    ];

    public static List<AzureCardEvidence> Parse(JsonElement workItem, IEnumerable<AzureCardComment> comments)
    {
        var list = new List<AzureCardEvidence>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(AzureCardEvidence e)
        {
            if (e.Key.Length > 0 && seen.Add(e.Key)) list.Add(e);
        }

        if (workItem.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
        {
            var html = fields.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String && p.Name != "System.History")
                .Select(p => (p.Name, Value: p.Value.GetString() ?? ""))
                .Where(p => p.Value.Contains("/_apis/wit/attachments/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => Array.FindIndex(PreferredFields, f => f.Field == p.Name) is var i && i >= 0 ? i : PreferredFields.Length)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToList();
            foreach (var (name, value) in html)
            {
                var label = PreferredFields.FirstOrDefault(f => f.Field == name).Label ?? FieldLabel(name);
                foreach (var e in FromHtml(value, AzureCardEvidence.FromField, label, null))
                    Add(e);
            }
        }

        foreach (var c in comments.OrderBy(c => c.CreatedDate ?? DateTimeOffset.MaxValue))
            foreach (var e in FromHtml(c.Text, AzureCardEvidence.FromDiscussion, c.CreatedByName, c.CreatedDate))
                Add(e);

        if (workItem.TryGetProperty("relations", out var relations) && relations.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in relations.EnumerateArray())
            {
                if (!r.TryGetProperty("rel", out var rel) || rel.GetString() != "AttachedFile") continue;
                var url = r.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var key = AttachmentId().Match(url) is { Success: true } m ? m.Groups[1].Value.ToLowerInvariant() : "";
                var attrs = r.TryGetProperty("attributes", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
                Add(new AzureCardEvidence
                {
                    Key = key,
                    Url = url,
                    FileName = SafeName(Str(attrs, "name") ?? $"anexo-{key[..Math.Min(8, key.Length)]}"),
                    Source = AzureCardEvidence.FromAttachment,
                    At = Date(attrs, "resourceCreatedDate") ?? Date(attrs, "authorizedDate"),
                    Size = attrs.ValueKind == JsonValueKind.Object && attrs.TryGetProperty("resourceSize", out var s) && s.TryGetInt64(out var size) ? size : null,
                    Comment = Str(attrs, "comment"),
                });
            }
        }
        return list;
    }

    /// <summary>Evidências citadas num HTML (imagem colada ou link para anexo), na ordem do texto.</summary>
    public static IEnumerable<AzureCardEvidence> FromHtml(string? html, string source, string? detail, DateTimeOffset? at)
    {
        if (string.IsNullOrEmpty(html)) yield break;
        foreach (Match m in HtmlAttachment().Matches(html))
        {
            var url = WebUtility.HtmlDecode(m.Groups[1].Value);
            yield return new AzureCardEvidence
            {
                Key = m.Groups[2].Value.ToLowerInvariant(),
                Url = url,
                FileName = SafeName(QueryValue(url, "fileName") ?? "image.png"),
                Source = source,
                SourceDetail = detail,
                At = at,
            };
        }
    }

    /// <summary>Só baixa anexo do DevOps da organização configurada (nada de URL arbitrária vinda do texto do card).</summary>
    public static bool IsAllowedDownload(string url, string organization)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!uri.AbsolutePath.Contains("/_apis/wit/attachments/", StringComparison.OrdinalIgnoreCase)) return false;
        var org = organization.Trim();
        return (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.StartsWith($"/{org}/", StringComparison.OrdinalIgnoreCase))
               || uri.Host.Equals($"{org}.visualstudio.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string FieldLabel(string field)
    {
        var last = field.Split('.').Last();
        return Regex.Replace(last, "(?<=[a-z])(?=[A-Z])", " ");
    }

    private static string? QueryValue(string url, string name)
    {
        var q = url.IndexOf('?');
        if (q < 0) return null;
        foreach (var part in url[(q + 1)..].Split('&'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals(name, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1].Replace('+', ' '));
        }
        return null;
    }

    /// <summary>Nome de arquivo seguro (sem pasta, sem caracteres de controle), com no máximo 120 caracteres.</summary>
    public static string SafeName(string name)
    {
        var n = Path.GetFileName(name.Replace('\\', '/')).Trim();
        n = Regex.Replace(n, @"[^\w.\- ()]+", "_");
        if (n.Length == 0) n = "arquivo";
        if (n.Length > 120)
        {
            var ext = Path.GetExtension(n);
            n = n[..(120 - ext.Length)] + ext;
        }
        return n;
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? Date(JsonElement obj, string name) =>
        Str(obj, name) is { } s && DateTimeOffset.TryParse(s, out var d) ? d : null;
}
