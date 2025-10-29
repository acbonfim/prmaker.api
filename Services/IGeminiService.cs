namespace solvace.prform.Services
{
    public interface IGeminiService
    {
        Task<string?> GenerateContentAsync(string prompt);
    }
}
