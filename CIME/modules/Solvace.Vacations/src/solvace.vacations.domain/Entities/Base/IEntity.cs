namespace solvace.vacations.domain.Entities.Base;

public interface IEntity<T>
{
    T Id { get; set; }
}
