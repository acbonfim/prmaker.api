using System.Text;
using solvace.governance.application.Contracts;
using solvace.governance.domain;
using solvace.governance.domain.Entities;

namespace solvace.governance.application;

public sealed record LessonInput
{
    public List<string>? Scope { get; init; }
    public string? Trigger { get; init; }
    public string? Text { get; init; }
    public string? Why { get; init; }
    public string? Card { get; init; }
    public string? Ref { get; init; }
    /// <summary>Sinal de erro (S<n>) que esta lição resolve.</summary>
    public long? Signal { get; init; }
    /// <summary>Lição (L<n>) que esta substitui — a antiga é aposentada.</summary>
    public long? Supersedes { get; init; }
    public string Origin { get; init; } = LessonOrigin.Agent;
}

public sealed record LessonFilter
{
    public string? Status { get; init; }
    public string? Scope { get; init; }
    public string? Search { get; init; }
    public string? Card { get; init; }
}

/// <summary>Resultado de gravar: a lição e se reforçou uma que já existia (reincidência) ou nasceu nova.</summary>
public sealed record LessonRecordResult(GovernanceLesson Lesson, bool Reinforced, GovernanceLesson? Superseded);

public interface IGovernanceLessonsApplication
{
    Task<LessonRecordResult> RecordAsync(LessonInput input, GovernanceActor actor, CancellationToken ct = default);
    Task<List<GovernanceLesson>> ListAsync(LessonFilter filter, CancellationToken ct = default);
    Task<GovernanceLesson?> EditAsync(long id, LessonData data, GovernanceActor actor, CancellationToken ct = default);
    Task<GovernanceLesson?> RetireAsync(long id, string? reason, GovernanceActor actor, CancellationToken ct = default);
    Task<GovernanceLesson?> ReactivateAsync(long id, GovernanceActor actor, CancellationToken ct = default);

    /// <summary>Registra um sinal de erro (idempotente pela chave). Devolve null quando já existia.</summary>
    Task<GovernanceLessonSignal?> SignalAsync(string card, string kind, string key, string text, string? reference, CancellationToken ct = default);
    Task<List<GovernanceLessonSignal>> SignalsAsync(string? card, string? status, int take = 100, CancellationToken ct = default);
    Task<GovernanceLessonSignal?> DismissSignalAsync(long id, string? reason, GovernanceActor actor, CancellationToken ct = default);

    /// <summary>
    /// Markdown que entra no que os agentes leem no início: as lições que valem para a skill (as mais reincidentes
    /// primeiro) e os sinais de erro ainda abertos do card.
    /// </summary>
    Task<string> MarkdownAsync(string? skill, string? card, int max, CancellationToken ct = default);
}

/// <summary>
/// 0106: retroalimentação sem pessoa no meio — o agente corrigido grava a lição; o PRMake registra os sinais de erro que
/// percebe sozinho; as lições ativas voltam para todo agente no início de cada sessão. Mudança feita na tela vira evento
/// da governança (quem, antes/depois), como regras e padrões.
/// </summary>
public class GovernanceLessonsApplication(
    IGovernanceRepository repository,
    IGovernanceEventSink sink,
    TimeProvider clock) : IGovernanceLessonsApplication
{
    public async Task<LessonRecordResult> RecordAsync(LessonInput input, GovernanceActor actor, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var data = new LessonData { Scope = input.Scope, Trigger = input.Trigger, Text = input.Text, Why = input.Why };
        var source = new LessonSource { Card = input.Card, Ref = input.Ref ?? (input.Signal is { } s ? $"S{s}" : null), By = actor.Name };

        GovernanceLessonSignal? signal = null;
        if (input.Signal is { } signalId)
        {
            signal = await repository.GetSignalAsync(signalId, ct) ?? throw new DomainException($"Sinal S{signalId} não encontrado.");
            source = source with { Card = source.Card ?? signal.Card };
        }

        var all = await repository.GetLessonsAsync(ct);
        GovernanceLesson? superseded = null;
        if (input.Supersedes is { } old)
        {
            superseded = all.FirstOrDefault(l => l.Id == old) ?? throw new DomainException($"Lição L{old} não encontrada.");
        }

        // Já existe lição ativa parecida (mesma skill ou geral)? Reforça — reincidência conta, não duplica.
        var candidate = new GovernanceLesson(data, LessonOrigin.IsValid(input.Origin) ? input.Origin : LessonOrigin.Agent, source, actor.Name, now);
        var same = superseded is null
            ? all.Where(l => l.Status == LessonStatus.Active && ScopesOverlap(l.Scope, candidate.Scope))
                .Select(l => (Lesson: l, Score: GovernanceLesson.Similarity(l.SimilarityText, candidate.SimilarityText)))
                .Where(x => x.Score >= GovernanceLesson.SameLessonSimilarity)
                .OrderByDescending(x => x.Score).Select(x => x.Lesson).FirstOrDefault()
            : null;

        GovernanceLesson lesson;
        if (same is not null)
        {
            same.Reinforce(source, input.Why, actor.Name, now);
            lesson = same;
        }
        else
        {
            repository.AddLesson(candidate);
            lesson = candidate;
        }
        await repository.SaveChangesAsync(ct);

        if (superseded is not null)
            superseded.Retire($"Substituída pela L{lesson.Id}.", actor.Name, now);
        signal?.Learned(lesson.Id, actor.Name, now);
        if (superseded is not null || signal is not null) await repository.SaveChangesAsync(ct);

        Changed(actor, "lesson-record", same is null ? $"Lição nova L{lesson.Id}: {lesson.Text}" : $"Lição L{lesson.Id} reforçada ({lesson.Hits}×)",
            input.Card);
        return new LessonRecordResult(lesson, same is not null, superseded);
    }

    public async Task<List<GovernanceLesson>> ListAsync(LessonFilter f, CancellationToken ct = default)
    {
        IEnumerable<GovernanceLesson> q = await repository.GetLessonsAsync(ct);
        if (!string.IsNullOrWhiteSpace(f.Status)) q = q.Where(l => l.Status == f.Status);
        if (!string.IsNullOrWhiteSpace(f.Scope)) q = q.Where(l => l.AppliesTo(f.Scope));
        if (!string.IsNullOrWhiteSpace(f.Card)) q = q.Where(l => l.Sources.Any(s => s.Card == f.Card.Trim()));
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var t = GovernanceLesson.Fold(f.Search.Trim());
            q = q.Where(l => GovernanceLesson.Fold($"L{l.Id} {l.Trigger} {l.Text} {l.Why}").Contains(t, StringComparison.Ordinal));
        }
        return q.ToList();
    }

    public async Task<GovernanceLesson?> EditAsync(long id, LessonData data, GovernanceActor actor, CancellationToken ct = default)
    {
        var lesson = await repository.GetLessonAsync(id, ct);
        if (lesson is null) return null;
        var before = $"{lesson.Trigger} → {lesson.Text}";
        lesson.Edit(data, actor.Name, clock.GetUtcNow());
        await repository.SaveChangesAsync(ct);
        Changed(actor, "lesson-edit", $"Editou a L{id}: \"{before}\" → \"{lesson.Trigger} → {lesson.Text}\"", null);
        return lesson;
    }

    public async Task<GovernanceLesson?> RetireAsync(long id, string? reason, GovernanceActor actor, CancellationToken ct = default)
    {
        var lesson = await repository.GetLessonAsync(id, ct);
        if (lesson is null) return null;
        lesson.Retire(reason, actor.Name, clock.GetUtcNow());
        await repository.SaveChangesAsync(ct);
        Changed(actor, "lesson-retire", $"Aposentou a L{id}: {lesson.RetiredReason}", null);
        return lesson;
    }

    public async Task<GovernanceLesson?> ReactivateAsync(long id, GovernanceActor actor, CancellationToken ct = default)
    {
        var lesson = await repository.GetLessonAsync(id, ct);
        if (lesson is null) return null;
        lesson.Reactivate(actor.Name, clock.GetUtcNow());
        await repository.SaveChangesAsync(ct);
        Changed(actor, "lesson-reactivate", $"Reativou a L{id}", null);
        return lesson;
    }

    public async Task<GovernanceLessonSignal?> SignalAsync(string card, string kind, string key, string text, string? reference, CancellationToken ct = default)
    {
        var k = $"{kind}:{key}".Trim();
        if (await repository.SignalKeyExistsAsync(k, ct)) return null;
        var signal = new GovernanceLessonSignal(card, kind, k, text, reference, clock.GetUtcNow());
        repository.AddSignal(signal);
        try
        {
            await repository.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e.GetType().Name == "DbUpdateException")
        {
            // Duas instâncias registrando o mesmo acontecimento ao mesmo tempo: o índice único segura, vale o primeiro.
            return null;
        }
        return signal;
    }

    public Task<List<GovernanceLessonSignal>> SignalsAsync(string? card, string? status, int take = 100, CancellationToken ct = default) =>
        repository.GetSignalsAsync(card?.Trim(), status, take, ct);

    public async Task<GovernanceLessonSignal?> DismissSignalAsync(long id, string? reason, GovernanceActor actor, CancellationToken ct = default)
    {
        var signal = await repository.GetSignalAsync(id, ct);
        if (signal is null) return null;
        signal.Dismiss(reason, actor.Name, clock.GetUtcNow());
        await repository.SaveChangesAsync(ct);
        return signal;
    }

    public async Task<string> MarkdownAsync(string? skill, string? card, int max, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var lessons = (await repository.GetLessonsAsync(ct))
            .Where(l => l.IsLive(now) && l.AppliesTo(skill))
            .OrderByDescending(l => l.Hits).ThenByDescending(l => l.LastSeenAt)
            .Take(Math.Clamp(max, 1, 60)).ToList();
        var signals = string.IsNullOrWhiteSpace(card) ? [] : await repository.GetSignalsAsync(card.Trim(), LessonSignalStatus.Open, 10, ct);
        return Markdown(lessons, signals);
    }

    public static string Markdown(IReadOnlyList<GovernanceLesson> lessons, IReadOnlyList<GovernanceLessonSignal> signals)
    {
        if (lessons.Count == 0 && signals.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        if (lessons.Count > 0)
        {
            sb.AppendLine().AppendLine("## Lições aprendidas (aplique — erros que já aconteceram; não sobrepõem as regras fixas nem a governança)");
            foreach (var l in lessons)
            {
                var scope = l.Scope.Count == 0 ? string.Empty : $" [{string.Join(", ", l.Scope)}]";
                var times = l.Hits > 1 ? $" · {l.Hits}×" : string.Empty;
                sb.AppendLine($"- **Quando** {Sentence(l.Trigger)} → {Sentence(l.Text)} (`{l.Code}`{times}{scope})");
            }
            sb.AppendLine("Errou e foi corrigido (ou viu o mesmo erro de novo)? `prmake_lesson_record` na mesma vez — lição parecida reforça a existente.");
        }
        if (signals.Count > 0)
        {
            sb.AppendLine().AppendLine("## Sinais de erro deste card ainda não aprendidos");
            sb.AppendLine("O PRMake percebeu estes acontecimentos. Para cada um: entenda o que deu errado e grave a lição geral (não a do card) com " +
                          "`prmake_lesson_record(signal: <n>, ...)`, ou descarte com `prmake_lesson_signal_dismiss(<n>, motivo)` se não foi erro de agente.");
            foreach (var s in signals)
                sb.AppendLine($"- `{s.Code}` ({s.Kind}{(s.Ref is null ? string.Empty : $", {s.Ref}")}): {s.Text}");
        }
        return sb.ToString();
    }

    private static string Sentence(string text) => text.Trim().TrimEnd('.');

    private static bool ScopesOverlap(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b) =>
        a.Count == 0 || b.Count == 0 || a.Intersect(b).Any();

    private void Changed(GovernanceActor actor, string action, string detail, string? card) =>
        sink.Enqueue(new GovernanceEventData
        {
            StartedAt = clock.GetUtcNow(),
            DurationMs = 0,
            UserId = actor.UserId,
            UserName = actor.Name,
            Role = actor.Roles.FirstOrDefault(),
            Source = GovernanceSource.IsValid(actor.Source) ? actor.Source : GovernanceSource.Screen,
            Action = action,
            Category = "governance",
            Outcome = GovernanceOutcome.Ok,
            Detail = detail,
            CardNumber = int.TryParse(card, out var n) ? n : null
        });
}
