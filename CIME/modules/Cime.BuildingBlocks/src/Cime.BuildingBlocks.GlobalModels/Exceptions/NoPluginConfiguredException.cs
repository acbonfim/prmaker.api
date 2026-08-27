

namespace Cime.BuildingBlocks.GlobalModels
{
    public class NoPluginConfiguredException : Exception
    {
        public NoPluginConfiguredException() : base("Plugin not found.")
        {
            // Você pode adicionar lógica personalizada aqui, se necessário.
        }

        public NoPluginConfiguredException(string message) : base(message)
        {
            // Você pode adicionar lógica personalizada aqui, se necessário.
        }

        public NoPluginConfiguredException(string message, Exception innerException) : base(message, innerException)
        {
            // Você pode adicionar lógica personalizada aqui, se necessário.
        }
    }
}