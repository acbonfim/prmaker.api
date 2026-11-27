using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using solvace.prform.Controllers;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0074: a promoção de PRs é de admin e support — nenhum endpoint pode escapar dessa exigência.</summary>
public class PromotionAccessTests
{
    private static readonly string[] Allowed = ["admin", "support"];

    /// <summary>O <c>[Authorize]</c> pede exatamente admin ou support (nem menos, nem outros papéis).</summary>
    private static bool RequiresPromotionRole(ICustomAttributeProvider provider) =>
        provider.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Any(a => (a.Roles ?? string.Empty).Split(',').Select(r => r.Trim().ToLowerInvariant()).Order()
                .SequenceEqual(Allowed.Order()));

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().Any(a => a is HttpMethodAttribute));

    private static ClaimsPrincipal UserWith(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test"));

    [Fact]
    public void Every_user_facing_promotion_endpoint_requires_admin_or_support()
    {
        Assert.True(RequiresPromotionRole(typeof(PromotionController)), "PromotionController precisa de [Authorize(Roles = \"admin,support\")]");

        var actions = Actions(typeof(PromotionController)).ToList();
        Assert.NotEmpty(actions);
        Assert.DoesNotContain(actions, a => a.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Any());
    }

    [Fact]
    public void Executor_endpoints_that_change_state_require_the_role_and_next_hides_the_queue_from_the_others()
    {
        var actions = Actions(typeof(PromotionExecutorController)).ToDictionary(m => m.Name);

        Assert.True(RequiresPromotionRole(actions["Heartbeat"]), "Heartbeat precisa de admin/support");
        Assert.True(RequiresPromotionRole(actions["Report"]), "Report precisa de admin/support");
        // "Next" não devolve 403 (o executor de quem não promove não pode registrar erro a cada consulta): responde 204.
        Assert.False(RequiresPromotionRole(actions["Next"]));
        Assert.DoesNotContain(actions.Values, a => a.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Any());
    }

    [Theory]
    [InlineData(true, "admin")]
    [InlineData(true, "support")]
    [InlineData(true, "user", "support")]
    [InlineData(false, "user")]
    [InlineData(false, "gestor")]
    [InlineData(false, "management")]
    [InlineData(false)]
    public void Only_admin_and_support_promote(bool expected, params string[] roles) =>
        Assert.Equal(expected, PromotionAccess.Allows(UserWith(roles)));

    [Fact]
    public void The_role_names_match_the_ones_used_by_the_rest_of_the_api()
    {
        Assert.Equal("admin", PromotionAccess.AdminRole);
        Assert.Equal("support", PromotionAccess.SupportRole);
    }
}
