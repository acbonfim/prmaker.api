namespace solvace.vacations.domain.Entities.Base;

public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
