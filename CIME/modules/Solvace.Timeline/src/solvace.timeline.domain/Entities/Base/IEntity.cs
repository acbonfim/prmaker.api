namespace solvace.timeline.domain.Entities.Base;

public interface IEntity<T>
{
    T Id { get; set; }
}
