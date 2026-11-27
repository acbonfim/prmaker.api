using System.Text.RegularExpressions;

namespace solvace.executionplans.domain.Entities;

public static class CaseChoiceMode
{
    /// <summary>Cada botão responde na hora ("Criar item e executar agora", "Cancelar").</summary>
    public const string Buttons = "buttons";
    /// <summary>Escolha única (rádio) + Enviar.</summary>
    public const string Single = "single";
    /// <summary>Checklist (várias) + Enviar.</summary>
    public const string Multi = "multi";
    /// <summary>Lista suspensa (picklist) + Enviar — para muitas opções.</summary>
    public const string Select = "select";

    public static string Normalize(string? mode) => (mode ?? Buttons).Trim().ToLowerInvariant() switch
    {
        Buttons or "button" or "botoes" or "botões" => Buttons,
        Single or "radio" or "unica" or "única" => Single,
        Multi or "multiple" or "checklist" or "checkbox" => Multi,
        Select or "picklist" or "dropdown" or "lista" => Select,
        var m => throw new DomainException($"Tipo de escolha inválido: '{m}' (buttons|single|multi|select).")
    };
}

public static class CaseChoiceStyle
{
    public const string Primary = "primary";
    public const string Danger = "danger";
    public const string Neutral = "neutral";

    public static string Normalize(string? style) => (style ?? Neutral).Trim().ToLowerInvariant() switch
    {
        Primary => Primary,
        Danger or "warn" => Danger,
        _ => Neutral
    };
}

public class CaseChoiceOption
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string Style { get; set; } = CaseChoiceStyle.Neutral;
    public bool Recommended { get; set; }
}

public class CaseChoiceAnswer
{
    public Guid? UserId { get; set; }
    public string By { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
    public List<string> Selected { get; set; } = [];
    public string? Other { get; set; }
    /// <summary>Mensagem da pessoa que levou a resposta ao especialista.</summary>
    public int? MessageNumber { get; set; }
}

/// <summary>
/// 0107: escolha clicável numa mensagem do especialista — botões com o texto do que vai acontecer, escolha única,
/// checklist ou lista suspensa. A pessoa responde clicando (uma resposta por escolha); a resposta vira mensagem dela e
/// chama o especialista. Escolha nova do especialista fecha as anteriores ainda abertas.
/// </summary>
public partial class CaseChoice
{
    public const int MaxQuestionLength = 300;
    public const int MaxLabelLength = 120;
    public const int MaxDetailLength = 300;
    public const int MaxIdLength = 40;
    public const int MaxButtons = 6;
    public const int MaxOptions = 12;
    public const int MaxSelectOptions = 50;
    public const int MaxOtherLength = 2_000;

    public string Mode { get; set; } = CaseChoiceMode.Buttons;
    public string? Question { get; set; }
    public List<CaseChoiceOption> Options { get; set; } = [];
    public bool AllowOther { get; set; }
    public string? OtherLabel { get; set; }
    public CaseChoiceAnswer? Answer { get; set; }
    /// <summary>Fechada sem resposta (o especialista mandou uma escolha mais nova).</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    public bool IsOpen => Answer is null && ClosedAt is null;

    /// <summary>Valida e normaliza o que o especialista mandou (ids gerados quando faltam, textos cortados).</summary>
    public static CaseChoice Create(string? mode, string? question, IEnumerable<CaseChoiceOption>? options, bool allowOther, string? otherLabel)
    {
        var m = CaseChoiceMode.Normalize(mode);
        var list = (options ?? []).Where(o => o is not null && !string.IsNullOrWhiteSpace(o.Label)).ToList();
        var max = m switch { CaseChoiceMode.Buttons => MaxButtons, CaseChoiceMode.Select => MaxSelectOptions, _ => MaxOptions };
        if (list.Count > max)
            throw new DomainException($"Escolha '{m}' aceita no máximo {max} opções.");
        var min = m == CaseChoiceMode.Buttons ? 1 : 2;
        if (list.Count < min && !(allowOther && list.Count >= 1))
            throw new DomainException(m == CaseChoiceMode.Buttons ? "Mande ao menos um botão." : "Mande ao menos duas opções.");

        var normalized = new List<CaseChoiceOption>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < list.Count; i++)
        {
            var o = list[i];
            var id = Slug(o.Id);
            if (id.Length == 0 || !ids.Add(id))
            {
                id = (i + 1).ToString();
                while (!ids.Add(id)) id += "x";
            }
            var label = o.Label.Trim();
            var detail = o.Detail?.Trim();
            normalized.Add(new CaseChoiceOption
            {
                Id = id,
                Label = Cut(label, MaxLabelLength),
                Detail = string.IsNullOrEmpty(detail) ? null : Cut(detail, MaxDetailLength),
                Style = CaseChoiceStyle.Normalize(o.Style),
                Recommended = o.Recommended
            });
        }
        var q = question?.Trim();
        var other = otherLabel?.Trim();
        return new CaseChoice
        {
            Mode = m,
            Question = string.IsNullOrEmpty(q) ? null : Cut(q, MaxQuestionLength),
            Options = normalized,
            AllowOther = allowOther,
            OtherLabel = allowOther ? (string.IsNullOrEmpty(other) ? "Outra resposta" : Cut(other, MaxLabelLength)) : null
        };
    }

    /// <summary>Grava a resposta (uma só). Devolve as opções escolhidas, na ordem da escolha.</summary>
    public List<CaseChoiceOption> Respond(IEnumerable<string>? selected, string? other, Guid? userId, string by, DateTimeOffset now)
    {
        if (Answer is { } done)
            throw new DomainException($"Esta escolha já foi respondida por {done.By}.");
        if (ClosedAt is not null)
            throw new DomainException("Esta escolha foi substituída por uma mais nova do especialista.");
        var wanted = (selected ?? []).Select(s => (s ?? string.Empty).Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = wanted.Where(w => Options.All(o => !string.Equals(o.Id, w, StringComparison.OrdinalIgnoreCase))).ToList();
        if (unknown.Count > 0)
            throw new DomainException($"Opção inexistente: {string.Join(", ", unknown)}.");
        var picked = Options.Where(o => wanted.Contains(o.Id)).ToList();
        var text = other?.Trim();
        if (!string.IsNullOrEmpty(text) && !AllowOther)
            throw new DomainException("Esta escolha não aceita outra resposta — escreva na conversa.");
        if (string.IsNullOrEmpty(text)) text = null;
        else text = Cut(text, MaxOtherLength);
        if (picked.Count == 0 && text is null)
            throw new DomainException("Escolha uma opção.");
        if (Mode != CaseChoiceMode.Multi && picked.Count + (text is null ? 0 : 1) > 1)
            throw new DomainException("Escolha só uma opção.");

        var who = (by ?? string.Empty).Trim();
        Answer = new CaseChoiceAnswer
        {
            UserId = userId,
            By = who.Length == 0 ? "Usuário" : Cut(who, 200),
            At = now,
            Selected = picked.Select(p => p.Id).ToList(),
            Other = text
        };
        return picked;
    }

    public void Close(DateTimeOffset now)
    {
        if (IsOpen) ClosedAt = now;
    }

    /// <summary>Texto da mensagem da pessoa que leva a resposta (o que a tela e o especialista leem).</summary>
    public static string AnswerText(IReadOnlyList<CaseChoiceOption> picked, string? other)
    {
        var lines = picked.Select(p => $"**{p.Label}**").ToList();
        if (other is not null)
            lines.Add(picked.Count == 0 ? other : $"Outra: {other}");
        return lines.Count == 1 ? lines[0] : string.Join("\n", lines.Select(l => "- " + l));
    }

    /// <summary>Uma linha para o contexto do especialista: as opções e a situação.</summary>
    public string ContextLine()
    {
        var opts = string.Join(" | ", Options.Select(o => $"{o.Id}={o.Label}"));
        var state = Answer is { } a
            ? $"respondida por {a.By}: {string.Join(", ", Options.Where(o => a.Selected.Contains(o.Id)).Select(o => o.Label).Append(a.Other).Where(s => s is not null))}"
            : ClosedAt is not null ? "substituída sem resposta" : "aguardando o clique";
        return $"[escolha {Mode}: {opts}{(AllowOther ? " | outra" : string.Empty)} — {state}]";
    }

    private static string Slug(string? id)
    {
        var s = SlugRegex().Replace((id ?? string.Empty).Trim().ToLowerInvariant(), "-").Trim('-');
        return s.Length <= MaxIdLength ? s : s[..MaxIdLength];
    }

    [GeneratedRegex(@"[^a-z0-9_-]+")]
    private static partial Regex SlugRegex();

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
