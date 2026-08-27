

namespace Cime.BuildingBlocks.GlobalModels
{
    public class IncompatibleException : Exception
{
    public IncompatibleException() : base("Item not found.")
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public IncompatibleException(string message) : base(message)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public IncompatibleException(string message, Exception innerException) : base(message, innerException)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }
}
}