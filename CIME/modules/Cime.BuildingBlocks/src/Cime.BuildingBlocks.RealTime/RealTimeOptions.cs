namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Configurações do canal de tempo real (WebSocket/SignalR).
/// Lidas da seção "RealTime" do appsettings.
/// </summary>
public class RealTimeOptions
{
    public const string SectionName = "RealTime";

    /// <summary>
    /// <see cref="RealTimeModes.InProcess"/> (padrão): o hub roda dentro da própria API.
    /// <see cref="RealTimeModes.Relay"/>: o hub roda num relay externo (Cime.RealTime.Relay) e a API
    /// só publica por HTTP — no Cloud Run, WebSocket aberto na API mantém a instância cobrando 24 h.
    /// Lido só do ambiente (não do plugin): decide o que é registrado no startup.
    /// </summary>
    public string Mode { get; set; } = RealTimeModes.InProcess;

    /// <summary>Caminho onde o hub é mapeado (default: /ws).</summary>
    public string HubPath { get; set; } = "/ws";


    /// <summary>
    /// Origens permitidas para a policy de CORS do hub. Quando vazia, qualquer origem é aceita
    /// (com credenciais). Restringir em produção.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

    /// <summary>Modo Relay: URL base do relay (ex.: https://realtime.softhouse.app.br).</summary>
    public string? RelayUrl { get; set; }

    /// <summary>Modo Relay: chave servidor-a-servidor enviada no header X-Relay-Key do /publish.</summary>
    public string? RelayKey { get; set; }

    /// <summary>
    /// Chave HMAC-SHA256 (mínimo 32 caracteres) dos tokens de conexão do navegador, compartilhada
    /// com o relay. Vazia => a API não emite token e o hub em processo fica aberto (só no dev).
    /// </summary>
    public string? TokenSigningKey { get; set; }

    /// <summary>Validade do token de conexão, em minutos (default: 10).</summary>
    public int TokenLifetimeMinutes { get; set; } = 10;

    /// <summary>
    /// URL do hub informada ao navegador. Vazia => no modo Relay, <see cref="RelayUrl"/> + <see cref="HubPath"/>;
    /// no modo em processo, o front usa a URL do próprio environment.
    /// </summary>
    public string? PublicHubUrl { get; set; }

    public bool IsRelay => string.Equals(Mode, RealTimeModes.Relay, StringComparison.OrdinalIgnoreCase);
}

public static class RealTimeModes
{
    public const string InProcess = "InProcess";
    public const string Relay = "Relay";
}
