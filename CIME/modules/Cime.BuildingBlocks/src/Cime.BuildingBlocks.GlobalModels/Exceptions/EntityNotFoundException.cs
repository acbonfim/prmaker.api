

namespace Cliqx.BuildingBlocks.GlobalModels
{
    public class EntityNotFoundException : Exception
{
    public EntityNotFoundException() : base("Item not found.")
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public EntityNotFoundException(string message) : base(message)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public EntityNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }
}
}