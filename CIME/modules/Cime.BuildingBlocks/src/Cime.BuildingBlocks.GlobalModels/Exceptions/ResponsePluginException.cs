

namespace Cliqx.BuildingBlocks.GlobalModels
{
    public class ResponsePluginException : Exception
{
    public ResponsePluginException() : base("Error to send request to plugin.")
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public ResponsePluginException(string message) : base(message)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public ResponsePluginException(string message, Exception innerException) : base(message, innerException)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }
}
}