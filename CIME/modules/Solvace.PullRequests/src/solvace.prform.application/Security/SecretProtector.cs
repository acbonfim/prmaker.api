using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace solvace.prform.application.Security;

/// <summary>Criptografa/descriptografa segredos (tokens das integrações pessoais) para gravar no banco.</summary>
public interface ISecretProtector
{
    /// <summary>true quando há chave configurada (sem ela nada sensível pode ser salvo).</summary>
    bool IsConfigured { get; }

    /// <param name="context">Contexto amarrado ao valor (ex.: plugin + usuário): impede reaproveitar a cifra em outro registro.</param>
    string Protect(string plainText, string context);

    string Unprotect(string protectedText, string context);

    bool IsProtected(string? value);
}

/// <summary>
/// AES-256-GCM. Formato: <c>enc:v1:</c> + Base64(nonce[12] | tag[16] | cifra). O contexto entra como
/// dado associado (autenticado, não cifrado): trocar a cifra de registro ou adulterá-la falha na leitura.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const string Prefix = "enc:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[]? _key;

    public AesGcmSecretProtector(IOptions<UserIntegrationOptions> options)
    {
        var raw = options.Value.EncryptionKey;
        if (string.IsNullOrWhiteSpace(raw))
            return;

        byte[] key;
        try
        {
            key = Convert.FromBase64String(raw.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{UserIntegrationOptions.SectionName}:EncryptionKey deve estar em Base64.");
        }

        if (key.Length != 32)
            throw new InvalidOperationException($"{UserIntegrationOptions.SectionName}:EncryptionKey deve ter 32 bytes (AES-256); tem {key.Length}.");

        _key = key;
    }

    public bool IsConfigured => _key is not null;

    public string Protect(string plainText, string context)
    {
        var key = RequireKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plainText ?? string.Empty);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(context ?? string.Empty));

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedText, string context)
    {
        if (!IsProtected(protectedText))
            throw new CryptographicException("Valor não está protegido.");

        var key = RequireKey();
        var payload = Convert.FromBase64String(protectedText[Prefix.Length..]);
        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("Valor protegido inválido.");

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using (var aes = new AesGcm(key, TagSize))
            aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(context ?? string.Empty));

        return Encoding.UTF8.GetString(plain);
    }

    public bool IsProtected(string? value) => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    private byte[] RequireKey() =>
        _key ?? throw new InvalidOperationException(
            $"Integrações pessoais indisponíveis: configure {UserIntegrationOptions.SectionName}:EncryptionKey (32 bytes em Base64).");
}
