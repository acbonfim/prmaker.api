namespace solvace.azure.domain.Options;

/// <summary>
/// 0105: chaves do SLA dos cards no plugin "AzureDevOps Configurations" (seção SLA das Configurações). Sem valor, vale o
/// padrão de <c>CardSlaSettings</c> — o mesmo do catálogo.
/// </summary>
public static class CardSlaKeys
{
    public const string Enabled = "SlaEnabled";
    /// <summary>Tabela [{origin: client|internal, severity, days}] — prazo máximo em dias úteis.</summary>
    public const string Rules = "SlaRules";
    /// <summary>Faltando até N dias úteis (ou vencido) = grave.</summary>
    public const string GraveMaxDays = "SlaGraveMaxDays";
    /// <summary>Faltando até N dias úteis = atenção; acima = ok.</summary>
    public const string AttentionMaxDays = "SlaAttentionMaxDays";
    public const string WorkItemTypes = "SlaWorkItemTypes";
    /// <summary>Campo que diz quem abriu o bug (Found by).</summary>
    public const string OriginField = "SlaOriginField";
    /// <summary>Valores do campo de origem que contam como time interno; os demais = cliente.</summary>
    public const string InternalValues = "SlaInternalValues";
    public const string SeverityField = "SlaSeverityField";
    /// <summary>Campos de data de abertura, na ordem (o primeiro preenchido vale).</summary>
    public const string StartFields = "SlaStartFields";
    /// <summary>Estados em que o prazo para de contar (entregue).</summary>
    public const string StopStates = "SlaStopStates";
    /// <summary>Feriados (yyyy-MM-dd) fora da contagem de dias úteis.</summary>
    public const string Holidays = "SlaHolidays";
    public const string TimeZone = "SlaTimeZone";
}
