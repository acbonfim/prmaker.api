using System.Text.RegularExpressions;

namespace solvace.prform.application.Security;

/// <summary>
/// Campos de plugin tratados como segredo (criptografados e nunca devolvidos ao front).
/// Por palavra do nome (PascalCase/camelCase/snake/kebab): Token, Secret, Password/Passwd, Pwd,
/// ApiKey, ou terminando em "Key" (ex.: PrivateKey). Ex.: "PersonalAccessToken" e "Token" são
/// sensíveis; "RootCauseFieldPath" e "Owner" não.
/// </summary>
public static partial class SensitiveFieldPolicy
{
    private static readonly HashSet<string> SensitiveWords =
        new(StringComparer.OrdinalIgnoreCase) { "token", "secret", "password", "passwd", "pwd", "apikey", "pat" };

    public static bool IsSensitive(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var words = SplitWords(key);
        if (words.Count == 0)
            return false;

        // "Api" + "Key" também conta como apikey; "...Key" no fim (PrivateKey, SecretKey).
        if (words.Any(w => SensitiveWords.Contains(w)))
            return true;
        for (var i = 0; i < words.Count - 1; i++)
            if (string.Equals(words[i] + words[i + 1], "apikey", StringComparison.OrdinalIgnoreCase))
                return true;
        return string.Equals(words[^1], "key", StringComparison.OrdinalIgnoreCase) && words.Count > 1;
    }

    private static List<string> SplitWords(string key) =>
        WordRegex().Matches(key).Select(m => m.Value).ToList();

    // Maiúsculas seguidas de minúsculas, siglas e números: "PersonalAccessToken" -> Personal|Access|Token
    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex WordRegex();
}
