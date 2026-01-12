using Microsoft.AspNetCore.Authorization;

namespace Cime.BuildingBlocks.Security
{
    public class RoleRequirement : IAuthorizationRequirement
    {
        public string RequiredRole { get; }

        public RoleRequirement(string requiredRole)
        {
            RequiredRole = requiredRole;
        }
    }
}