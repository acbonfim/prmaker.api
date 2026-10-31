using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using solvace.prform.Controllers;
using solvace.prform.Knowledge;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0074: a engenharia reversa é só de administrador, no backend — o controller e o acesso que a Base de conhecimento consulta.</summary>
public class ReverseAccessTests
{
    private static IHttpContextAccessor Accessor(params string[] roles)
    {
        var identity = new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test");
        return new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
    }

    [Fact]
    public void Reverse_engineering_controller_requires_the_admin_role_on_every_endpoint()
    {
        var type = typeof(ReverseEngineeringController);
        var classAdmin = type.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any(a => (a.Roles ?? string.Empty).Split(',').Select(r => r.Trim()).Contains("admin"));
        Assert.True(classAdmin, "ReverseEngineeringController precisa de [Authorize(Roles = \"admin\")]");

        var actions = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().Any(a => a is HttpMethodAttribute)).ToList();
        Assert.NotEmpty(actions);
        // nenhum endpoint libera anônimo nem troca a exigência por uma mais fraca
        Assert.DoesNotContain(actions, a => a.GetCustomAttributes<AllowAnonymousAttribute>(true).Any());
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("admin,gestor", true)]
    [InlineData("support", false)]
    [InlineData("user", false)]
    [InlineData("gestor", false)]
    [InlineData("", false)]
    public void Only_the_admin_role_can_read_the_reverse_engineering(string roles, bool expected)
    {
        var access = new HttpReverseAccess(Accessor(roles.Split(',', StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal(expected, access.CanRead);
    }

    [Fact]
    public void Outside_a_request_there_is_no_restriction_for_background_work()
    {
        Assert.True(new HttpReverseAccess(new HttpContextAccessor { HttpContext = null }).CanRead);
    }
}
