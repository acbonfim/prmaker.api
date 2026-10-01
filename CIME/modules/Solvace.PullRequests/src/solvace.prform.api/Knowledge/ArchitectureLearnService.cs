using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using solvace.ai.application.Contract;
using solvace.azure.application.Contract;
using solvace.executionplans.application.Contracts;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Filtering;
using solvace.knowledge.domain.Responses;
using solvace.prform.application.Contracts;
using solvace.timeline.application.Contracts;

namespace solvace.prform.Knowledge;

public class LearnFromCardRequest
{
    public string CardNumber { get; set; } = string.Empty;
    /// <summary>O que a pessoa quer que seja aprendido (opcional).</summary>
    public string? Instructions { get; set; }
}

/// <summary>
/// "Aprender com um card" (0038): para os cards que não viraram conhecimento sozinhos (a analisar-bug só sugere no
/// passo 9). Junta o que foi feito — card do DevOps (campos, repro steps, root cause, comentários, histórico), o PR/RCA
/// salvo no PRMake, a Timeline, os planos de execução (achados e decisões) e as sugestões que já existem para o card —
/// e a IA propõe os aprendizados para a Base Solvace. Nada é gravado: a pessoa revisa e envia para a fila de sugestões.
/// </summary>
public partial class ArchitectureLearnService(
    IAIService ai, IArchitectureApplication architecture, ArchitectureChatService chat, IAzureService azure,
    IPullRequestApplication pullRequests, ITimelineApplication timeline, IExecutionPlanApplication plans,
    ILogger<ArchitectureLearnService> logger)
{
    public const int MaxProposals = 6;
    private const int MaxInstructions = 2_000;

    public async Task<LearnFromCardResponse> LearnAsync(LearnFromCardRequest request, CancellationToken cancellationToken)
    {
        var card = CardDigits().Match(request.CardNumber ?? string.Empty);
        if (!card.Success) throw new DomainException("Informe o número do card (ex.: 74669).");
        var number = card.Value;
        var response = new LearnFromCardResponse { CardNumber = number };
        var material = new StringBuilder();

        // DevOps
        string? title = null, module = null;
        try
        {
            var full = await azure.GetCardFullAsync(number, cancellationToken);
            if (full is null || !string.IsNullOrWhiteSpace(full.Error))
                response.Sources.Add(new LearnFromCardSource { Kind = "devops", Label = "Card no Azure DevOps", Detail = full?.Error ?? "não encontrado (integração do Azure configurada?)" });
            else
            {
                title = Text(full.Fields, "System.Title");
                module = Text(full.Fields, "Custom.Module");
                response.CardTitle = title;
                material.AppendLine($"## Card {number}: {title}");
                foreach (var key in new[] { "System.WorkItemType", "System.State", "System.AreaPath", "System.Tags" })
                    if (Text(full.Fields, key) is { } v) material.AppendLine($"{key.Split('.')[^1]}: {v}");
                foreach (var (key, value) in full.Fields.Where(f => f.Key.StartsWith("Custom.", StringComparison.Ordinal)).Take(40))
                    if (Scalar(value) is { Length: > 0 and <= 300 } v && v != "false" && v != "0") material.AppendLine($"{key[7..]}: {v}");
                var repro = ArchitectureAi.PlainText(Text(full.Fields, "Microsoft.VSTS.TCM.ReproSteps") ?? Text(full.Fields, "System.Description"));
                if (repro.Length > 0) material.AppendLine().AppendLine("### Repro steps / descrição").AppendLine(ArchitectureAi.Cut(repro, 5_000));
                foreach (var (key, value) in full.Fields.Where(f => f.Key.Contains("RootCause", StringComparison.OrdinalIgnoreCase) || f.Key.Contains("RCA", StringComparison.Ordinal)))
                    if (ArchitectureAi.PlainText(Scalar(value)) is { Length: > 3 } rca)
                        material.AppendLine().AppendLine($"### Root cause no DevOps ({key})").AppendLine(ArchitectureAi.Cut(rca, 4_000));
                var comments = full.Comments.OrderBy(c => c.CreatedDate).TakeLast(20)
                    .Select(c => $"- {c.CreatedDate:yyyy-MM-dd} {c.CreatedByName}: {ArchitectureAi.PlainText(c.Text)}").ToList();
                if (comments.Count > 0) material.AppendLine().AppendLine("### Comentários").AppendLine(ArchitectureAi.Cut(string.Join("\n", comments), 7_000));
                var states = full.History.SelectMany(h => h.Changes.Where(c => c.Field == "System.State").Select(c => $"{h.ChangedDate:yyyy-MM-dd} {c.OldValue} → {c.NewValue} ({h.ChangedByName})")).ToList();
                if (states.Count > 0) material.AppendLine().AppendLine("### Mudanças de estado").AppendLine(string.Join("\n", states.TakeLast(15)));
                response.Sources.Add(new LearnFromCardSource { Kind = "devops", Label = "Card no Azure DevOps", Ok = true, Detail = $"{comments.Count} comentário(s)" });
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "learn-from-card: card {Card} no DevOps", number);
            response.Sources.Add(new LearnFromCardSource { Kind = "devops", Label = "Card no Azure DevOps", Detail = e.Message });
        }

        // PR / RCA salvos no PRMake
        var pr = await pullRequests.GetByCardNumber(number, cancellationToken);
        if (pr is not null)
        {
            material.AppendLine().AppendLine("## Tratamento salvo no PRMake");
            if (!string.IsNullOrWhiteSpace(pr.RootCause)) material.AppendLine("### Root cause").AppendLine(ArchitectureAi.Cut(pr.RootCause, 5_000));
            if (!string.IsNullOrWhiteSpace(pr.Description)) material.AppendLine("### Descrição do PR").AppendLine(ArchitectureAi.Cut(pr.Description, 6_000));
            if (!string.IsNullOrWhiteSpace(pr.Summary)) material.AppendLine("### Resumo não técnico").AppendLine(ArchitectureAi.Cut(pr.Summary, 2_500));
        }
        response.Sources.Add(new LearnFromCardSource { Kind = "pr", Label = "PR e root cause no PRMake", Ok = pr is not null, Detail = pr is null ? "nenhum registro" : pr.BranchName });

        // Timeline
        var entries = await timeline.GetByCardNumberAsync(number, cancellationToken);
        if (entries.Count > 0)
            material.AppendLine().AppendLine("## Timeline").AppendLine(ArchitectureAi.Cut(string.Join("\n",
                entries.OrderBy(e => e.CreatedAt).TakeLast(30).Select(e => $"- {e.CreatedAt:yyyy-MM-dd} {e.UserName}: {ArchitectureAi.Cut(ArchitectureAi.PlainText(e.Description), 1_500)}")), 10_000));
        response.Sources.Add(new LearnFromCardSource { Kind = "timeline", Label = "Timeline", Ok = entries.Count > 0, Detail = $"{entries.Count} registro(s)" });

        // Planos de execução (análise e correção)
        var summaries = await plans.GetByCardAsync(number, cancellationToken);
        foreach (var summary in summaries.Take(4))
        {
            var plan = await plans.GetAsync(summary.Id, cancellationToken);
            material.AppendLine().AppendLine($"## Plano de {plan.Kind} — {plan.Title} ({plan.Status})");
            if (!string.IsNullOrWhiteSpace(plan.Summary)) material.AppendLine(ArchitectureAi.Cut(plan.Summary, 4_000));
            material.AppendLine("Etapas: " + string.Join("; ", plan.Steps.Select(s => $"{s.Title} [{s.Status}]{(string.IsNullOrWhiteSpace(s.StatusReason) ? "" : $" — {s.StatusReason}")}")));
            var logs = await plans.GetLogsAsync(plan.Id, 0, null, 500, cancellationToken);
            var key = logs.Where(l => l.Kind is "finding" or "decision" or "warning").Select(l => $"- [{l.Kind}] {l.Message}").ToList();
            if (key.Count > 0) material.AppendLine("Achados e decisões:").AppendLine(ArchitectureAi.Cut(string.Join("\n", key), 6_000));
            var answered = plan.Questions.Where(q => !string.IsNullOrWhiteSpace(q.Answer)).Select(q => $"- P: {q.Text} → R: {q.Answer}").ToList();
            if (answered.Count > 0) material.AppendLine("Perguntas respondidas:").AppendLine(ArchitectureAi.Cut(string.Join("\n", answered), 3_000));
        }
        response.Sources.Add(new LearnFromCardSource { Kind = "plan", Label = "Planos de execução (análise/correção)", Ok = summaries.Count > 0, Detail = $"{summaries.Count} plano(s)" });

        // Sugestões que já existem para o card
        response.Existing = (await architecture.GetSuggestionsAsync(null, cancellationToken)).Where(s => s.CardNumber == number).ToList();
        response.Sources.Add(new LearnFromCardSource { Kind = "suggestions", Label = "Sugestões já feitas para o card", Ok = true, Detail = $"{response.Existing.Count} sugestão(ões)" });

        if (!response.Sources.Where(s => s.Kind is "devops" or "pr" or "timeline" or "plan").Any(s => s.Ok))
            throw new KnowledgeNotFoundException($"Não encontrei nada sobre o card {number}: nem no Azure DevOps nem no PRMake (PR, Timeline, planos).");

        var status = await chat.GetStatusAsync(cancellationToken);
        if (!status.Available)
        {
            response.AiUnavailableReason = status.Reason ?? "IA não configurada.";
            return response;
        }

        try
        {
            var catalog = await architecture.BuildCatalogAsync(10_000, cancellationToken);
            var related = await architecture.SearchAsync($"{title} {module}", 8, null, null, null, cancellationToken);
            var prompt = new StringBuilder()
                .AppendLine("Você é o curador da Base Solvace (engenharia reversa e regras de negócio dos sistemas Solvace, lida pelas análises de bugs e por pessoas).")
                .AppendLine("Leia tudo o que foi feito no CARD abaixo e proponha o que vale entrar na base para que os próximos bugs parecidos sejam resolvidos mais rápido.")
                .AppendLine("Bom aprendizado: regra de negócio confirmada, armadilha/bug conhecido e como reconhecer, causa raiz e correção típica, fluxo que não estava documentado, tabela/consulta útil, configuração por cliente/ambiente. Não proponha o que é específico só deste cliente sem valor geral, nem dados pessoais ou credenciais.")
                .AppendLine("Para cada aprendizado escolha o projeto (chave do CATÁLOGO) e a seção: técnicas (público llm) — visao-geral, modulos, dados, integracoes, infra, autenticacao, jobs, regras-de-negocio, armadilhas; ou do Guia em linguagem simples (público human) — guia-regras, guia-como-funciona, guia-conexoes, guia-como-testar, guia-perguntas.")
                .AppendLine("O texto de cada aprendizado é o trecho a ACRESCENTAR na seção (markdown curto, autocontido, citando o card), não a seção inteira. Técnico: objetivo, com nomes de tabelas/arquivos quando houver. Guia: " + ArchitectureGuideTemplate.WritingRules.Replace("\n", " "))
                .AppendLine("Não repita o que as SUGESTÕES JÁ FEITAS ou os TRECHOS DA BASE já dizem.")
                .AppendLine()
                .AppendLine("Devolva sem texto fora dos blocos:")
                .AppendLine("<<<RESUMO\n(3 a 6 frases: o problema, a causa, o que foi feito e como terminou)\nRESUMO>>>")
                .AppendLine($"e de 0 a {MaxProposals} blocos:")
                .AppendLine(ArchitectureBlocks.Format("APRENDIZADO", "projeto", "secao", "publico", "titulo", "motivo"));
            var instructions = (request.Instructions ?? string.Empty).Trim();
            if (instructions.Length > 0)
                prompt.AppendLine().AppendLine("Pedido de quem está ensinando: " + (instructions.Length <= MaxInstructions ? instructions : instructions[..MaxInstructions]));
            if (response.Existing.Count > 0)
            {
                prompt.AppendLine().AppendLine("## SUGESTÕES JÁ FEITAS para este card");
                foreach (var s in response.Existing) prompt.AppendLine($"- {s.ProjectKey}/{s.SectionKey} ({s.Status}): {ArchitectureAi.Cut(s.Content, 800)}");
            }
            if (related.Count > 0)
            {
                prompt.AppendLine().AppendLine("## TRECHOS DA BASE ligados ao card");
                foreach (var h in related) prompt.AppendLine($"- {h.Title}{(h.Heading is null ? "" : " › " + h.Heading)}: {h.Snippet}");
            }
            prompt.AppendLine().AppendLine("## CATÁLOGO (chave | nome — seções)").AppendLine(catalog);
            prompt.AppendLine().AppendLine("## CARD").AppendLine(material.ToString());

            var (content, provider, model) = await ArchitectureAi.GenerateAsync(ai, prompt.ToString(), cancellationToken);
            response.Provider = provider;
            response.Model = model;
            var known = (await architecture.ListProjectsAsync(cancellationToken)).Select(p => p.Key).ToHashSet();
            ApplyProposals(response, content, known);
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException)
        {
            response.AiUnavailableReason = $"A IA não respondeu ({e.Message}).";
        }
        return response;
    }

    public static void ApplyProposals(LearnFromCardResponse response, string content, IReadOnlySet<string> knownProjects)
    {
        response.Summary = ArchitectureBlocks.First(content, "RESUMO") is { } r ? (r.Body.Length > 0 ? r.Body : string.Join(" ", r.Fields.Values)) : null;
        foreach (var block in ArchitectureBlocks.Parse(content, "APRENDIZADO").Take(MaxProposals))
        {
            if (block.Body.Length == 0) continue;
            var project = block.Field("projeto")?.Trim().ToLowerInvariant() ?? string.Empty;
            var section = block.Field("secao")?.Trim().ToLowerInvariant();
            var audience = block.Field("publico")?.Trim().ToLowerInvariant() is "human" or "guia" || (section?.StartsWith(ArchitectureSectionAudience.GuidePrefix) ?? false)
                ? ArchitectureSectionAudience.Human : ArchitectureSectionAudience.Llm;
            response.Proposals.Add(new ArchitectureSectionProposal
            {
                ProjectKey = knownProjects.Contains(project) ? project : string.Empty,
                SectionKey = string.IsNullOrWhiteSpace(section) ? null : section,
                Audience = audience,
                Title = block.Field("titulo") ?? "Aprendizado do card " + response.CardNumber,
                Content = block.Body,
                Reason = knownProjects.Contains(project) || project.Length == 0 ? block.Field("motivo") : $"{block.Field("motivo")} (projeto sugerido '{project}' não existe na base — escolha um)".Trim()
            });
        }
    }

    private static string? Text(Dictionary<string, JsonElement> fields, string key) =>
        fields.TryGetValue(key, out var value) ? Scalar(value) : null;

    private static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        JsonValueKind.Object when value.TryGetProperty("displayName", out var name) => name.GetString(),
        _ => null
    };

    [GeneratedRegex(@"\d{2,10}")] private static partial Regex CardDigits();
}
