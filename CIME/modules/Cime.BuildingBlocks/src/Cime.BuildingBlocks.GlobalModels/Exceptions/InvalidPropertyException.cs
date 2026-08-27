

namespace Cime.BuildingBlocks.GlobalModels
{
    public class InvalidPropertyException : Exception
{
    public InvalidPropertyException() : base("Item not found.")
    {
    }

    public InvalidPropertyException(string message) : base(message)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public InvalidPropertyException(string message, Exception innerException) : base(message, innerException)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }
}
}