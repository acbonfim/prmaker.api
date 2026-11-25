using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using solvace.executionplans.application;
using solvace.executionplans.application.Contracts;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Responses;
using solvace.knowledge.domain.Reverse;

namespace solvace.prform.Execution;

/// <summary>
/// 0102: a resposta da pergunta da Base Solvace feita pelo executor. O Claude manda o texto, a cobertura e os itens que usou;
/// o servidor monta o mesmo <see cref="ArchitectureAskResponse"/> do Pergunte (resultados que abrem o documento no item),
/// grava na pergunta, registra a pergunta/lacuna como o Pergunte faz e avisa a tela.
/// </summary>
[McpServerToolType]
public partial class BaseAskMcpTools(IExecutionQueueApplication queue, IReverseEngineeringApplication reverse, IKnowledgeApplication knowledge,
    IArchitectureApplication architecture, IHttpContextAccessor http, ILogger<BaseAskMcpTools> logger)
{
    public const string AnswerTool = "prmake_base_answer";
    public const string ProviderName = "Claude Code";
    private const int MaxResults = 6;
    private const int MaxAnswerChars = 20_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = AnswerTool)]
    [Description("Pergunta da Base Solvace pelo executor (0102): grava a resposta que a pessoa ve na tela. Chame UMA vez, no fim, com o ID " +
                 "da pergunta do prompt. answer = markdown para quem usa o sistema; coverage = answered|partial|not-found; kind = " +
                 "operacao|regra|tecnica|outra; refs = itens usados (modulo#RN-012, ART-21), do mais util para o menos util.")]
    public async Task<string> Answer(
        [Description("ID da pergunta (vem no prompt)")] string question,
        [Description("Resposta em markdown, em portugues")] string answer,
        [Description("answered | partial | not-found")] string coverage,
        [Description("operacao | regra | tecnica | outra")] string? kind = null,
        [Description("Fontes usadas, separadas por virgula, ate 6: itens (modulo#RN-012), artigos (ART-21) ou secoes (projeto/secao § titulo)")] string? refs = null,
        CancellationToken ct = default)
    {
        if (BaseAskClaims.RequestOf(http.HttpContext?.User) is not { } requestId)
            throw new McpException("prmake_base_answer so vale na pergunta da Base Solvace pelo executor (token da pergunta).");
        if (!Guid.TryParse(question?.Trim(), out var questionId))
            throw new McpException("question deve ser o ID da pergunta que veio no prompt.");
        var text = (answer ?? string.Empty).Trim();
        if (text.Length == 0)
            throw new McpException("answer vazio — escreva a resposta (ou diga em uma frase que a Base nao responde, com coverage not-found).");
        if (text.Length > MaxAnswerChars) text = text[..MaxAnswerChars];

        var q = await queue.GetBaseQuestionForRequestAsync(requestId, ct);
        if (q is null || q.Id != questionId)
            throw new McpException("Esta pergunta nao e a deste pedido — responda so a pergunta do prompt.");

        var response = new ArchitectureAskResponse
        {
            Question = q.Question,
            Answer = text,
            AiUsed = true,
            Provider = ProviderName,
            Model = http.HttpContext?.User.FindFirst(BaseAskClaims.Model)?.Value,
            Kind = ArchitectureQuestionKind.Normalize(kind),
            Coverage = ArchitectureCoverage.Normalize(coverage),
            Results = await HitsAsync(refs, ct)
        };
        response.SuggestedSection = response.Results.FirstOrDefault(h => h.Type == "section" && h.ProjectKey is not null && h.SectionKey is not null) is { } best
            ? new ArchitectureSuggestedSection { ProjectKey = best.ProjectKey!, SectionKey = best.SectionKey!, Heading = best.Heading, Title = $"{best.ProjectName} › {best.SectionTitle}" }
            : null;

        try
        {
            await queue.SaveBaseAnswerAsync(requestId, q.Id, JsonSerializer.Serialize(response, Json), ct);
        }
        catch (Exception e) when (e is ExecutionForbiddenException or solvace.executionplans.domain.Entities.DomainException)
        {
            throw new McpException(e.Message, e);
        }
        await RecordAsync(response, q.OwnerName, ct);
        return $"Resposta gravada ({response.Coverage}, {response.Results.Count} fonte(s)). A pessoa ja ve na tela — encerre.";
    }

    /// <summary>Os itens citados como resultados da tela (abrem o documento do módulo no item; artigo do KC abre o artigo).</summary>
    private async Task<List<ArchitectureSearchHit>> HitsAsync(string? refs, CancellationToken ct)
    {
        var hits = new List<ArchitectureSearchHit>();
        var list = (refs ?? string.Empty).Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxResults * 2).ToList();
        var items = new List<string>();
        var projects = new Dictionary<string, ArchitectureProjectResponse?>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in list)
        {
            if (SectionRef().Match(raw) is { Success: true } sec)
            {
                if (await SectionHitAsync(sec, projects, ct) is { } hit) hits.Add(hit);
                continue;
            }
            if (Article().Match(raw) is { Success: true } art)
            {
                try
                {
                    var a = await knowledge.GetArticleAsync(int.Parse(art.Groups[1].Value), ct);
                    hits.Add(new ArchitectureSearchHit
                    {
                        Type = "article", ArticleNumber = a.ArticleNumber, Title = a.Title, Snippet = Short(a.Content, 240), Reason = "Knowledge Center"
                    });
                }
                catch (Exception e) when (e is KnowledgeNotFoundException or DomainException) { /* referência errada: ignora */ }
                continue;
            }
            if (ReverseItemKinds.ParseRef(raw) is not null) items.Add(raw);
        }
        if (items.Count > 0)
        {
            try
            {
                var found = await reverse.GetItemsAsync(items, null, ct);
                // na ordem em que o Claude citou
                foreach (var item in items.Select(r => found.FirstOrDefault(f => string.Equals(f.Ref, r, StringComparison.OrdinalIgnoreCase)
                                                                             || r.EndsWith("#" + f.ItemId, StringComparison.OrdinalIgnoreCase)
                                                                             || string.Equals(r, f.ItemId, StringComparison.OrdinalIgnoreCase)))
                             .OfType<ReverseItemResponse>().Concat(found).Distinct())
                    hits.Add(ItemHit(item));
            }
            catch (Exception e) when (e is solvace.knowledge.application.ReverseForbiddenException or DomainException or KnowledgeNotFoundException)
            {
                logger.LogInformation(e, "Pergunte pelo executor: itens citados não resolvidos ({Refs})", string.Join(", ", items));
            }
        }
        return hits.Take(MaxResults).ToList();
    }

    /// <summary>Seção citada (<c>projeto/secao</c>, opcional <c>§ título</c>): abre a seção no trecho. Só títulos — nada do conteúdo.</summary>
    private async Task<ArchitectureSearchHit?> SectionHitAsync(Match m, Dictionary<string, ArchitectureProjectResponse?> projects, CancellationToken ct)
    {
        var key = m.Groups["project"].Value;
        if (!projects.TryGetValue(key, out var project))
        {
            try { project = await architecture.GetProjectAsync(key, ct); }
            catch (Exception e) when (e is KnowledgeNotFoundException or DomainException) { project = null; }
            projects[key] = project;
        }
        // o espelho mostra "020-modulos"; a chave da seção é "modulos"
        var sectionKey = Regex.Replace(m.Groups["section"].Value, @"^\d{3}-", "");
        var section = project?.Sections.FirstOrDefault(s => string.Equals(s.Key, sectionKey, StringComparison.OrdinalIgnoreCase));
        if (project is null || section is null) return null;
        var name = project.DisplayName ?? project.Name;
        return new ArchitectureSearchHit
        {
            Type = "section", ProjectKey = project.Key, ProjectName = name, SectionKey = section.Key, SectionTitle = section.Title,
            Audience = section.Audience, Title = $"{name} — {section.Title}",
            Heading = m.Groups["heading"].Success ? m.Groups["heading"].Value.Trim() : null, Snippet = string.Empty, Matched = [], Reason = "Seção da Base Solvace"
        };
    }

    private static ArchitectureSearchHit ItemHit(ReverseItemResponse item)
    {
        var doc = ReverseDocTypes.ByKey.TryGetValue(item.DocType, out var d) ? d : null;
        var docTitle = doc?.Title ?? item.DocType;
        return new ArchitectureSearchHit
        {
            Type = "section", ProjectKey = item.ModuleKey, ProjectName = item.ModuleName ?? item.ModuleKey, SectionKey = doc?.SectionKey,
            SectionTitle = docTitle, Audience = doc?.Audience ?? "llm", Title = $"{item.ModuleName ?? item.ModuleKey} — {docTitle}",
            Heading = $"{item.ItemId} — {item.Title}", Snippet = item.Snippet.Length > 0 ? item.Snippet : Short(item.Body, 240),
            Score = item.Score, Matched = [], Reason = item.KindLabel
        };
    }

    /// <summary>Como o Pergunte: a pergunta entra nas "Perguntas feitas" e, sem resposta num módulo, vira pergunta prática (0054).</summary>
    private async Task RecordAsync(ArchitectureAskResponse response, string actor, CancellationToken ct)
    {
        try
        {
            await architecture.RecordQuestionAsync(response.Question, response.Kind, response.Coverage, response.SuggestedSection?.ProjectKey,
                response.SuggestedSection?.SectionKey, actor, ct);
            if (response.Coverage is ArchitectureCoverage.NotFound or ArchitectureCoverage.Partial && response.SuggestedSection?.ProjectKey is { } project)
                await reverse.RecordQuestionGapAsync(project, response.Question, actor, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Pergunte pelo executor: não registrei a pergunta");
        }
    }

    private static string Short(string value, int max) => value.Length <= max ? value : value[..max].TrimEnd() + "…";

    [GeneratedRegex(@"^ART-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Article();

    [GeneratedRegex(@"^(?<project>[a-z0-9][a-z0-9._-]*)/(?<section>[a-z0-9][a-z0-9._-]*)(?:\s*§\s*(?<heading>.+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex SectionRef();
}
