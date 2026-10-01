using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// Comentários do plano (0031): texto + anexos (imagens coladas/arrastadas, arquivos). Numerados por card
/// (<c>#n</c>) para serem citados no Claude ou no PRMake; a skill lê tudo como entrada da análise.
/// </summary>
public partial class ExecutionPlanApplication
{
    private const int TimelineNoteMaxChars = 1_500;

    public async Task<ExecutionNoteResponse> AddNoteAsync(Guid planId, string? text, string? stepKey,
        IReadOnlyList<ExecutionArtifactUpload> files, ExecutionActor actor, CancellationToken cancellationToken)
    {
        if (files.Count > ExecutionNote.MaxAttachments)
            throw new DomainException($"Anexe no máximo {ExecutionNote.MaxAttachments} arquivos por comentário.");
        foreach (var file in files)
        {
            if (file.Data.LongLength == 0)
                throw new DomainException($"O arquivo '{file.FileName}' está vazio.");
            if (file.Data.LongLength > ExecutionArtifact.MaxFileBytes)
                throw new DomainException($"O arquivo '{file.FileName}' passa do limite de {ExecutionArtifact.MaxFileBytes / (1024 * 1024)} MB.");
        }

        ExecutionNote? note = null;
        List<ExecutionArtifact> attachments = [];
        var plan = await MutateAsync(planId, async p =>
        {
            var now = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(stepKey))
            {
                var key = ExecutionStep.NormalizeKey(stepKey);
                if (!p.Steps.Any(s => s.Key == key))
                    throw new DomainException($"Etapa não encontrada: '{key}'.");
            }

            var existing = await _repository.GetArtifactsAsync(p.Id, cancellationToken);
            var incoming = files.Sum(f => f.Data.LongLength);
            if (existing.Sum(a => a.Size) + incoming > ExecutionArtifact.MaxPlanBytes)
                throw new DomainException($"Os arquivos deste plano passariam do limite de {ExecutionArtifact.MaxPlanBytes / (1024 * 1024)} MB.");

            note = new ExecutionNote(p.Id, p.CardNumber, await _repository.GetMaxNoteNumberAsync(p.CardNumber, cancellationToken) + 1,
                stepKey, text, files.Count > 0, actor.UserId, actor.Name, actor.IsExecutor, now);
            _repository.AddNote(note);

            var taken = existing.Select(a => (a.Kind, a.Name)).ToHashSet();
            var number = await _repository.GetMaxArtifactNumberAsync(p.CardNumber, cancellationToken);
            attachments = [];
            foreach (var file in files)
            {
                var baseName = ExecutionArtifact.NormalizeName(file.FileName);
                var kind = ExecutionArtifact.NormalizeKind(file.Kind, baseName);
                var name = ExecutionArtifact.UniqueName(baseName, n => taken.Contains((kind, n)));
                taken.Add((kind, name));

                var artifact = new ExecutionArtifact(p.Id, name, kind, actor.Name, now, ++number, note.Id);
                artifact.SetContent(file.Data, file.ContentType, note.StepKey, $"Anexo do comentário #{note.Number}", now, isNew: true);
                _repository.AddArtifact(artifact);
                attachments.Add(artifact);
            }
            p.Touch(now, fromExecutor: actor.IsExecutor);
        }, cancellationToken, actor, () => NoteTimelineText(note!, attachments));

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Note, note!.StepKey, cancellationToken);
        return note.ToResponse(plan.Phase, attachments);
    }

    public async Task<ExecutionNoteResponse> EditNoteAsync(Guid planId, Guid noteId, string? text, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ExecutionNote? note = null;
        List<ExecutionArtifact> attachments = [];
        var plan = await MutateAsync(planId, async p =>
        {
            note = await _repository.GetNoteAsync(p.Id, noteId, cancellationToken)
                   ?? throw new ExecutionPlanNotFoundException("Comentário não encontrado.");
            if (!note.CanBeChangedBy(actor.UserId, actor.IsExecutor))
                throw new DomainException("Só quem escreveu o comentário pode editá-lo.");
            attachments = await _repository.GetNoteAttachmentsAsync([note.Id], cancellationToken);
            var now = DateTimeOffset.UtcNow;
            note.Edit(text, attachments.Count > 0, now);
            p.Touch(now, fromExecutor: actor.IsExecutor);
        }, cancellationToken, actor);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Note, note!.StepKey, cancellationToken);
        return note.ToResponse(plan.Phase, attachments);
    }

    public async Task DeleteNoteAsync(Guid planId, Guid noteId, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ExecutionNote? note = null;
        var plan = await MutateAsync(planId, async p =>
        {
            note = await _repository.GetNoteAsync(p.Id, noteId, cancellationToken)
                   ?? throw new ExecutionPlanNotFoundException("Comentário não encontrado.");
            if (!note.CanBeChangedBy(actor.UserId, actor.IsExecutor))
                throw new DomainException("Só quem escreveu o comentário pode removê-lo.");
            var now = DateTimeOffset.UtcNow;
            note.Delete(actor.Name, now);
            foreach (var attachment in await _repository.GetNoteAttachmentsAsync([note.Id], cancellationToken))
                await _repository.RemoveArtifactAsync(attachment.Id, cancellationToken);
            p.Touch(now, fromExecutor: actor.IsExecutor);
        }, cancellationToken, actor);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Note, note!.StepKey, cancellationToken);
    }

    public async Task<List<ExecutionNoteResponse>> GetNotesByCardAsync(string cardNumber, CancellationToken cancellationToken, bool fromExecutor = false)
    {
        var card = cardNumber.Trim();
        var notes = await _repository.GetNotesByCardAsync(card, cancellationToken);
        if (notes.Count == 0) return [];

        // 0037: a skill leu os comentários — a tela mostra "lido · o Claude está analisando" no último do usuário.
        if (fromExecutor && notes.Where(n => !n.FromExecutor).Select(n => n.Number).DefaultIfEmpty(0).Max() is var lastUserNote and > 0
            && await _repository.MarkNotesReadAsync(card, lastUserNote, DateTimeOffset.UtcNow, cancellationToken)
            && await _repository.GetCurrentPlanIdAsync(card, cancellationToken) is { } currentId
            && await _repository.GetPlanWithStepsAsync(currentId, cancellationToken) is { } current)
        {
            _repository.ClearTracking();
            await NotifyAsync(current, ExecutionPlanRealTimeEvents.Actions.Note, null, cancellationToken);
        }

        var phases = (await _repository.GetSummariesByCardAsync(card, cancellationToken)).ToDictionary(p => p.Id, p => p.Phase);
        var attachments = (await _repository.GetNoteAttachmentsAsync(notes.Select(n => n.Id).ToList(), cancellationToken))
            .ToLookup(a => a.NoteId!.Value);
        return notes
            .Select(n => n.ToResponse(phases.GetValueOrDefault(n.PlanId, ExecutionPhase.Analysis), attachments[n.Id]))
            .ToList();
    }

    private static string NoteTimelineText(ExecutionNote note, IReadOnlyList<ExecutionArtifact> attachments)
    {
        var where = note.StepKey is null ? "" : $" (etapa *{note.StepKey}*)";
        var author = note.FromExecutor ? "Claude" : note.AuthorName;
        var lines = new List<string> { $"💬 **Comentário #{note.Number}** de {author} no plano{where}" };
        if (note.Text.Length > 0)
        {
            var text = note.Text.Length > TimelineNoteMaxChars ? note.Text[..TimelineNoteMaxChars] + "…" : note.Text;
            lines.Add(string.Join("\n", text.Split('\n').Select(l => "> " + l)));
        }
        if (attachments.Count > 0)
            lines.Add("📎 " + string.Join(", ", attachments.Select(a => $"anexo #{a.Number} `{a.Name}`")));
        return string.Join("\n\n", lines);
    }
}
