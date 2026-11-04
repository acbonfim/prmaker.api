using System.Text.RegularExpressions;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0075: fotografia de um trecho de código citado na análise/correção — o código real de um commit, o foco
/// (<see cref="StartLine"/>–<see cref="EndLine"/>), a mudança proposta em diff unificado e as notas que explicam cada
/// trecho. Quem tira a fotografia é a skill (o servidor não tem os repositórios). Única por (plano, <see cref="Ref"/>);
/// o texto cita <c>[...](ref:&lt;ref&gt;)</c> ou <c>caminho:linha</c>, que a tela liga a ela.
/// </summary>
public partial class ExecutionCodeRef
{
    public const int MaxRefLength = 60;
    public const int MaxRepositoryLength = 200;
    public const int MaxPathLength = 500;
    public const int MaxBranchLength = 200;
    public const int MaxLanguageLength = 30;
    public const int MaxTitleLength = 200;
    public const int MaxWhyLength = 2_000;
    public const int MaxSnapshotChars = 64 * 1024;
    public const int MaxSnapshotLines = 600;
    public const int MaxPatchChars = 64 * 1024;
    public const int MaxAnnotations = 30;
    public const int MaxAnnotationLength = 1_000;

    /// <summary>Refs por plano (fotografias são pequenas, mas a lista vai inteira para a tela).</summary>
    public const int MaxPerPlan = 80;

    public Guid Id { get; private set; }
    public Guid PlanId { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public string Ref { get; private set; } = string.Empty;

    public string? Repository { get; private set; }
    public string Path { get; private set; } = string.Empty;
    public string? CommitSha { get; private set; }
    public string? Branch { get; private set; }
    public string? Language { get; private set; }

    /// <summary>Foco: as linhas destacadas (no arquivo, não na fotografia). Em <c>create</c>, as linhas do arquivo novo.</summary>
    public int StartLine { get; private set; }
    public int EndLine { get; private set; }

    /// <summary><see cref="ExecutionCodeChange"/>: change | create | remove | suspect | read.</summary>
    public string Change { get; private set; } = ExecutionCodeChange.Read;
    public string? Title { get; private set; }

    /// <summary>Por que este trecho importa (1–3 frases). Obrigatório fora de <c>read</c>/<c>suspect</c>.</summary>
    public string? Why { get; private set; }

    /// <summary>O código real (foco ± contexto); <see cref="FirstLine"/> é o número da 1ª linha. Vazio em <c>create</c>.</summary>
    public string? Snapshot { get; private set; }
    public int FirstLine { get; private set; } = 1;

    /// <summary>Mudança proposta em diff unificado (<c>create</c>: o diff do arquivo novo).</summary>
    public string? Proposal { get; private set; }

    /// <summary>Correção: o diff real do commit que fez a mudança, e o commit.</summary>
    public string? Implemented { get; private set; }
    public string? ImplementedSha { get; private set; }

    /// <summary>Notas por faixa de linhas (JSON: <see cref="ExecutionCodeAnnotation"/>).</summary>
    public List<ExecutionCodeAnnotation> Annotations { get; private set; } = [];

    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    protected ExecutionCodeRef() { }

    public ExecutionCodeRef(Guid planId, string cardNumber, string reference, string createdBy, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        PlanId = planId;
        CardNumber = cardNumber;
        Ref = NormalizeRef(reference);
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex RefPattern();

    public static string NormalizeRef(string? reference)
    {
        var r = (reference ?? string.Empty).Trim().ToLowerInvariant();
        if (r.Length == 0 || r.Length > MaxRefLength || !RefPattern().IsMatch(r))
            throw new DomainException($"Ref inválida: '{reference}'. Use letras minúsculas, números, '.', '_' ou '-' (até {MaxRefLength}).");
        return r;
    }

    /// <summary>Grava a fotografia inteira (o <c>coderef</c> da skill). Campos nulos ficam vazios — é uma substituição.</summary>
    public void Capture(ExecutionCodeCapture capture, DateTimeOffset now)
    {
        var path = (capture.Path ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
        if (path.Length == 0)
            throw new DomainException("Informe o caminho do arquivo.");
        if (path.Length > MaxPathLength)
            throw new DomainException($"O caminho pode ter no máximo {MaxPathLength} caracteres.");

        Path = path;
        Repository = Clip(capture.Repository, MaxRepositoryLength);
        CommitSha = Clip(capture.CommitSha, 64);
        Branch = Clip(capture.Branch, MaxBranchLength);
        Language = Clip(capture.Language?.ToLowerInvariant(), MaxLanguageLength);
        Snapshot = NormalizeSnapshot(capture.Snapshot);
        FirstLine = Math.Max(1, capture.FirstLine ?? 1);
        StartLine = capture.StartLine;
        EndLine = capture.EndLine ?? capture.StartLine;
        Implemented = null;
        ImplementedSha = null;
        Change = ExecutionCodeChange.Read;
        Title = null;
        Why = null;
        Proposal = null;
        Annotations = [];
        Apply(new ExecutionCodeRefPatch(capture.Change, capture.Title, capture.Why, capture.Proposal, capture.Annotations), now);
    }

    /// <summary>Ajuste sem nova fotografia (MCP <c>prmake_code_ref</c>): só o que vier preenchido muda.</summary>
    public void Apply(ExecutionCodeRefPatch patch, DateTimeOffset now)
    {
        if (patch.Change is not null)
        {
            var change = patch.Change.Trim().ToLowerInvariant();
            if (!ExecutionCodeChange.All.Contains(change))
                throw new DomainException($"Mudança inválida: '{patch.Change}'. Use {string.Join(", ", ExecutionCodeChange.All)}.");
            Change = change;
        }
        if (patch.Title is not null)
            Title = Clip(patch.Title, MaxTitleLength);
        if (patch.Why is not null)
        {
            var why = patch.Why.Trim();
            if (why.Length > MaxWhyLength)
                throw new DomainException($"O 'por quê' pode ter no máximo {MaxWhyLength} caracteres.");
            Why = why.Length == 0 ? null : why;
        }
        if (patch.Proposal is not null)
            Proposal = NormalizePatch(patch.Proposal, "proposta");
        if (patch.Annotations is not null)
            Annotations = NormalizeAnnotations(patch.Annotations);
        if (patch.Implemented is not null)
        {
            Implemented = NormalizePatch(patch.Implemented, "mudança feita");
            ImplementedSha = Clip(patch.ImplementedSha, 64);
        }
        Validate();
        UpdatedAt = now;
    }

    private void Validate()
    {
        if (StartLine < 1 || EndLine < StartLine)
            throw new DomainException($"Linhas inválidas: {StartLine}-{EndLine}.");
        if (Change != ExecutionCodeChange.Create && Snapshot is null)
            throw new DomainException("Falta a fotografia do código (só 'create' pode vir sem ela).");
        if (Snapshot is not null)
        {
            var last = FirstLine + LineCount(Snapshot) - 1;
            if (StartLine < FirstLine || EndLine > last)
                throw new DomainException($"O foco {StartLine}-{EndLine} está fora da fotografia ({FirstLine}-{last}).");
            if (Annotations.FirstOrDefault(a => a.Side == ExecutionCodeAnnotation.Old && (a.From < FirstLine || a.To > last)) is { } outside)
                throw new DomainException($"A nota das linhas {outside.From}-{outside.To} está fora da fotografia ({FirstLine}-{last}).");
        }
        if (Change is not (ExecutionCodeChange.Read or ExecutionCodeChange.Suspect) && Why is null)
            throw new DomainException($"Ref '{Ref}': diga por que o trecho muda ('why').");
        if ((Change is ExecutionCodeChange.Change or ExecutionCodeChange.Create or ExecutionCodeChange.Remove) && Proposal is null)
            throw new DomainException($"Ref '{Ref}': falta a mudança proposta ('proposal', diff unificado).");
    }

    private static string? NormalizeSnapshot(string? snapshot)
    {
        if (string.IsNullOrEmpty(snapshot)) return null;
        var text = snapshot.Replace("\r\n", "\n");
        if (text.Length > MaxSnapshotChars)
            throw new DomainException($"A fotografia passa de {MaxSnapshotChars / 1024} KB — recorte menos contexto.");
        if (LineCount(text) > MaxSnapshotLines)
            throw new DomainException($"A fotografia passa de {MaxSnapshotLines} linhas — recorte menos contexto.");
        return text;
    }

    private static string? NormalizePatch(string patch, string what)
    {
        var text = patch.Replace("\r\n", "\n");
        if (text.Trim().Length == 0) return null;
        if (text.Length > MaxPatchChars)
            throw new DomainException($"A {what} passa de {MaxPatchChars / 1024} KB.");
        return text;
    }

    private static List<ExecutionCodeAnnotation> NormalizeAnnotations(IEnumerable<ExecutionCodeAnnotation> annotations)
    {
        var list = annotations.ToList();
        if (list.Count > MaxAnnotations)
            throw new DomainException($"No máximo {MaxAnnotations} notas por ref.");
        return list.Select(a =>
        {
            var side = (a.Side ?? ExecutionCodeAnnotation.Old).Trim().ToLowerInvariant();
            if (side is not (ExecutionCodeAnnotation.Old or ExecutionCodeAnnotation.New))
                throw new DomainException($"Lado da nota inválido: '{a.Side}' (old | new).");
            var to = a.To ?? a.From;
            if (a.From < 1 || to < a.From)
                throw new DomainException($"Linhas da nota inválidas: {a.From}-{to}.");
            var text = (a.Text ?? string.Empty).Trim();
            if (text.Length == 0)
                throw new DomainException("Nota vazia.");
            if (text.Length > MaxAnnotationLength)
                throw new DomainException($"Cada nota pode ter no máximo {MaxAnnotationLength} caracteres.");
            return new ExecutionCodeAnnotation { Side = side, From = a.From, To = to, Text = text };
        }).ToList();
    }

    private static int LineCount(string text) => text.TrimEnd('\n').Split('\n').Length;

    private static string? Clip(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length <= max ? v : v[..max];
    }

    /// <summary>A citação <c>caminho:ini-fim</c> aponta para esta ref? (o caminho citado pode ser só o fim do caminho).</summary>
    public bool Matches(string citedPath, int start, int end) => Matches(Path, Repository, StartLine, EndLine, citedPath, start, end);

    /// <summary>Mesma regra para a cabeça da ref (lista do card) — e a da tela.</summary>
    public static bool Matches(string path, string? repository, int startLine, int endLine, string citedPath, int start, int end)
    {
        var cited = citedPath.Trim().Replace('\\', '/').TrimStart('/');
        if (cited.Length == 0) return false;
        var samePath = path.Equals(cited, StringComparison.OrdinalIgnoreCase)
                       || path.EndsWith("/" + cited, StringComparison.OrdinalIgnoreCase)
                       || (repository is not null && $"{repository}/{path}".Equals(cited, StringComparison.OrdinalIgnoreCase));
        return samePath && start <= endLine && end >= startLine;
    }
}

/// <summary>0075: tipo de mudança de uma <see cref="ExecutionCodeRef"/>.</summary>
public static class ExecutionCodeChange
{
    public const string Change = "change";
    public const string Create = "create";
    public const string Remove = "remove";
    public const string Suspect = "suspect";
    public const string Read = "read";

    public static readonly string[] All = [Change, Create, Remove, Suspect, Read];
}

/// <summary>0075: nota sobre as linhas <c>From</c>–<c>To</c> do código atual (<c>old</c>) ou do proposto (<c>new</c>).</summary>
public class ExecutionCodeAnnotation
{
    public const string Old = "old";
    public const string New = "new";

    public string? Side { get; set; }
    public int From { get; set; }
    public int? To { get; set; }
    public string? Text { get; set; }
}

/// <summary>0075: fotografia completa enviada pela skill.</summary>
public record ExecutionCodeCapture(
    string? Repository, string? Path, string? CommitSha, string? Branch, string? Language,
    int StartLine, int? EndLine, int? FirstLine, string? Snapshot,
    string? Change, string? Title, string? Why, string? Proposal, IReadOnlyList<ExecutionCodeAnnotation>? Annotations);

/// <summary>0075: ajuste parcial (null = não muda).</summary>
public record ExecutionCodeRefPatch(
    string? Change = null, string? Title = null, string? Why = null, string? Proposal = null,
    IReadOnlyList<ExecutionCodeAnnotation>? Annotations = null, string? Implemented = null, string? ImplementedSha = null);
