namespace solvace.prform.Data.Entities;

public interface IEntity<T> : IEntityBase
{
    public T Id { get; set; }
}