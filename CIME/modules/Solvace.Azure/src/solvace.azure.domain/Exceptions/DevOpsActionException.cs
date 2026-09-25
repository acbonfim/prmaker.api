namespace solvace.azure.domain.Exceptions;

/// <summary>
/// Falha de uma ação no DevOps (feature 0011), com o status HTTP que a API deve devolver:
/// 404 card não encontrado, 409 regra da ação não atendida, 502 erro do próprio DevOps.
/// A mensagem é exibida ao usuário.
/// </summary>
public class DevOpsActionException : Exception
{
    public int StatusCode { get; }

    public DevOpsActionException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static DevOpsActionException NotFound(string message) => new(404, message);
    public static DevOpsActionException Conflict(string message) => new(409, message);
    public static DevOpsActionException Upstream(string message) => new(502, message);
}
