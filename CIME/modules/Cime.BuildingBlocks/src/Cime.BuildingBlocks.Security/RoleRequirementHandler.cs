using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Cime.BuildingBlocks.Security
{
    public class RoleRequirementHandler : AuthorizationHandler<RoleRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            RoleRequirement requirement)
        {
            var roleClaim = context.User.Claims
                .FirstOrDefault(c => c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name");

            if (roleClaim != null && roleClaim.Value.Equals(requirement.RequiredRole, StringComparison.OrdinalIgnoreCase))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}