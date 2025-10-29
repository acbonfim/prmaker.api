

namespace Cime.BuildingBlocks.GlobalModels
{
    public class PluginNotFoundException : Exception
    {
        public PluginNotFoundException() : base("Plugin not found.")
        {
            // Você pode adicionar lógica personalizada aqui, se necessário.
        }

        public PluginNotFoundException(string message) : base(message)
        {
            // Você pode adicionar lógica personalizada aqui, se necessário.
        }

        public PluginNotFoundException(string message, Exception innerException) : base(message, innerException)
        {
            // Você pode adicionar lógica personalizada aqui, se necessário.
        }
    }
}