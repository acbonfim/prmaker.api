using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Enums;
using solvace.prform.domain.Extensions;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.application.Teams;

public interface ITeamsApprovalService
{
    /// <summary>Se o plugin existe e se o usuário da requisição já configurou o Workflow dele.</summary>
    Task<TeamsStatusResponse> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Posta no grupo do Teams (Workflow do usuário) o pedido de aprovação do PR. Erros de
    /// validação → DomainException; sem configuração → PersonalIntegrationRequiredException (403).
    /// </summary>
    Task<TeamsApprovalResponse> RequestApprovalAsync(TeamsApprovalRequest request, string authorName, CancellationToken cancellationToken);
}

/// <summary>
/// Pedido de aprovação de PR no Teams (feature 0007) via Workflow "Enviar alertas de webhook
/// para um chat": cada usuário cola a URL do Workflow dele em "Minhas integrações" (plugin pessoal
/// e opcional "Teams Configurations"); o nome do grupo e o modelo da mensagem são do admin.
/// A mensagem vai como Adaptive Card.
/// </summary>
public partial class TeamsApprovalService : ITeamsApprovalService
{
    public const string PluginName = "Teams Configurations";
    public const string HttpClientName = "TeamsWebhook";

    /// <summary>
    /// Só hosts de Workflows/Power Automate (e o webhook legado do Office 365): a URL é informada
    /// pelo usuário e é o servidor que faz o POST — não pode apontar para endereços internos.
    /// </summary>
    private static readonly string[] AllowedHostSuffixes =
        { ".logic.azure.com", ".powerplatform.com", ".powerautomate.com", ".webhook.office.com" };

    private static readonly HashSet<string> TitleColors = new(StringComparer.OrdinalIgnoreCase)
        { "Default", "Dark", "Light", "Accent", "Good", "Warning", "Attention" };

    private const string DefaultTitle = "Aprovação de PR — AB#{cardNumber}";
    private const string DefaultBody = "**{author}** pede aprovação do PR **#{prNumber}**\n\n{repository}: {branch} → {targetBranch}\n\n{prTitle}";
    private const string DefaultButton = "Abrir PR no GitHub";
    private const int DefaultDescriptionMaxLength = 1200;

    private readonly DefaultContext _context;
    private readonly IPluginConfigurationResolver _resolver;
    private readonly IPluginCacheManager _pluginCacheManager;
    private readonly IHttpClientFactory _httpClientFactory;

    public TeamsApprovalService(DefaultContext context, IPluginConfigurationResolver resolver,
        IPluginCacheManager pluginCacheManager, IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _resolver = resolver;
        _pluginCacheManager = pluginCacheManager;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<TeamsStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var plugin = FindPlugin();
        if (plugin is null)
        {
            await _pluginCacheManager.RefreshPluginsAsync(cancellationToken);
            plugin = FindPlugin();
        }
        if (plugin is null)
            return new TeamsStatusResponse { Available = false };

        var status = new TeamsStatusResponse
        {
            Available = true,
            PluginId = plugin.Id,
            GroupName = plugin.Configurations?.GetConfigurationValue("GroupName")?.Trim() ?? string.Empty
        };

        try
        {
            var config = await _resolver.GetEffectiveConfigurationAsync(PluginName, cancellationToken);
            status.Configured = !string.IsNullOrWhiteSpace(config.GetConfigurationValue("WebhookUrl"));
        }
        catch (PersonalIntegrationRequiredException)
        {
            status.Configured = false;
        }

        return status;
    }

    public async Task<TeamsApprovalResponse> RequestApprovalAsync(TeamsApprovalRequest request, string authorName, CancellationToken cancellationToken)
    {
        var cardNumber = request.CardNumber?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(cardNumber) || request.PullRequestGithubId <= 0)
            throw new DomainException("Informe o card e o PR");

        var pr = await _context.PullRequestsGithub.AsNoTracking()
                     .FirstOrDefaultAsync(x => x.Id == request.PullRequestGithubId && x.CardNumber == cardNumber, cancellationToken)
                 ?? throw new DomainException($"PR {request.PullRequestGithubId} não encontrado para o card {cardNumber}");
        if (pr.IsLegacy || string.IsNullOrWhiteSpace(pr.Url))
            throw new DomainException("Registro legado sem PR no GitHub — abra o PR antes de pedir aprovação");
        if (pr.Status != PullRequestGithubStatus.Open || pr.IsDraft)
            throw new DomainException(pr.IsDraft && pr.Status == PullRequestGithubStatus.Open
                ? "O PR está como DRAFT — marque como pronto (OPEN) antes de pedir aprovação"
                : $"Só é possível pedir aprovação de PR aberto (este está {pr.Status})");

        // Fora do try: sem configuração → PersonalIntegrationRequiredException → 403 (0002).
        var config = await _resolver.GetEffectiveConfigurationAsync(PluginName, cancellationToken);
        var webhook = ValidateWebhookUrl(config.GetConfigurationValue("WebhookUrl"));
        var groupName = config.GetConfigurationValue("GroupName")?.Trim() ?? string.Empty;

        var payload = BuildMessage(config, pr, string.IsNullOrWhiteSpace(authorName) ? "Alguém" : authorName.Trim());

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var http = _httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(webhook, content, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DomainException("O Teams não respondeu a tempo — tente de novo em instantes");
        }
        catch (HttpRequestException)
        {
            throw new DomainException("Não foi possível conectar ao Workflow do Teams — confira a URL em Minhas integrações");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                throw new DomainException(status is 401 or 403 or 404
                    ? $"O Workflow do Teams recusou a mensagem ({status}) — a URL pode ter expirado ou o Workflow foi desativado. Gere uma nova e salve em Minhas integrações"
                    : $"O Workflow do Teams recusou a mensagem ({status}) — tente de novo em instantes");
            }
        }

        return new TeamsApprovalResponse { Sent = true, GroupName = groupName };
    }

    /// <summary>Monta o Adaptive Card no formato aceito pelo Workflow ("type": "message" + attachments).</summary>
    public static string BuildMessage(PluginConfiguration config, PullRequestGithub pr, string authorName)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cardNumber"] = pr.CardNumber,
            ["prNumber"] = pr.GithubPrNumber?.ToString() ?? string.Empty,
            ["prTitle"] = pr.Title,
            ["prUrl"] = pr.Url,
            ["repository"] = pr.RepositoryId,
            ["branch"] = pr.BranchPrefix + pr.BranchName,
            ["targetBranch"] = pr.TargetBranch,
            ["author"] = authorName,
            ["status"] = pr.Status,
        };

        var title = Render(ValueOr(config, "MessageTitle", DefaultTitle), values);
        var body = Render(ValueOr(config, "MessageBody", DefaultBody), values);
        var button = Render(ValueOr(config, "ButtonText", DefaultButton), values);
        var color = ValueOr(config, "TitleColor", "Accent");
        if (!TitleColors.Contains(color))
            color = "Default";

        var blocks = new List<object>
        {
            new { type = "TextBlock", text = title, weight = "Bolder", size = "Medium", color, wrap = true },
            new { type = "TextBlock", text = body, wrap = true, spacing = "Small" },
        };

        var includeDescription = bool.TryParse(config.GetConfigurationValue("IncludeDescription"), out var include) && include;
        if (includeDescription && !string.IsNullOrWhiteSpace(pr.Description))
        {
            var max = int.TryParse(config.GetConfigurationValue("DescriptionMaxLength"), out var parsed) && parsed > 0
                ? parsed
                : DefaultDescriptionMaxLength;
            var description = pr.Description.Trim();
            if (description.Length > max)
                description = description[..max].TrimEnd() + "…";
            blocks.Add(new { type = "TextBlock", text = description, wrap = true, isSubtle = true, spacing = "Medium", separator = true });
        }

        var card = new Dictionary<string, object>
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["msteams"] = new { width = "Full" },
            ["body"] = blocks,
            ["actions"] = new[] { new { type = "Action.OpenUrl", title = button, url = pr.Url } },
        };

        return JsonConvert.SerializeObject(new
        {
            type = "message",
            attachments = new[]
            {
                new { contentType = "application/vnd.microsoft.card.adaptive", contentUrl = (string?)null, content = card }
            }
        });
    }

    /// <summary>Substitui {chave} pelos valores do PR; placeholders desconhecidos ficam como estão.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values) =>
        PlaceholderRegex().Replace(template, m =>
            values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);

    /// <summary>URL do Workflow válida (https + host permitido), ou DomainException com a orientação.</summary>
    public static Uri ValidateWebhookUrl(string? value)
    {
        const string help = "Cole em Minhas integrações a URL gerada pelo Workflow do Teams (\"Enviar alertas de webhook para um chat\")";
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            throw new DomainException($"URL do Workflow do Teams inválida. {help}");
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new DomainException($"A URL do Workflow do Teams precisa ser https. {help}");
        var host = uri.IdnHost.ToLowerInvariant();
        if (uri.HostNameType != UriHostNameType.Dns || !AllowedHostSuffixes.Any(s => host.EndsWith(s, StringComparison.Ordinal)))
            throw new DomainException($"A URL não é de um Workflow do Teams (Power Automate). {help}");
        return uri;
    }

    private Plugin? FindPlugin() =>
        _pluginCacheManager.GetCachedPlugins()?
            .FirstOrDefault(p => !p.IsDeleted && string.Equals(p.Description, PluginName, StringComparison.OrdinalIgnoreCase));

    private static string ValueOr(PluginConfiguration config, string key, string fallback)
    {
        var value = config.GetConfigurationValue(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex PlaceholderRegex();
}
