using solvace.knowledge.application.Contracts;

namespace solvace.prform.Knowledge;

/// <summary>
/// 0074: a engenharia reversa (a tela, a geração, os itens e o texto que ela publica na Base) é só de administrador — o papel
/// vem do usuário da requisição (api-key do usuário ou credencial do executor, que leva o papel do dono). Fora de uma
/// requisição (tarefa de fundo) não há restrição.
/// </summary>
public sealed class HttpReverseAccess(IHttpContextAccessor http) : IReverseAccess
{
    public const string AdminRole = "admin";

    public bool CanRead => http.HttpContext is not { } context || context.User.IsInRole(AdminRole);
}
