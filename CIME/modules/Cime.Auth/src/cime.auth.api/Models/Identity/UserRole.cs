using Microsoft.AspNetCore.Identity;

namespace cliqx.auth.api.Models.Identity
{
    public class UserRole : IdentityUserRole<int>
    {
        public User User { get; set; }
        public Role Role { get; set; }

    }
}