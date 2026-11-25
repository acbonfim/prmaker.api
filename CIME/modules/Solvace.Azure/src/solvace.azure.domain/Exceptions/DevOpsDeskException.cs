namespace solvace.azure.domain.Exceptions;

/// <summary>
/// 0099 (Mesa de gestão): falha numa leitura/gravação do DevOps, com o status da API do PRMake e um código estável para a
/// tela — <c>{code, message}</c>. A mensagem é a do DevOps quando ele recusou (ex.: regra de transição de estado).
/// </summary>
public class DevOpsDeskException : DevOpsActionException
{
    public string Code { get; }

    public DevOpsDeskException(int statusCode, string code, string message) : base(statusCode, message)
    {
        Code = code;
    }

    public static DevOpsDeskException Invalid(string message) => new(400, "invalid", message);
    public static DevOpsDeskException NoMapping(string message) => new(409, "no_state_mapping", message);
    public static DevOpsDeskException NotFoundDesk(string message) => new(404, "not_found", message);

    /// <summary>Resposta de erro do DevOps → status do PRMake: 400 recusado, 401/403 sem permissão, 404, 409/412 conflito, resto 502.</summary>
    public static DevOpsDeskException FromDevOps(int devOpsStatus, string message) => devOpsStatus switch
    {
        400 => new(400, "devops_rejected", message),
        401 or 403 => new(403, "devops_forbidden", message),
        404 => new(404, "devops_not_found", message),
        409 or 412 => new(409, "devops_conflict", message),
        _ => new(502, "devops_error", message)
    };
}
