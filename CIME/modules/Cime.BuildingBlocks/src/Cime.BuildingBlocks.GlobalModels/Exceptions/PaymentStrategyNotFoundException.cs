

namespace Cime.BuildingBlocks.GlobalModels
{
    public class PaymentStrategyNotFoundException : Exception
{
    public PaymentStrategyNotFoundException() : base("Item not found.")
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public PaymentStrategyNotFoundException(string message) : base(message)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }

    public PaymentStrategyNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
        // Você pode adicionar lógica personalizada aqui, se necessário.
    }
}
}