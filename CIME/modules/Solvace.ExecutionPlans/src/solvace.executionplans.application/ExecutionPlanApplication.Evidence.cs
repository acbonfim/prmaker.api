using System.Globalization;
using System.Text.RegularExpressions;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// 0076: evidências do card no DevOps (arquivos anexados, imagens coladas na descrição e na discussion) importadas para
/// os arquivos do plano — o "tratamento" guarda tudo e a análise só conclui depois de abrir cada evidência legível.
/// </summary>
public partial class ExecutionPlanApplication
{
    private static readonly TimeZoneInfo EvidenceZone = CardSituation.Brasilia;

    public async Task<CardEvidenceResponse> ImportEvidenceAsync(Guid planId, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var plan = await LoadAsync(planId, cancellationToken);
        var card = plan.CardNumber;
        if (_evidence is null)
            throw new DomainException("Leitura das evidências do DevOps indisponível nesta instalação.");

        IReadOnlyList<CardEvidence> evidences;
        try
        {
            evidences = await _evidence.ListAsync(card, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException and not DomainException)
        {
            var error = $"Não deu para ler o card no DevOps: {e.Message}";
            await MutateAsync(planId, p => p.RecordEvidenceCheck(error, DateTimeOffset.UtcNow), cancellationToken, actor);
            var failed = await ListEvidenceAsync(card, cancellationToken);
            failed.Error = error;
            return failed;
        }

        var stored = (await _repository.GetCardArtifactsAsync(card, cancellationToken))
            .Where(a => a.ExternalKey is not null)
            .ToDictionary(a => a.ExternalKey!, StringComparer.OrdinalIgnoreCase);
        var nextNumber = stored.Count + 1;
        var problems = new Dictionary<string, (string Status, string Error)>();
        var importedIds = new HashSet<Guid>();

        foreach (var evidence in evidences)
        {
            var key = $"devops:{evidence.Key}";
            if (stored.ContainsKey(key))
                continue;
            CardEvidenceFile file;
            try
            {
                file = await _evidence.DownloadAsync(evidence, ExecutionArtifact.MaxFileBytes, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                problems[evidence.Key] = ("error", e.Message);
                continue;
            }
            if (file.TooLarge)
            {
                problems[evidence.Key] = ("too-large", $"maior que {ExecutionArtifact.MaxFileBytes / (1024 * 1024)} MB — abra no DevOps");
                continue;
            }
            if (file.Data.Length == 0)
            {
                problems[evidence.Key] = ("error", "arquivo vazio");
                continue;
            }

            var name = EvidenceName(nextNumber, evidence);
            ExecutionArtifact? saved = null;
            try
            {
                await MutateAsync(planId, async p =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var others = await _repository.GetArtifactsSizeAsync(p.Id, null, cancellationToken);
                    if (others + file.Data.LongLength > ExecutionArtifact.MaxPlanBytes)
                        throw new DomainException($"os arquivos do plano passariam de {ExecutionArtifact.MaxPlanBytes / (1024 * 1024)} MB — abra no DevOps");
                    var artifact = new ExecutionArtifact(p.Id, name, null, actor.Name, now,
                        await _repository.GetMaxArtifactNumberAsync(p.CardNumber, cancellationToken) + 1);
                    artifact.SetExternalKey(key);
                    artifact.SetContent(file.Data, ContentTypeFor(name, file.ContentType), null, EvidenceDescription(evidence), now, true);
                    _repository.AddArtifact(artifact);
                    p.Touch(now, fromExecutor: actor.IsExecutor);
                    saved = artifact;
                }, cancellationToken, actor);
            }
            catch (DomainException e)
            {
                problems[evidence.Key] = (e.Message.Contains("MB") ? "plan-limit" : "error", e.Message);
                continue;
            }
            stored[key] = saved!;
            nextNumber++;
            importedIds.Add(saved!.Id);
        }

        var after = await MutateAsync(planId, p => p.RecordEvidenceCheck(null, DateTimeOffset.UtcNow), cancellationToken, actor);
        if (importedIds.Count > 0)
            await NotifyAsync(after, ExecutionPlanRealTimeEvents.Actions.Artifact, null, cancellationToken);

        var response = await ListEvidenceAsync(card, cancellationToken);
        foreach (var item in response.Items.Where(i => i.ArtifactId is { } id && importedIds.Contains(id)))
            item.Status = "imported";
        // Evidências que não entraram ficam na lista com o motivo (a análise diz que não conseguiu ler).
        foreach (var e in evidences.Where(e => problems.ContainsKey(e.Key)))
            response.Items.Add(new CardEvidenceItem
            {
                Name = e.FileName, Source = e.Source, SourceDetail = e.SourceDetail, At = e.At, Size = e.Size,
                Status = problems[e.Key].Status, Error = problems[e.Key].Error,
                Readable = EvidenceFormats.IsReadable(e.FileName, null)
            });
        return response;
    }

    /// <summary>Evidências já guardadas no card (sem ir ao DevOps) e o estado da importação no plano atual.</summary>
    public async Task<CardEvidenceResponse> ListEvidenceAsync(string cardNumber, CancellationToken cancellationToken)
    {
        var card = cardNumber.Trim();
        var response = new CardEvidenceResponse { CardNumber = card };
        if (await _repository.GetCurrentPlanIdAsync(card, cancellationToken) is { } planId)
        {
            var plan = await LoadAsync(planId, cancellationToken);
            response.CheckedAt = plan.EvidenceCheckedAt;
            response.Error = plan.EvidenceError;
            response.SkipReason = plan.EvidenceSkipReason;
            // Correção: a importação foi feita na análise (o plano pai) — vale para o card.
            if (plan.EvidenceCheckedAt is null && plan.ParentPlanId is { } parentId
                && await _repository.GetPlanWithStepsAsync(parentId, cancellationToken) is { } parent)
            {
                response.CheckedAt = parent.EvidenceCheckedAt;
                response.Error ??= parent.EvidenceError;
                response.SkipReason ??= parent.EvidenceSkipReason;
            }
        }
        response.Items = (await _repository.GetCardArtifactsAsync(card, cancellationToken))
            .Where(a => a.ExternalKey is not null)
            .OrderBy(a => a.Number)
            .Select(a => new CardEvidenceItem
            {
                Number = a.Number, ArtifactId = a.Id, PlanId = a.PlanId, Name = a.Name, ContentType = a.ContentType, Size = a.Size,
                Source = SourceOf(a.Name), SourceDetail = a.Description, At = a.CreatedAt, Status = "stored",
                Readable = EvidenceFormats.IsReadable(a.Name, a.ContentType), ReadAt = a.ReadAt
            })
            .ToList();
        return response;
    }

    public async Task<CardEvidenceResponse> SkipEvidenceAsync(Guid planId, string reason, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var plan = await MutateAsync(planId, p => p.SkipEvidence(reason, actor.Name, DateTimeOffset.UtcNow), cancellationToken, actor);
        await WriteTimelineAsync(plan, $"⚠️ **Análise segue sem as evidências do DevOps** — {plan.EvidenceSkipReason}", actor, cancellationToken);
        return await ListEvidenceAsync(plan.CardNumber, cancellationToken);
    }

    /// <summary>
    /// Portão da análise: sem importação feita (nem motivo do usuário para seguir sem) ou com evidência legível ainda não
    /// aberta pela skill → texto da recusa; null = pode concluir.
    /// </summary>
    public async Task<string?> CheckEvidenceAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await LoadAsync(planId, cancellationToken);
        var evidence = await ListEvidenceAsync(plan.CardNumber, cancellationToken);
        if (evidence.SkipReason is not null)
            return null;
        if (evidence.CheckedAt is null)
            return "0076: as evidências do card no DevOps (anexos, imagens da descrição e da discussion) ainda não foram importadas" +
                   (evidence.Error is null ? "" : $" — último erro: {evidence.Error}") +
                   $". Rode `prmake_evidence(card: \"{plan.CardNumber}\")` (script: `prmake-plan.sh evidencias {plan.CardNumber}`) e abra cada uma. " +
                   "Sem acesso ao DevOps, pergunte ao usuário e, com o ok dele, siga sem: `prmake_evidence(card, skip: \"motivo\")`.";
        var unread = evidence.Items.Where(i => i.Readable && i.Number is not null && i.ReadAt is null).ToList();
        if (unread.Count == 0)
            return null;
        return $"0076: a análise ainda não abriu {unread.Count} evidência(s) do card: " +
               string.Join(", ", unread.Take(10).Select(i => $"#{i.Number} {i.Name}")) + (unread.Count > 10 ? $" (e mais {unread.Count - 10})" : "") +
               ". Abra cada uma com `prmake_attachment(card, \"#n\")` (imagem volta como imagem; PDF/Office: `prmake-plan.sh attachment` + Read " +
               "ou `evidence-text.py`) e diga na análise o que cada uma mostrou.";
    }

    private static string EvidenceName(int number, CardEvidence e)
    {
        var origin = e.Source switch
        {
            "campo" => Slug(e.SourceDetail) is "repro-steps" or "descricao" or "" ? "descricao" : Slug(e.SourceDetail),
            "discussion" => "discussion",
            _ => "anexo"
        };
        var file = e.FileName.Length > 80 ? e.FileName[^80..] : e.FileName;
        return $"ev{number:00}-{origin}-{file}";
    }

    private static string SourceOf(string name) =>
        name.Contains("-discussion-", StringComparison.Ordinal) ? "discussion"
        : name.Contains("-anexo-", StringComparison.Ordinal) ? "anexo"
        : "campo";

    private static string EvidenceDescription(CardEvidence e)
    {
        string When(DateTimeOffset? at) => at is { } d ? TimeZoneInfo.ConvertTime(d, EvidenceZone).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "";
        var text = e.Source switch
        {
            "campo" => $"Evidência do DevOps — colada em {e.SourceDetail ?? "um campo do card"}",
            "discussion" => $"Evidência do DevOps — discussion: {e.SourceDetail ?? "comentário"}{(e.At is null ? "" : $" em {When(e.At)}")}",
            _ => $"Evidência do DevOps — anexo do card{(e.At is null ? "" : $" (enviado em {When(e.At)})")}"
        };
        return string.IsNullOrWhiteSpace(e.Comment) ? text : $"{text}: {e.Comment.Trim()}";
    }

    private static string ContentTypeFor(string name, string declared) =>
        Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".txt" or ".log" or ".sql" or ".ini" or ".config" => "text/plain; charset=utf-8",
            ".csv" => "text/csv; charset=utf-8",
            ".json" or ".har" => "application/json; charset=utf-8",
            ".xml" => "application/xml; charset=utf-8",
            _ => string.IsNullOrWhiteSpace(declared) || declared == "application/octet-stream" ? "application/octet-stream" : declared
        };

    private static string Slug(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var plain = new string(normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Replace(plain.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    }
}
