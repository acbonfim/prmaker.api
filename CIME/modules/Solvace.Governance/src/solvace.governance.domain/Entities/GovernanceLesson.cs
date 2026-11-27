using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace solvace.governance.domain.Entities;

/// <summary>
/// 0106: lição aprendida com um erro de um agente (especialista, tratamento, executor) — "quando X, faça/nunca Y", o porquê
/// e de onde veio. Quem erra e é corrigido grava; lição parecida com uma ativa reforça a existente (conta a reincidência)
/// em vez de duplicar. As ativas entram sozinhas no que todo agente lê no início (regras do card e contexto do caso), as
/// mais reincidentes primeiro. Nunca sobrepõe as regras fixas nem a governança.
/// </summary>
public partial class GovernanceLesson
{
    public const int MaxTrigger = 300;
    public const int MaxText = 600;
    public const int MaxWhy = 1500;
    public const int MaxSources = 10;
    /// <summary>Semelhança (palavras em comum) a partir da qual a lição nova reforça a existente.</summary>
    public const double SameLessonSimilarity = 0.5;

    public long Id { get; private set; }
    /// <summary>Skills a que se aplica (vazio = todas).</summary>
    public List<string> Scope { get; private set; } = [];
    public string Trigger { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public string Why { get; private set; } = string.Empty;
    public List<LessonSource> Sources { get; private set; } = [];
    public string Status { get; private set; } = LessonStatus.Active;
    public string Origin { get; private set; } = LessonOrigin.Agent;
    public int Hits { get; private set; } = 1;
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
    public string? RetiredReason { get; private set; }

    public string Code => $"L{Id}";

    protected GovernanceLesson() { }

    public GovernanceLesson(LessonData data, string origin, LessonSource? source, string actor, DateTimeOffset now)
    {
        if (!LessonOrigin.IsValid(origin)) throw new DomainException($"Origem inválida: {origin}.");
        Apply(data);
        Origin = origin;
        CreatedAt = now;
        LastSeenAt = now;
        CreatedBy = Clip(actor, 200) ?? "PRMake";
        if (source is not null) Sources.Add(source.Normalized(now, CreatedBy));
    }

    /// <summary>Aconteceu de novo: conta, guarda a origem nova (as 10 últimas) e volta a valer se estava aposentada por desuso.</summary>
    public void Reinforce(LessonSource? source, string? why, string actor, DateTimeOffset now)
    {
        Hits++;
        LastSeenAt = now;
        if (source is not null)
        {
            Sources.Add(source.Normalized(now, actor));
            if (Sources.Count > MaxSources) Sources = Sources.Skip(Sources.Count - MaxSources).ToList();
        }
        if (!string.IsNullOrWhiteSpace(why) && Why.Length < MaxWhy / 2 && !Why.Contains(why.Trim(), StringComparison.OrdinalIgnoreCase))
            Why = Clip($"{Why} {why.Trim()}".Trim(), MaxWhy)!;
    }

    public void Edit(LessonData data, string actor, DateTimeOffset now)
    {
        Apply(data);
        UpdatedAt = now;
        UpdatedBy = Clip(actor, 200);
    }

    public void Retire(string? reason, string actor, DateTimeOffset now)
    {
        Status = LessonStatus.Retired;
        RetiredReason = Clip(reason, 500) ?? "Aposentada.";
        UpdatedAt = now;
        UpdatedBy = Clip(actor, 200);
    }

    public void Reactivate(string actor, DateTimeOffset now)
    {
        Status = LessonStatus.Active;
        RetiredReason = null;
        LastSeenAt = now;
        UpdatedAt = now;
        UpdatedBy = Clip(actor, 200);
    }

    public bool AppliesTo(string? skill) =>
        Scope.Count == 0 || string.IsNullOrWhiteSpace(skill) || Scope.Contains(skill.Trim().ToLowerInvariant());

    /// <summary>Vale no que os agentes leem: ativa e não esquecida (uma vez só e sem repetir há 120 dias sai da lista).</summary>
    public bool IsLive(DateTimeOffset now) =>
        Status == LessonStatus.Active && (Hits > 1 || LastSeenAt >= now.AddDays(-120));

    private void Apply(LessonData d)
    {
        var trigger = Clip(d.Trigger, MaxTrigger);
        var text = Clip(d.Text, MaxText);
        if (trigger is null) throw new DomainException("Informe quando a lição vale (trigger: a situação, em uma frase).");
        if (text is null) throw new DomainException("Informe a lição (o que fazer ou nunca fazer, em uma frase).");
        if (LoosensFixedRule().IsMatch(Fold(text)))
            throw new DomainException("Lição não pode liberar o que as regras fixas proíbem (mesclar/aprovar PR, senha, gravar fora do PRMake).");
        var scope = (d.Scope ?? []).Select(s => (s ?? string.Empty).Trim().ToLowerInvariant()).Where(s => s.Length > 0 && s != "all")
            .Distinct().ToList();
        foreach (var s in scope)
            if (!ScopePattern().IsMatch(s)) throw new DomainException($"Escopo inválido: {s} (use o nome da skill, ex.: especialista-do-caso).");
        Scope = scope;
        Trigger = trigger;
        Text = text;
        Why = Clip(d.Why, MaxWhy) ?? string.Empty;
    }

    /// <summary>Palavras em comum (sem acento, sem palavras curtas/comuns) entre situação+lição — 0 a 1.</summary>
    public static double Similarity(string a, string b)
    {
        var x = Words(a);
        var y = Words(b);
        if (x.Count == 0 || y.Count == 0) return 0;
        var common = x.Intersect(y).Count();
        return (double)common / x.Union(y).Count();
    }

    public string SimilarityText => $"{Trigger} {Text}";

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "que", "para", "com", "sem", "uma", "por", "nao", "nunca", "sempre", "quando", "mais", "como", "isso", "esta", "este", "essa",
        "esse", "dos", "das", "nos", "nas", "pelo", "pela", "ser", "ter", "foi", "sao", "seu", "sua", "the", "and", "faca", "use",
        "pessoa", "card", "voce", "antes", "depois", "aqui", "ele", "ela", "ja", "tem", "fica", "vai"
    };

    private static HashSet<string> Words(string text) =>
        WordPattern().Matches(Fold(text)).Select(m => m.Value).Where(w => w.Length >= 3 && !Stop.Contains(w)).Select(Stem).ToHashSet();

    /// <summary>Raiz simples (plural/gênero): "comentarios" e "comentario" contam como a mesma palavra.</summary>
    private static string Stem(string w) =>
        w.Length > 5 && w.EndsWith("s", StringComparison.Ordinal) ? Stem(w[..^1]) :
        w.Length > 5 && (w.EndsWith('a') || w.EndsWith('o') || w.EndsWith('e')) ? w[..^1] : w;

    public static string Fold(string text)
    {
        var normalized = (text ?? string.Empty).ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string? Clip(string? v, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var t = v.Trim();
        return t.Length <= max ? t : t[..max];
    }

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex WordPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,39}$")]
    private static partial Regex ScopePattern();

    /// <summary>"pode mesclar", "liberado aprovar o PR", "pode pedir a senha", "pode gravar direto no DevOps"...</summary>
    [GeneratedRegex(@"\b(pode|podem|liberad[oa]s?|permitid[oa]s?|tudo bem)\b[^.;]{0,40}\b(mescl\w*|merge\w*|aprov\w*|senha\w*|gravar direto)")]
    private static partial Regex LoosensFixedRule();
}

public sealed record LessonData
{
    public List<string>? Scope { get; init; }
    public string? Trigger { get; init; }
    public string? Text { get; init; }
    public string? Why { get; init; }
}

/// <summary>De onde a lição veio (card, referência — mensagem, item, PR — e quem gravou).</summary>
public sealed record LessonSource
{
    public string? Card { get; init; }
    public string? Ref { get; init; }
    public DateTimeOffset? At { get; init; }
    public string? By { get; init; }

    public LessonSource Normalized(DateTimeOffset now, string by) => this with
    {
        Card = Cut(Card, 20), Ref = Cut(Ref, 200), At = At ?? now, By = Cut(By, 200) ?? Cut(by, 200)
    };

    private static string? Cut(string? v, int max) =>
        string.IsNullOrWhiteSpace(v) ? null : v.Trim().Length <= max ? v.Trim() : v.Trim()[..max];
}

public static class LessonStatus
{
    public const string Active = "active";
    public const string Retired = "retired";
    public static bool IsValid(string? v) => v is Active or Retired;
}

public static class LessonOrigin
{
    /// <summary>O agente gravou ao ser corrigido.</summary>
    public const string Agent = "agent";
    /// <summary>Veio de um sinal de erro que o PRMake registrou sozinho.</summary>
    public const string Signal = "signal";
    /// <summary>Semeada por migração (ex.: as do card 79047).</summary>
    public const string Seed = "seed";
    /// <summary>Escrita na tela.</summary>
    public const string Admin = "admin";
    public static bool IsValid(string? v) => v is Agent or Signal or Seed or Admin;
}
