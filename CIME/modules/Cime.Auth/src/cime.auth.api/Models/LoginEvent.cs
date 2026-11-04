using System;

namespace cliqx.auth.api.Models;

/// <summary>
/// Tentativa de login (0079): toda chamada ao <c>LoginUser</c>, com sucesso ou não. A tela "Análise de uso" da API
/// principal lê esta tabela (schema auth, somente leitura por lá).
/// </summary>
public class LoginEvent
{
    public const int MaxUserNameLength = 256;
    public const int MaxReasonLength = 40;
    public const int MaxIpLength = 64;
    public const int MaxUserAgentLength = 512;

    public const string ReasonOk = "ok";
    public const string ReasonNotFound = "not_found";
    public const string ReasonInactive = "inactive";
    public const string ReasonUnconfirmed = "unconfirmed";
    public const string ReasonWrongPassword = "wrong_password";
    public const string ReasonError = "error";

    public long Id { get; set; }
    /// <summary>Horário "de parede" (DateTime.Now), como as demais datas da auth.</summary>
    public DateTime At { get; set; }
    public int? UserId { get; set; }
    public Guid? UserExternalId { get; set; }
    /// <summary>Usuário encontrado ou, quando não existe, o nome digitado.</summary>
    public string UserName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Reason { get; set; } = ReasonOk;
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }

    public static string? Cut(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length <= max ? v : v[..max];
    }
}
