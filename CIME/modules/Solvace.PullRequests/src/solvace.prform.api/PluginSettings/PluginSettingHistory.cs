using System.Security.Claims;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Entities;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.PluginSettings;

/// <summary>
/// 0086: grava o histórico das chaves de plugin no mesmo contexto da alteração (sai junto no SaveChanges). Segredo
/// (pelo catálogo ou pelo nome) vai mascarado: "********" com valor, "" vazio.
/// </summary>
public class PluginSettingHistory(DefaultContext db, IHttpContextAccessor http) : IPluginSettingHistory
{
    public void Track(Plugin plugin, IDictionary<string, string>? before, IDictionary<string, string>? after)
    {
        var who = CurrentUser(http.HttpContext?.User);
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, old, @new) in PluginSettingDiff.Compute(before, after))
        {
            var secret = PluginSettingsCatalog.IsSecret(plugin, key);
            db.PluginSettingChanges.Add(new PluginSettingChange
            {
                PluginId = plugin.Id,
                Key = key.Length > PluginSettingChange.MaxKeyLength ? key[..PluginSettingChange.MaxKeyLength] : key,
                OldValue = secret ? Mask(old) : old,
                NewValue = secret ? Mask(@new) : @new,
                ChangedBy = who,
                ChangedAt = now
            });
        }
    }

    /// <summary>Segredo no histórico: null continua null (não existia/removida), vazio continua vazio, o resto vira a máscara.</summary>
    public static string? Mask(string? value) => value is null ? null : value.Length == 0 ? string.Empty : PluginSettingChange.SecretMask;

    /// <summary>ExternalId do usuário (fallback NameIdentifier/Name) — o mesmo do AuditSaveChangesInterceptor.</summary>
    public static string? CurrentUser(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return null;
        var who = principal.FindFirstValue("ExternalId")
                  ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue(ClaimTypes.Name);
        return who is { Length: > PluginSettingChange.MaxChangedByLength } ? who[..PluginSettingChange.MaxChangedByLength] : who;
    }
}
