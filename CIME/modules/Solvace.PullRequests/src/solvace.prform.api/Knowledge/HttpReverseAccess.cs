using solvace.knowledge.application.Contracts;

namespace solvace.prform.Knowledge;

/// <summary>
/// 0097: a engenharia reversa é lida por todo usuário autenticado (a tela, os itens, a busca, o MCP, o <c>for-card</c>, o
/// Pergunte e as armadilhas) — gerar continua de administrador (no controller) e aprovar/publicar/configurar continua com
/// <c>ReverseEngineeringApproverRoles</c>. (0074 restringia a leitura a administradores.) Fora de uma requisição (tarefa
/// de fundo) não há restrição.
/// </summary>
public sealed class HttpReverseAccess(IHttpContextAccessor http) : IReverseAccess
{
    public const string AdminRole = "admin";

    public bool CanRead => http.HttpContext is not { } context || context.User.Identity?.IsAuthenticated == true;
}
