namespace solvace.prform.application.Security;

/// <summary>Configuração das integrações pessoais (seção "UserIntegrations").</summary>
public class UserIntegrationOptions
{
    public const string SectionName = "UserIntegrations";

    /// <summary>
    /// Chave AES-256 em Base64 (32 bytes) para criptografar os valores sensíveis das integrações
    /// pessoais. Deve ser a mesma em todas as instâncias/deploys (Secret Manager em produção).
    /// Gerar: <c>openssl rand -base64 32</c>.
    /// </summary>
    public string? EncryptionKey { get; set; }
}
