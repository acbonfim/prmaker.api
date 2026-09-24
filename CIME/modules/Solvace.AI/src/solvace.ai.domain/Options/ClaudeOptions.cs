namespace solvace.ai.domain.Options;

/// <summary>
/// Configuração do provedor Claude (plugin "Claude Plugin"). Todos os valores vêm do plugin — o
/// Model em especial é trocado lá, sem deploy; os padrões abaixo só valem para campo vazio.
/// </summary>
public class ClaudeOptions
{
    public const string DefaultModel = "claude-haiku-4-5";
    public const string DefaultBaseUrl = "https://api.anthropic.com";
    public const int DefaultMaxOutputTokens = 8000;
    public const int DefaultTimeoutSeconds = 120;
    public const int DefaultMaxRetries = 3;

    /// <summary>Chave da API da Anthropic (uso pessoal: a de cada usuário, via Minhas integrações).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Raiz da API. Aceita também o valor antigo com "/v1/messages" (é normalizado).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>ID do modelo (ex.: claude-haiku-4-5, claude-sonnet-5, claude-opus-5), sem sufixo de data.</summary>
    public string? Model { get; set; }

    /// <summary>Instruções fixas enviadas como system prompt (opcional).</summary>
    public string? SystemInstruction { get; set; }

    /// <summary>Teto de tokens da resposta. Se for atingido a resposta vem cortada e o serviço avisa.</summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>Timeout de cada tentativa, em segundos.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Retentativas automáticas do SDK (429, 5xx, 529 sobrecarregado, conexão).</summary>
    public int? MaxRetries { get; set; }

    /// <summary>low | medium | high | max — só em modelos que suportam (não usar com Haiku 4.5).</summary>
    public string? Effort { get; set; }
}
