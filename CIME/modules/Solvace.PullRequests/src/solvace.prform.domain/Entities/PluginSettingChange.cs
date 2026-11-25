namespace solvace.prform.domain.Entities;

/// <summary>
/// Histórico de alterações das chaves de plugin (0086): uma linha por chave que mudou — pela tela nova de configurações
/// (PATCH settings) ou pelo PUT update-configuration antigo. Segredo nunca é gravado aqui: vai "********" (ou "" quando
/// vazio). OldValue null = a chave não existia; NewValue null = a chave foi removida.
/// </summary>
public class PluginSettingChange
{
    public const int MaxKeyLength = 200;
    public const int MaxChangedByLength = 100;
    public const string SecretMask = "********";

    public long Id { get; set; }
    public int PluginId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    /// <summary>ExternalId de quem alterou (o mesmo do AuditSaveChangesInterceptor); null = sem usuário.</summary>
    public string? ChangedBy { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}
