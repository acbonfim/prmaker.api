

namespace Cliqx.BuildingBlocks.GlobalModels
{
    public class UrlModelNotFoundException : Exception
{
    public UrlModelNotFoundException() : base("Item not found.")
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public UrlModelNotFoundException(string message) : base(message)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public UrlModelNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }
}
}