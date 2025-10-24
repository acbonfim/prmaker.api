using solvace.prform.Data.Entities;

namespace solvace.prform.Data.Requests;

public class UserRequest
{
    public string Name { get; set; } = string.Empty;

    public User CreateUser(string name)
    {
        return new User(name);
    }
}