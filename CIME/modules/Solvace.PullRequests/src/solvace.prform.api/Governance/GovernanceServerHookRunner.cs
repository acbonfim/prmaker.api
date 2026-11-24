using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using solvace.executionplans.application.Contracts;
using solvace.governance.application.Contracts;
using solvace.governance.domain;
using solvace.governance.domain.Entities;

namespace solvace.prform.Governance;

/// <summary>
/// 0098: gatilhos do servidor (<c>server:*</c>) — rodam depois que o lote da trilha foi gravado, fora da requisição:
/// <c>timeline</c> anota no card (só eventos com card) e <c>webhook</c> manda o evento em JSON (https, assinatura
/// HMAC-SHA256 em <c>X-PRMake-Signature</c> quando há <c>config.secret</c>).
/// </summary>
public sealed class GovernanceServerHookRunner(IExecutionTimelineWriter timeline, IHttpClientFactory http) : IGovernanceServerHookRunner
{
    public const string HttpClientName = "governance-webhook";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(GovernanceHook hook, GovernanceEvent evt, CancellationToken ct)
    {
        switch (hook.Action)
        {
            case GovernanceHookAction.Timeline when evt.CardNumber is { } card:
                var template = hook.Config.GetValueOrDefault("template") is { Length: > 0 } t ? t : "Governança: {user} — {action} ({outcome})";
                await timeline.WriteAsync(card.ToString(), Fill(template, evt), evt.UserId, "Governança do PRMake", ct);
                break;
            case GovernanceHookAction.Webhook when hook.Config.GetValueOrDefault("url") is { Length: > 0 } url:
                var body = JsonSerializer.Serialize(new { hook = hook.Key, evt }, Json);
                using (var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") })
                {
                    if (hook.Config.GetValueOrDefault("secret") is { Length: > 0 } secret)
                        request.Headers.Add("X-PRMake-Signature",
                            "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant());
                    using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct);
                }
                break;
        }
    }

    public static string Fill(string template, GovernanceEvent e) => template
        .Replace("{user}", e.UserName)
        .Replace("{action}", e.Action)
        .Replace("{outcome}", e.Outcome)
        .Replace("{card}", e.CardNumber?.ToString() ?? "-")
        .Replace("{source}", e.Source)
        .Replace("{rules}", string.Join(", ", e.Rules))
        .Replace("{reason}", e.Detail ?? "-")
        .Replace("{duration}", e.DurationMs is { } ms ? $"{ms} ms" : "-");
}
