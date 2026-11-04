using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// 0075: fotografias do código citado na análise/correção. A skill grava (<c>PUT code-refs/{ref}</c> pelo
/// <c>prmake-plan.sh coderef</c>, ou ajusta pelo MCP), a tela lê as cabeças do card e o conteúdo de uma ref sob demanda.
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
            ownerPlanId = head.PlanId;
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
    /// procura em todos os planos do card — arquivo da skill antes de anexo de comentário, o mais recente.
    /// </summary>
    public async Task<ExecutionArtifactFile> GetCardArtifactFileByNameAsync(string cardNumber, string name, CancellationToken cancellationToken)
    {
        var wanted = (name ?? string.Empty).Trim();
        var artifact = (await _repository.GetCardArtifactsAsync(cardNumber.Trim(), cancellationToken))
                       .Where(a => string.Equals(a.Name, wanted, StringComparison.OrdinalIgnoreCase))
                       .OrderBy(a => a.NoteId is null ? 0 : 1)
                       .ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt)
                       .FirstOrDefault()
                       ?? throw new ExecutionPlanNotFoundException($"Arquivo '{wanted}' não encontrado no card.");
        var data = await _repository.GetArtifactContentAsync(artifact.Id, cancellationToken) ?? [];
        return new ExecutionArtifactFile(artifact.ToResponse(), data);
    }

    private static IEnumerable<ExecutionCodeRefHeadResponse> LatestByRef(IEnumerable<ExecutionCodeRefHeadResponse> heads) =>
        heads.GroupBy(h => h.Ref)
            .Select(g => g.OrderByDescending(h => h.UpdatedAt).First())
            .OrderBy(h => h.Ref, StringComparer.Ordinal);
}
