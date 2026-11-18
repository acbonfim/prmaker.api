using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using solvace.prform.Controllers;
using solvace.prform.Knowledge;
using Xunit;

namespace solvace.prform.tests;

/// <summary>
/// 0097: a engenharia reversa é lida por todo usuário autenticado; gerar continua de administrador (ação por ação no
/// controller) e aprovar/publicar/configurar continua com ReverseEngineeringApproverRoles (na aplicação). 0074 restringia
/// tudo a administrador.
/// </summary>
public class ReverseAccessTests
{
    private static IHttpContextAccessor Accessor(params string[] roles)
    {
        var identity = new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test");
        return new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
    }

    /// <summary>Ações de geração: só administrador.</summary>
    private static readonly string[] Generation =
    [
        "StartSession", "Lint", "SaveRevision", "Progress", "Submit", "AddLink", "AddFile", "DeleteAsset", "UpsertInfra",
        "CreateTraps", "UpdateTrap", "DeleteTrap", "TrapsMigrated", "SuggestionToTrap", "LinkSuggestion", "TechnicalIndex", "MarkStale"
    ];

    private static bool HasAdmin(MemberInfo member) =>
        member.GetCustomAttributes<AuthorizeAttribute>(true).Any(a => (a.Roles ?? string.Empty).Split(',').Select(r => r.Trim()).Contains("admin"));

    [Fact]
    public void The_controller_requires_authentication_and_only_the_generation_actions_require_admin()
    {
        var type = typeof(ReverseEngineeringController);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(true), a => string.IsNullOrEmpty(a.Roles));
        Assert.False(HasAdmin(type), "a leitura é aberta a todo usuário autenticado — o admin vai só nas ações de geração");

        var actions = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().Any(a => a is HttpMethodAttribute)).ToList();
        Assert.NotEmpty(actions);
        Assert.DoesNotContain(actions, a => a.GetCustomAttributes<AllowAnonymousAttribute>(true).Any());

        foreach (var name in Generation)
        {
            var action = actions.SingleOrDefault(a => a.Name == name);
            Assert.True(action is not null, $"ação {name} não encontrada");
            Assert.True(HasAdmin(action!), $"{name} é geração: precisa de [Authorize(Roles = \"admin\")]");
        }
        // leitura, revisão, publicação e configuração: sem papel no atributo (aprovar/publicar/configurar checam ApproverRoles na aplicação)
        foreach (var name in new[] { "Modules", "Module", "Doc", "Search", "Items", "Impact", "ForCard", "Traps", "Review", "Publish", "UpsertModule", "Consulted", "GetCardItem" })
            Assert.False(HasAdmin(actions.Single(a => a.Name == name)), $"{name} não é geração");
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("admin,gestor")]
    [InlineData("support")]
    [InlineData("user")]
    [InlineData("gestor")]
    [InlineData("")]
    public void Every_authenticated_user_reads_the_reverse_engineering(string roles)
    {
        var access = new HttpReverseAccess(Accessor(roles.Split(',', StringSplitOptions.RemoveEmptyEntries)));
        Assert.True(access.CanRead);
    }

    [Fact]
    public void An_anonymous_request_does_not_read()
    {
        var anonymous = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) } };
        Assert.False(new HttpReverseAccess(anonymous).CanRead);
    }

    [Fact]
    public void Outside_a_request_there_is_no_restriction_for_background_work()
    {
        Assert.True(new HttpReverseAccess(new HttpContextAccessor { HttpContext = null }).CanRead);
    }
}
