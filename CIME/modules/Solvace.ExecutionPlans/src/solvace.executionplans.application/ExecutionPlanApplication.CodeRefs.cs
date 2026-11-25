using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// 0075: fotografias do código citado na análise/correção. A skill grava (<c>PUT code-refs/{ref}</c> pelo
/// <c>prmake-plan.sh coderef</c>, ou ajusta pelo MCP), a tela lê as cabeças do card e o conteúdo de uma ref sob demanda.
/// Card sem plano (itens de trabalho avulsos): grava pelo card (<c>PUT/PATCH card/{card}/code-refs/{ref}</c>), com PlanId nulo.
/// </summary>
public partial class ExecutionPlanApplication
{
    public async Task<ExecutionCodeRefResponse> SaveCodeRefAsync(Guid planId, string reference, ExecutionCodeCapture capture,
        ExecutionActor actor, CancellationToken cancellationToken)
    {
        var key = ExecutionCodeRef.NormalizeRef(reference);
        ExecutionCodeRef? saved = null;
        var plan = await MutateAsync(planId, async p =>
        {
            var now = DateTimeOffset.UtcNow;
            var codeRef = await _repository.FindCodeRefAsync(p.Id, key, cancellationToken);
            if (codeRef is null)
            {
                if (await _repository.CountCodeRefsAsync(p.Id, cancellationToken) >= ExecutionCodeRef.MaxPerPlan)
                    throw new DomainException($"O plano já tem {ExecutionCodeRef.MaxPerPlan} fotografias de código.");
                codeRef = new ExecutionCodeRef(p.Id, p.CardNumber, key, actor.Name, now);
                codeRef.Capture(capture, now);
                _repository.AddCodeRef(codeRef);
            }
            else
            {
                codeRef.Capture(capture, now);
            }
            p.Touch(now, fromExecutor: actor.IsExecutor);
            saved = codeRef;
        }, cancellationToken, actor);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.CodeRef, null, cancellationToken);
        return saved!.ToResponse();
    }

    /// <summary>
    /// Ajuste sem nova fotografia. A ref pode ser de outro plano do card (a correção registra o diff feito na ref que a
    /// análise fotografou): procura no plano e, sem ela, na mais recente do card.
    /// </summary>
    public async Task<ExecutionCodeRefResponse> PatchCodeRefAsync(Guid planId, string reference, ExecutionCodeRefPatch patch,
        ExecutionActor actor, CancellationToken cancellationToken)
    {
        var key = ExecutionCodeRef.NormalizeRef(reference);
        var target = await LoadAsync(planId, cancellationToken);
        var ownerPlanId = planId;
        if (await _repository.FindCodeRefAsync(planId, key, cancellationToken) is null)
        {
            var head = LatestByRef(await _repository.GetCodeRefHeadsAsync(target.CardNumber, null, cancellationToken))
                .FirstOrDefault(h => h.Ref == key)
                ?? throw new ExecutionPlanNotFoundException($"Ref '{key}' não encontrada no card — grave a fotografia antes (coderef).");
            _repository.ClearTracking();
            // A mais recente é a fotografia do card sem plano (gravada antes de o card ter plano): ajusta ela.
            if (head.PlanId is not { } owner)
                return await PatchCardLevelCodeRefAsync(target.CardNumber, key, patch, cancellationToken);
            ownerPlanId = owner;
        }
        _repository.ClearTracking();

        ExecutionCodeRef? saved = null;
        var plan = await MutateAsync(ownerPlanId, async p =>
        {
            var now = DateTimeOffset.UtcNow;
            var codeRef = await _repository.FindCodeRefAsync(p.Id, key, cancellationToken)
                          ?? throw new ExecutionPlanNotFoundException($"Ref '{key}' não encontrada.");
            codeRef.Apply(patch, now);
            p.Touch(now, fromExecutor: actor.IsExecutor);
            saved = codeRef;
        }, cancellationToken, actor);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.CodeRef, null, cancellationToken);
        return saved!.ToResponse();
    }

    public async Task DeleteCodeRefAsync(Guid planId, string reference, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var key = ExecutionCodeRef.NormalizeRef(reference);
        var plan = await MutateAsync(planId, async p =>
        {
            var codeRef = await _repository.FindCodeRefAsync(p.Id, key, cancellationToken)
                          ?? throw new ExecutionPlanNotFoundException($"Ref '{key}' não encontrada.");
            _repository.RemoveCodeRef(codeRef);
            p.Touch(DateTimeOffset.UtcNow, fromExecutor: actor.IsExecutor);
        }, cancellationToken, actor);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.CodeRef, null, cancellationToken);
    }

    /// <summary>
    /// Grava pelo card (<c>PUT ExecutionPlan/card/{card}/code-refs/{ref}</c>): card com plano = o mesmo de
    /// <see cref="SaveCodeRefAsync"/> no plano do card (<see cref="ExecutionCodeRef.PlanForCard"/>); sem plano (itens de
    /// trabalho avulsos) = fotografia do card, com <c>PlanId</c> nulo e o mesmo limite por card.
    /// </summary>
    public async Task<ExecutionCodeRefResponse> SaveCardCodeRefAsync(string cardNumber, string reference, ExecutionCodeCapture capture,
        ExecutionActor actor, CancellationToken cancellationToken)
    {
        var card = NormalizeCodeRefCard(cardNumber);
        var key = ExecutionCodeRef.NormalizeRef(reference);
        if (ExecutionCodeRef.PlanForCard(await _repository.GetCardPlanStatusesAsync(card, cancellationToken)) is { } planId)
            return await SaveCodeRefAsync(planId, key, capture, actor, cancellationToken);

        var saved = await WriteCardLevelCodeRefAsync(card, key, async now =>
        {
            var codeRef = await _repository.FindCardCodeRefAsync(card, key, cancellationToken);
            if (codeRef is null)
            {
                if (await _repository.CountCardCodeRefsAsync(card, cancellationToken) >= ExecutionCodeRef.MaxPerPlan)
                    throw new DomainException($"O card já tem {ExecutionCodeRef.MaxPerPlan} fotografias de código.");
                codeRef = new ExecutionCodeRef(null, card, key, actor.Name, now);
                codeRef.Capture(capture, now);
                _repository.AddCodeRef(codeRef);
            }
            else
            {
                codeRef.Capture(capture, now);
            }
            return codeRef;
        }, cancellationToken);
        return saved.ToResponse();
    }

    /// <summary>
    /// Ajusta pelo card (<c>PATCH ExecutionPlan/card/{card}/code-refs/{ref}</c>): com plano, o mesmo de
    /// <see cref="PatchCodeRefAsync"/> (procura no plano e, sem ela, a mais recente do card); sem plano, a fotografia do card.
    /// </summary>
    public async Task<ExecutionCodeRefResponse> PatchCardCodeRefAsync(string cardNumber, string reference, ExecutionCodeRefPatch patch,
        ExecutionActor actor, CancellationToken cancellationToken)
    {
        var card = NormalizeCodeRefCard(cardNumber);
        var key = ExecutionCodeRef.NormalizeRef(reference);
        if (ExecutionCodeRef.PlanForCard(await _repository.GetCardPlanStatusesAsync(card, cancellationToken)) is { } planId)
            return await PatchCodeRefAsync(planId, key, patch, actor, cancellationToken);
        return await PatchCardLevelCodeRefAsync(card, key, patch, cancellationToken);
    }

    private async Task<ExecutionCodeRefResponse> PatchCardLevelCodeRefAsync(string card, string key, ExecutionCodeRefPatch patch,
        CancellationToken cancellationToken)
    {
        var saved = await WriteCardLevelCodeRefAsync(card, key, async now =>
        {
            var codeRef = await _repository.FindCardCodeRefAsync(card, key, cancellationToken)
                          ?? throw new ExecutionPlanNotFoundException($"Ref '{key}' não encontrada no card — grave a fotografia antes (coderef).");
            codeRef.Apply(patch, now);
            return codeRef;
        }, cancellationToken);
        return saved.ToResponse();
    }

    /// <summary>
    /// Fotografia do card sem plano: sem o agregado do plano para conferir a versão, grava direto; a mesma ref gravada ao
    /// mesmo tempo (índice único parcial) relê e tenta de novo. Avisa o grupo do card (a tela relê as cabeças).
    /// </summary>
    private async Task<ExecutionCodeRef> WriteCardLevelCodeRefAsync(string card, string key, Func<DateTimeOffset, Task<ExecutionCodeRef>> write,
        CancellationToken cancellationToken)
    {
        ExecutionCodeRef saved;
        for (var attempt = 1; ; attempt++)
        {
            saved = await write(DateTimeOffset.UtcNow);
            try
            {
                await _repository.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (ExecutionPlanConcurrencyException) when (attempt < ConcurrencyRetries)
            {
                _repository.ClearTracking();
                await Task.Delay(Random.Shared.Next(10, 40) * attempt, cancellationToken);
            }
        }

        try
        {
            await _realTimeNotifier.NotifyGroupAsync(
                ExecutionPlanRealTimeEvents.Group(card),
                ExecutionPlanRealTimeEvents.EventPlanUpdated,
                new { cardNumber = card, planId = (Guid?)null, action = ExecutionPlanRealTimeEvents.Actions.CodeRef, stepKey = (string?)null, status = (string?)null },
                cancellationToken);
        }
        catch
        {
            // Tempo real é best-effort: a tela relê as cabeças ao abrir.
        }
        return saved;
    }

    private static string NormalizeCodeRefCard(string? cardNumber)
    {
        var card = (cardNumber ?? string.Empty).Trim();
        if (card.Length is 0 or > 100)
            throw new DomainException("Card inválido.");
        return card;
    }

    public async Task<List<ExecutionCodeRefHeadResponse>> GetCodeRefHeadsAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await LoadAsync(planId, cancellationToken);
        return await _repository.GetCodeRefHeadsAsync(plan.CardNumber, plan.Id, cancellationToken);
    }

    /// <summary>Cabeças do card: uma por ref — a mais recente, se a mesma ref foi fotografada em mais de um plano.</summary>
    public async Task<List<ExecutionCodeRefHeadResponse>> GetCardCodeRefHeadsAsync(string cardNumber, CancellationToken cancellationToken) =>
        LatestByRef(await _repository.GetCodeRefHeadsAsync(cardNumber.Trim(), null, cancellationToken)).ToList();

    public async Task<ExecutionCodeRefResponse> GetCardCodeRefAsync(string cardNumber, string reference, CancellationToken cancellationToken)
    {
        var key = ExecutionCodeRef.NormalizeRef(reference);
        var head = LatestByRef(await _repository.GetCodeRefHeadsAsync(cardNumber.Trim(), null, cancellationToken))
                       .FirstOrDefault(h => h.Ref == key)
                   ?? throw new ExecutionPlanNotFoundException($"Ref '{key}' não encontrada no card.");
        var codeRef = await _repository.GetCodeRefAsync(head.Id, cancellationToken)
                      ?? throw new ExecutionPlanNotFoundException($"Ref '{key}' não encontrada no card.");
        return codeRef.ToResponse();
    }

    public async Task<List<ExecutionArtifactResponse>> GetCardArtifactsAsync(string cardNumber, CancellationToken cancellationToken) =>
        (await _repository.GetCardArtifactsAsync(cardNumber.Trim(), cancellationToken)).Select(a => a.ToResponse()).ToList();

    /// <summary>
    /// O texto cita a imagem pelo nome (<c>![Depois](anexo:mock-depois.png)</c>) e pode estar na Timeline, fora do plano:
    /// procura em todos os planos do card — arquivo da skill antes de anexo de comentário, o mais recente — e, sem nenhum,
    /// nos arquivos dos itens de trabalho do card (o card sem plano só tem esses), o mais recente (<see cref="CardFileByName"/>).
    /// Lista sem o conteúdo; os bytes só do escolhido.
    /// </summary>
    public async Task<ExecutionArtifactFile> GetCardArtifactFileByNameAsync(string cardNumber, string name, CancellationToken cancellationToken)
    {
        var card = (cardNumber ?? string.Empty).Trim();
        var wanted = CardFileByName.Normalize(name);
        if (wanted.Length == 0)
            throw new ExecutionPlanNotFoundException("Informe o nome do arquivo.");

        if (CardFileByName.PickArtifact(await _repository.GetCardArtifactsAsync(card, cancellationToken), wanted) is { } artifact)
        {
            var data = await _repository.GetArtifactContentAsync(artifact.Id, cancellationToken) ?? [];
            return new ExecutionArtifactFile(artifact.ToResponse(), data);
        }

        var file = CardFileByName.PickWorkItemFile(await _repository.GetCardWorkItemFilesByNameAsync(card, wanted, cancellationToken), wanted)
                   ?? throw new ExecutionPlanNotFoundException($"Arquivo '{wanted}' não encontrado no card.");
        var bytes = await _repository.GetWorkItemFileContentAsync(file.Id, cancellationToken) ?? [];
        return new ExecutionArtifactFile(new ExecutionArtifactResponse
        {
            Id = file.Id,
            Number = file.Number,
            Name = file.Name,
            Kind = file.Kind,
            ContentType = file.ContentType,
            Size = file.Size,
            Sha256 = file.Sha256,
            CreatedBy = file.CreatedBy,
            CreatedAt = file.CreatedAt,
            UpdatedAt = file.UpdatedAt,
        }, bytes);
    }

    private static IEnumerable<ExecutionCodeRefHeadResponse> LatestByRef(IEnumerable<ExecutionCodeRefHeadResponse> heads) =>
        heads.GroupBy(h => h.Ref)
            .Select(g => g.OrderByDescending(h => h.UpdatedAt).First())
            .OrderBy(h => h.Ref, StringComparer.Ordinal);
}
