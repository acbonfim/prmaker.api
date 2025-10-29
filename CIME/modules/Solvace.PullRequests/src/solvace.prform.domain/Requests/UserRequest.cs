using solvace.prform.domain.Entities;

namespace solvace.prform.domain.Requests;

public class UserRequest
{
    public string Name { get; set; } = string.Empty;

    public User CreateUser(string name)
    {
        return new User(name);
    }
}