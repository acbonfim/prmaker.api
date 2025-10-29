

namespace Cliqx.BuildingBlocks.GlobalModels
{
    public class PDFConfirmationException : Exception
    {
        public PDFConfirmationException() : base("Error to send email")
        {
            // Você pode adicionar lógica personalizada aqui, se necessário.
        }

        public PDFConfirmationException(string message) : base(message)
        {
            // Você pode adicionar lógica personalizada aqui, se necessário.
        }

        public PDFConfirmationException(string message, Exception innerException) : base(message, innerException)
        {
            // Você pode adicionar lógica personalizada aqui, se necessário.
        }
    }
}