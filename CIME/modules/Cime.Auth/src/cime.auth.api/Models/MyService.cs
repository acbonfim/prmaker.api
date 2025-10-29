namespace cliqx.auth.api.Models
{
    public class MyService : BasicEntity
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<UserService>? UserServices { get; set; }
    }
}