using Microsoft.Extensions.Configuration;

namespace cliqx.auth.api.Security
{
    /// <summary>
    /// Chaves de assinatura dos tokens (acesso/api-key e refresh). Vêm da configuração
    /// (Auth:Secret / Auth:SecretRefresh — Secret Manager em produção, appsettings.Development no dev),
    /// nunca do código (0016). O Auth:Secret é o mesmo que a API principal usa para validar as api-keys.
    /// </summary>
    public static class Settings
    {
        // HMAC-SHA512 exige chave de pelo menos 512 bits.
        private const int MinLength = 64;

        public static string Secret { get; private set; } = string.Empty;
        public static string SecretRefresh { get; private set; } = string.Empty;

        public static void Configure(IConfiguration configuration)
        {
            Secret = configuration["Auth:Secret"] ?? string.Empty;
            SecretRefresh = configuration["Auth:SecretRefresh"] ?? string.Empty;
            if (Secret.Length < MinLength || SecretRefresh.Length < MinLength)
                throw new InvalidOperationException(
                    $"Configure Auth:Secret e Auth:SecretRefresh (mínimo {MinLength} caracteres).");
        }
    }
}
