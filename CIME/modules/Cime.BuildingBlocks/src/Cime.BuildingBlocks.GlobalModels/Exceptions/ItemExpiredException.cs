

namespace Cliqx.BuildingBlocks.GlobalModels
{
    public class ItemExpiredException : Exception
{
    public ItemExpiredException() : base("Item has expired.")
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public ItemExpiredException(string message) : base(message)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public ItemExpiredException(string message, Exception innerException) : base(message, innerException)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }
}
}