using System.Text;
using System.Text.Json;

namespace Cime.ExecutionAgent;

/// <summary>
/// Registro da máquina no PRMake e o dono do executor. A credencial do executor nasce da api-key de quem registrou; se a
/// api-key da máquina (a das skills) passar a ser de outra pessoa, o executor se registra de novo para ela — senão
/// continuaria pegando os pedidos de quem estava antes e a pessoa nova veria "Nenhuma máquina registrada".
/// </summary>
public static class Registration
{
    /// <summary>Registra (ou registra de novo) a máquina com a api-key e grava a credencial nova no config.</summary>
    public static async Task<RegistrationResponse> RegisterAsync(AgentConfig config, string userToken, string? name, CancellationToken ct)
    {
        using var client = new PrmakeClient(ConfigStore.ResolveApiBase(config), userToken);
        var registration = await client.RegisterAsync(new RegisterRequest
        {
            Host = Paths.HostName,
            Name = name ?? config.Name,
            Os = Paths.OsDescription,
            AgentVersion = Agent.Version
        }, ct);
        config.Token = registration.Token;
        config.WorkerId = registration.Worker.Id;
        config.Name = registration.Worker.Name;
        ConfigStore.Save(config);
        return registration;
    }

    /// <summary>
    /// A api-key da máquina é de outra pessoa que a credencial do executor? (Comparação pelo <c>ExternalId</c> dos dois
    /// tokens; sem algum deles ou sem o claim, não dá para afirmar — false.)
    /// </summary>
    public static bool OwnerChanged(string? executorToken, string? userToken)
    {
        var executorOwner = ExternalIdOf(executorToken);
        var userOwner = ExternalIdOf(userToken);
        return executorOwner is not null && userOwner is not null
               && !string.Equals(executorOwner, userOwner, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Na partida: dono mudou → registra para o dono novo. Falha só é logada (segue com a credencial antiga).</summary>
    public static async Task<AgentConfig> EnsureOwnerAsync(AgentConfig config, CancellationToken ct)
    {
        var userToken = ConfigStore.UserToken();
        if (config.Token is null || userToken is null || !OwnerChanged(config.Token, userToken)) return config;
        try
        {
            var registration = await RegisterAsync(config, userToken, null, ct);
            Log.Info($"a api-key desta máquina agora é de outra pessoa — executor registrado para ela ({registration.Worker.Name}, {registration.Worker.Id})");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Warn($"a api-key desta máquina é de outra pessoa, mas o registro para ela falhou: {e.Message} — rode: prmake-skills.sh agent install");
        }
        return config;
    }

    /// <summary>Claim <c>ExternalId</c> do JWT (só lê o payload; a validação é do servidor).</summary>
    public static string? ExternalIdOf(string? jwt)
    {
        var parts = jwt?.Trim().Split('.');
        if (parts is not { Length: >= 2 }) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return doc.RootElement.TryGetProperty("ExternalId", out var id) && id.ValueKind == JsonValueKind.String
                   && id.GetString() is { Length: > 0 } value
                ? value
                : null;
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
