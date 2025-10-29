

namespace Cime.BuildingBlocks.GlobalModels
{
    public class ItemNotFoundException : Exception
{
    public ItemNotFoundException() : base("Item not found.")
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public ItemNotFoundException(string message) : base(message)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public ItemNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }
}
}