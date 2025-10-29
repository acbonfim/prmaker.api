

namespace Cliqx.BuildingBlocks.GlobalModels
{
    public class SendEmailException : Exception
    {
        public SendEmailException() : base("Error to send email")
        {
            // Você pode adicionar lógica personalizada aqui, se necessário.
        }

        public SendEmailException(string message) : base(message)
        {
            // Você pode adicionar lógica personalizada aqui, se necessário.
        }

        public SendEmailException(string message, Exception innerException) : base(message, innerException)
        {
            // Você pode adicionar lógica personalizada aqui, se necessário.
        }
    }
}