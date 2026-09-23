namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Configurações do canal de tempo real (WebSocket/SignalR).
/// Lidas da seção "RealTime" do appsettings.
/// </summary>
public class RealTimeOptions
{
    public const string SectionName = "RealTime";

    /// <summary>Caminho onde o hub é mapeado (default: /ws).</summary>
    public string HubPath { get; set; } = "/ws";

    /// <summary>
    /// Chave compartilhada exigida para conectar ao hub. Quando vazia, o gate é desligado
    /// (útil em ambientes locais). Aceita via header "x-api-key" ou query string
    /// ("x-api-key" / "access_token").
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Origens permitidas para a policy de CORS do hub. Quando vazia, qualquer origem é aceita
    /// (com credenciais). Restringir em produção.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}
