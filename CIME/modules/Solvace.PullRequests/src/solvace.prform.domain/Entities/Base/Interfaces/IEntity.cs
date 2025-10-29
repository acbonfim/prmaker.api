namespace solvace.prform.domain.Entities;

public interface IEntity<T> : IEntityBase
{
    public T Id { get; set; }
}