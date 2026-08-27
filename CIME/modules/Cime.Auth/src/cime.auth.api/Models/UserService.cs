using cliqx.auth.api.Models.Identity;

namespace cliqx.auth.api.Models
{
    public class UserService
    {
        public int UserId { get; set; }
        public User User { get; set; }

        public long ServiceId { get; set; }
        public MyService Service { get; set; }
    }
}