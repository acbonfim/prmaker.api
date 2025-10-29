using Microsoft.Extensions.Options;
using solvace.prform.Models;

namespace solvace.prform.Services
{
    public class GeminiService : IGeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public GeminiService(HttpClient httpClient, IOptions<GeminiSettings> settings)
        {
            _httpClient = httpClient;
            _apiKey = settings.Value.ApiKey;
            _httpClient.BaseAddress = new Uri(settings.Value.BaseUrl);
            _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", _apiKey);
        }

        public async Task<string?> GenerateContentAsync(string prompt)
        {
            var request = new GeminiRequest
            {
                Contents = new List<Content>
            {
                new Content
                {
                    Parts = new List<Part>
                    {
                        new Part { Text = prompt }
                    }
                }
            }
            };

            var response = await _httpClient.PostAsJsonAsync("", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<GeminiResponse>();
            return result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        }
    }

    public class GeminiSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
    }
}
