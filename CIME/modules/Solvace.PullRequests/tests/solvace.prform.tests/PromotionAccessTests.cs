using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using solvace.prform.Controllers;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0074: a promoção de PRs é só de administrador — nenhum endpoint pode escapar dessa exigência.</summary>
public class PromotionAccessTests
{
    private static bool RequiresAdmin(ICustomAttributeProvider provider) =>
        provider.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Any(a => (a.Roles ?? string.Empty).Split(',').Select(r => r.Trim()).Contains("admin", StringComparer.OrdinalIgnoreCase));

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().Any(a => a is HttpMethodAttribute));

    [Fact]
    public void Every_user_facing_promotion_endpoint_requires_admin()
    {
        Assert.True(RequiresAdmin(typeof(PromotionController)), "PromotionController precisa de [Authorize(Roles = \"admin\")]");

        var actions = Actions(typeof(PromotionController)).ToList();
        Assert.NotEmpty(actions);
        Assert.DoesNotContain(actions, a => a.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Any());
    }

    [Fact]
    public void Executor_endpoints_that_change_state_require_admin_and_next_hides_the_queue_from_non_admins()
    {
        var actions = Actions(typeof(PromotionExecutorController)).ToDictionary(m => m.Name);

        Assert.True(RequiresAdmin(actions["Heartbeat"]), "Heartbeat precisa de admin");
        Assert.True(RequiresAdmin(actions["Report"]), "Report precisa de admin");
        // "Next" não devolve 403 (o executor de quem não é admin não pode registrar erro a cada consulta): responde 204.
        Assert.False(RequiresAdmin(actions["Next"]));
        Assert.DoesNotContain(actions.Values, a => a.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Any());
    }

    [Fact]
    public void The_admin_role_name_matches_the_one_used_by_the_rest_of_the_api()
    {
        Assert.Equal("admin", PromotionAccess.AdminRole);
    }
}
