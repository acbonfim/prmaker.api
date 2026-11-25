using System.Text.Json;

namespace solvace.executionplans.domain.Entities;

/// <summary>0102: anexo da pergunta (imagem, PDF ou texto) — o executor grava numa pasta do pedido e o Claude lê com Read.</summary>
public sealed record BaseQuestionFile(string Name, string MediaType, string Data);

/// <summary>
/// 0102: pergunta à Base Solvace que o Claude Code da máquina de quem perguntou responde (pedido
/// <see cref="ExecutionRequestKind.BaseAsk"/>), em vez do provedor de IA do servidor. O andamento é o do pedido; a resposta
/// (JSON do <c>ArchitectureAskResponse</c>) chega pela ferramenta MCP <c>prmake_base_answer</c>. Os anexos só servem ao
/// executor e saem quando o pedido termina.
/// </summary>
public class BaseQuestion
{
    public const int MaxQuestionLength = 2_000;
    public const int MaxErrorLength = 1_000;
    public const int MaxHintsLength = 6_000;
    public const string KeyPrefix = "BQ-";

    public Guid Id { get; private set; }
    /// <summary>"Card" do pedido na fila (<c>BQ-</c> + 12 hex): um por pergunta e o grupo do tempo real (<c>execplan:{Key}</c>).</summary>
    public string Key { get; private set; } = string.Empty;
    public Guid OwnerUserId { get; private set; }
    public string OwnerName { get; private set; } = string.Empty;
    public string Question { get; private set; } = string.Empty;
    /// <summary>"Analisar a fundo": mais esforço e pode confirmar no código da máquina.</summary>
    public bool Deep { get; private set; }
    /// <summary>JSON de <see cref="BaseQuestionFile"/>; null depois que o pedido termina.</summary>
    public string? Files { get; private set; }
    public Guid RequestId { get; private set; }
    /// <summary>
    /// Busca na Base já feita pelo PRMake com o texto da pergunta (o mesmo texto do <c>prmake_base_search</c>) — vai no prompt
    /// e o Claude pula a primeira busca. null = não deu tempo (ele busca).
    /// </summary>
    public string? Hints { get; private set; }
    /// <summary>JSON do <c>ArchitectureAskResponse</c> (camelCase), como a tela recebe.</summary>
    public string? Answer { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AnsweredAt { get; private set; }
    public uint Version { get; private set; }

    protected BaseQuestion() { }

    public BaseQuestion(Guid ownerUserId, string ownerName, string question, bool deep, IReadOnlyList<BaseQuestionFile>? files, DateTimeOffset now,
        string? hints = null)
    {
        var q = (question ?? string.Empty).Trim();
        if (q.Length == 0)
            throw new DomainException("Escreva a pergunta.");
        Id = Guid.NewGuid();
        Key = KeyOf(Id);
        OwnerUserId = ownerUserId;
        OwnerName = string.IsNullOrWhiteSpace(ownerName) ? "Usuário" : ownerName.Trim()[..Math.Min(ownerName.Trim().Length, 200)];
        Question = q.Length <= MaxQuestionLength ? q : q[..MaxQuestionLength];
        Deep = deep;
        Files = files is { Count: > 0 } ? JsonSerializer.Serialize(files) : null;
        var h = hints?.Trim();
        Hints = string.IsNullOrEmpty(h) ? null : h.Length <= MaxHintsLength ? h : h[..MaxHintsLength];
        CreatedAt = now;
    }

    public static string KeyOf(Guid id) => KeyPrefix + id.ToString("N")[..12].ToUpperInvariant();

    public static bool IsKey(string? card) =>
        card is { Length: 15 } k && k.StartsWith(KeyPrefix, StringComparison.Ordinal) && k[3..].All(char.IsAsciiHexDigit);

    public IReadOnlyList<BaseQuestionFile> GetFiles()
    {
        if (string.IsNullOrEmpty(Files)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<BaseQuestionFile>>(Files) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void AttachRequest(Guid requestId) => RequestId = requestId;

    public bool IsAnswered => Answer is not null;

    public void SetAnswer(string answerJson, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(answerJson))
            throw new DomainException("Resposta vazia.");
        Answer = answerJson;
        AnsweredAt = now;
        Files = null;
    }

    /// <summary>O pedido terminou (com ou sem resposta): os anexos não servem mais.</summary>
    public void DropFiles() => Files = null;
}
