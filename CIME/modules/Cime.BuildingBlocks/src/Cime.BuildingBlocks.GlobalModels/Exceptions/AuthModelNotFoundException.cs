

namespace Cime.BuildingBlocks.GlobalModels
{
    public class AuthModelNotFoundException : Exception
{
    public AuthModelNotFoundException() : base("Item not found.")
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public AuthModelNotFoundException(string message) : base(message)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public AuthModelNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }
}
}