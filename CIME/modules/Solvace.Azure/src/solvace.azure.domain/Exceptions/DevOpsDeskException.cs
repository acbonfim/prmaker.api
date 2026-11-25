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

/// <summary>
/// Campos obrigatórios do tipo, sem valor padrão, que o pedido não preencheu (ex.: <c>Custom.Module</c> na User Story). O PRMake
/// confere antes de criar o card, para não repassar o TF401320 cru do DevOps, e devolve 422 com <c>{code: "required_fields",
/// fields}</c> para a tela pedir cada um (picklist com as opções).
/// </summary>
public class DevOpsRequiredFieldsException(string type, IReadOnlyList<solvace.azure.domain.Models.DevOpsFieldInfo> fields)
    : DevOpsDeskException(422, "required_fields",
        $"O DevOps exige no {type}: {string.Join(", ", fields.Select(f => f.Name))}. Preencha {(fields.Count == 1 ? "o campo" : "os campos")} e tente de novo.")
{
    public IReadOnlyList<solvace.azure.domain.Models.DevOpsFieldInfo> Fields { get; } = fields;
}
