

namespace Cliqx.BuildingBlocks.GlobalModels
{
    public class DataBaseException : Exception
{
    public DataBaseException() : base("Database exception.")
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public DataBaseException(string message) : base(message)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public DataBaseException(string message, Exception innerException) : base(message, innerException)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }
}
}