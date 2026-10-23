using System.Text;
using System.Text.Json;
using solvace.ai.application.Contract;
using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Filtering;
using solvace.knowledge.domain.Responses;

namespace solvace.prform.Knowledge;

public class ArchitectureAskRequest
{
    public string Question { get; set; } = string.Empty;
    /// <summary>0071: true = ignora a resposta guardada (perguntas repetidas voltam do cache por alguns minutos).</summary>
    public bool Refresh { get; set; }
}

/// <summary>
/// "Pergunte à Base Solvace" (0037): a busca simples só acha a palavra exata; aqui a IA entende a pergunta
/// ("como saber se o usuário fez login com sucesso?") e leva ao trecho certo, mesmo com outras palavras.
/// 1) a IA lê a pergunta e o catálogo (projetos e seções) e devolve termos de busca — sinônimos, nomes técnicos
///    prováveis (tabelas, classes, telas) e os projetos mais prováveis;
/// 2) a busca no conteúdo roda com a pergunta + esses termos;
/// 3) a IA escolhe, entre os candidatos, os trechos que respondem e explica por quê, com uma resposta curta.
/// Sem IA configurada (plugin AI Configurations), devolve só a busca no conteúdo.
/// </summary>
public class ArchitectureAskService(IAIService ai, IArchitectureApplication architecture, ArchitectureChatService chat, IKnowledgeApplication knowledge,
    IReverseEngineeringApplication reverse, ILogger<ArchitectureAskService> logger)
{
    private const int DeepBudget = 60_000;
    private const int DeepPerSection = 14_000;
    private const int DeepSections = 10;
    public const int MaxQuestionChars = 600;
    private const int CatalogChars = 14_000;
    private const int Candidates = 14;
    private const int MaxResults = 6;
    // 0071: contexto por item (engenharia reversa): os melhores + os que eles citam
    private const int ItemLimit = 7;
    private const int RelatedLimit = 12;
    private const int ItemChars = 1_500;
    private const int SectionCandidates = 5;
    private static readonly TimeSpan AnswerTtl = TimeSpan.FromMinutes(15);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset At, ArchitectureAskResponse Response)> Answers = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ArchitectureAskResponse> AskAsync(string question, CancellationToken cancellationToken, bool refresh = false)
    {
        var q = (question ?? string.Empty).Trim();
        if (q.Length < 3) throw new solvace.knowledge.domain.Entities.DomainException("Escreva a pergunta.");
        if (q.Length > MaxQuestionChars) q = q[..MaxQuestionChars];

        // 0071: pergunta repetida volta na hora (e não gasta a chave de API); só respostas com IA entram
        var cacheKey = solvace.knowledge.application.ArchitectureSearch.Normalize(q).Trim();
        if (!refresh && Answers.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.At < AnswerTtl) return cached.Response;

        var response = new ArchitectureAskResponse { Question = q };
        var status = await chat.GetStatusAsync(cancellationToken);
        if (!status.Available)
        {
            response.AiUnavailableReason = status.Reason ?? "IA não configurada.";
            response.Results = await architecture.SearchAsync(q, MaxResults * 2, null, null, null, cancellationToken);
            return response;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        long tPlan = 0, tItems = 0, tSections = 0;
        try
        {
            // 1) termos, projetos prováveis e tipo da pergunta (IA) — enquanto isso o índice por item é carregado/validado
            var catalog = await architecture.BuildCatalogAsync(CatalogChars, cancellationToken);
            var planTask = ai.GenerateContentAsync(ArchitectureAskPrompts.Plan(q, catalog), cancellationToken);
            var warm = reverse.SearchAsync(q, null, null, null, 1, false, cancellationToken);
            await Task.WhenAll(planTask, warm);
            var first = await planTask;
            if (first is null || !string.IsNullOrWhiteSpace(first.Error) || string.IsNullOrWhiteSpace(first.Content))
                throw new InvalidOperationException(first?.Error ?? "O provedor de IA não respondeu.");
            tPlan = clock.ElapsedMilliseconds;
            var (terms, projects, aiKind) = ParsePlan(first.Content);
            var intent = ReverseAnswerContext.Intent(q, aiKind);
            response.Terms = terms;
            response.Kind = intent;
            var (boostProjects, boostSections) = Boosts(intent, projects);
            response.Provider = first.Provider;
            response.Model = first.Model;

            // 2) itens da engenharia reversa (regras, casos de uso, telas, perfis…) e os que eles citam
            var items = await reverse.GetAnswerContextAsync(q, terms, boostProjects, intent, ItemLimit, RelatedLimit, ItemChars, cancellationToken);
            tItems = clock.ElapsedMilliseconds;

            // 3) seções que não são da ER (Guia, operação da plataforma, legado) e artigos do KC — só dos projetos prováveis
            var only = projects.Concat(items.Select(i => i.ModuleKey)).Append(PlatformOperationProject).Distinct().ToList();
            var sectionHits = (await architecture.SearchInProjectsAsync(q, Candidates, terms, boostProjects, boostSections, only, cancellationToken))
                // o documento da ER já entra por item
                .Where(h => items.Count == 0 || h.Type != "section" || h.SectionKey?.StartsWith(solvace.knowledge.domain.Reverse.ReverseDocTypes.SectionPrefix, StringComparison.Ordinal) != true)
                .Take(SectionCandidates).ToList();
            if (items.Count == 0 && sectionHits.Count < MinCandidates)
                sectionHits = await WithQuestionOnlyAsync(q, Candidates, sectionHits, boostProjects, boostSections, cancellationToken);
            tSections = clock.ElapsedMilliseconds;

            // 4) as fontes do prompt: itens primeiro, depois as seções/artigos (as melhores com o bloco sob o título encontrado)
            var hits = new List<ArchitectureSearchHit>();
            var sources = new List<AskSource>();
            foreach (var item in items)
            {
                hits.Add(ItemHit(item));
                sources.Add(new AskSource($"{item.ItemId} — {item.Title} ({item.ModuleName ?? item.ModuleKey} · {item.DocTitle})", item.Body));
            }
            var blockTerms = solvace.knowledge.application.ArchitectureSearch.AnswerTerms(q, terms);
            var expanded = 0;
            foreach (var c in sectionHits)
            {
                var block = expanded < ExpandedCandidates && c.Type == "section" && c.ProjectKey is not null && c.SectionKey is not null
                    ? await BlockAsync(c, blockTerms, cancellationToken) : null;
                if (block is not null) expanded++;
                hits.Add(c);
                sources.Add(new AskSource($"{c.Title}{(c.Heading is null ? "" : " › " + c.Heading)}", block ?? c.Snippet));
            }
            if (sources.Count == 0)
            {
                response.AiUsed = true;
                response.Coverage = ArchitectureCoverage.NotFound;
                response.Answer = "Não encontrei nada na Base Solvace sobre isso. Talvez o assunto ainda não esteja documentado — vale sugerir à base (skill base-solvace) ou consultar o código.";
                return response;
            }

            // 5) a IA escreve a resposta para quem usa o sistema
            var second = await ai.GenerateContentAsync(ArchitectureAskPrompts.Answer(q, intent, sources, MaxResults), cancellationToken);
            if (second is null || !string.IsNullOrWhiteSpace(second.Error) || string.IsNullOrWhiteSpace(second.Content))
                throw new InvalidOperationException(second?.Error ?? "O provedor de IA não respondeu.");
            var answer = ArchitectureAskPrompts.ParseAnswer(second.Content) ?? throw new InvalidOperationException("a resposta veio fora do formato");

            response.AiUsed = true;
            response.Answer = answer.Text;
            response.Coverage = answer.Coverage;
            var refs = answer.Sources.Where(n => n >= 1 && n <= hits.Count).Select(n => (Ref: n, Reason: hits[n - 1].Reason)).ToList();
            response.SuggestedSection = Suggested(hits, answer.Section, refs);
            response.Results = refs.Select(r => hits[r.Ref - 1]).Take(MaxResults).ToList();
            // a IA não escolheu nada mas há fontes: mostra as melhores da busca
            if (response.Results.Count == 0) response.Results = hits.Take(3).ToList();
            logger.LogInformation("Pergunte: plano {Plan} ms, itens {Items} ms ({ItemCount}), seções {Sections} ms ({SectionCount}), total {Total} ms, {Chars} caracteres de fontes",
                tPlan, tItems - tPlan, items.Count, tSections - tItems, sectionHits.Count, clock.ElapsedMilliseconds, sources.Sum(x => x.Text.Length));
            if (answer.Text is not null)
            {
                if (Answers.Count > 200) Answers.Clear();
                Answers[cacheKey] = (DateTimeOffset.UtcNow, response);
            }
            return response;
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException or HttpRequestException)
        {
            response.AiUnavailableReason = $"A IA não respondeu ({e.Message}) — mostrando a busca no conteúdo.";
            response.Results = await architecture.SearchAsync(q, MaxResults * 2, response.Terms, null, null, cancellationToken);
            return response;
        }
    }

    /// <summary>Item da engenharia reversa como resultado da tela: abre o documento do módulo no cabeçalho do item.</summary>
    private static ArchitectureSearchHit ItemHit(ReverseAnswerItem item) => new()
    {
        Type = "section", ProjectKey = item.ModuleKey, ProjectName = item.ModuleName ?? item.ModuleKey, SectionKey = item.SectionKey, SectionTitle = item.DocTitle,
        Audience = item.Audience, Title = $"{item.ModuleName ?? item.ModuleKey} — {item.DocTitle}", Heading = $"{item.ItemId} — {item.Title}",
        Snippet = item.Snippet, Score = item.Score, Matched = [],
        Reason = item.Via is null ? item.KindLabel : $"{item.KindLabel} (citado em {item.Via})"
    };

    /// <summary>
    /// "Analisar a fundo" (0038): quando a base não cobre bem a pergunta, a IA lê as SEÇÕES INTEIRAS dos projetos
    /// prováveis (não só os trechos) e os artigos do KC, responde o que dá e propõe a seção que documenta o assunto.
    /// Se a documentação não basta, diz o que confirmar no código (vira sugestão do tipo lacuna). Nada é gravado.
    /// </summary>
    public async Task<ArchitectureDeepAnswerResponse> DeepAsync(string question, CancellationToken cancellationToken)
    {
        var q = (question ?? string.Empty).Trim();
        if (q.Length < 3) throw new solvace.knowledge.domain.Entities.DomainException("Escreva a pergunta.");
        if (q.Length > MaxQuestionChars) q = q[..MaxQuestionChars];
        var response = new ArchitectureDeepAnswerResponse { Question = q };
        var status = await chat.GetStatusAsync(cancellationToken);
        if (!status.Available)
        {
            response.AiUnavailableReason = status.Reason ?? "IA não configurada.";
            return response;
        }

        try
        {
            var catalog = await architecture.BuildCatalogAsync(CatalogChars, cancellationToken);
            var plan = new StringBuilder()
                .AppendLine("Você ajuda a encontrar documentação na Base Solvace (engenharia reversa dos sistemas Solvace e regras de negócio do Knowledge Center).")
                .AppendLine("Leia a PERGUNTA e o CATÁLOGO e devolva SÓ um JSON: {\"kind\": \"operacao | regra | tecnica | outra\", \"terms\": [até 14 termos de busca — sinônimos PT/EN, nomes técnicos prováveis], \"projects\": [chaves de até 4 projetos mais prováveis]}")
                .AppendLine().AppendLine("PERGUNTA: " + q).AppendLine().AppendLine("CATÁLOGO:").AppendLine(catalog);
            var (planContent, provider, model) = await ArchitectureAi.GenerateAsync(ai, plan.ToString(), cancellationToken);
            response.Provider = provider;
            response.Model = model;
            var (terms, projects, kind) = ParsePlan(planContent);
            var (boostProjects, boostSections) = Boosts(kind, projects);
            var candidates = await WithQuestionOnlyAsync(q, 20,
                await architecture.SearchAsync(q, 20, terms, boostProjects, boostSections, cancellationToken), boostProjects, boostSections, cancellationToken);

            // Seções a ler inteiras: as que tiveram trechos, depois as dos projetos prováveis.
            var targets = candidates.Where(h => h.Type == "section" && h.ProjectKey is not null && h.SectionKey is not null)
                .Select(h => (h.ProjectKey!, h.SectionKey!)).ToList();
            foreach (var key in projects.Concat(candidates.Where(h => h.ProjectKey is not null).Select(h => h.ProjectKey!)).Distinct().Take(4))
            {
                try
                {
                    var project = await architecture.GetProjectAsync(key, cancellationToken);
                    targets.AddRange(project.Sections.OrderBy(s => s.Order).Select(s => (project.Key, s.Key)));
                }
                catch (solvace.knowledge.domain.Entities.KnowledgeNotFoundException) { }
            }

            var material = new StringBuilder();
            var deepTerms = solvace.knowledge.application.ArchitectureSearch.Terms(q, terms);
            foreach (var (projectKey, sectionKey) in targets.Distinct().Take(DeepSections))
            {
                if (material.Length >= DeepBudget) break;
                var section = await architecture.GetSectionExcerptAsync(projectKey, sectionKey, DeepPerSection, deepTerms, cancellationToken);
                material.AppendLine($"## {projectKey}/{section.Key} — {section.Title}{(section.Audience == "human" ? " (Guia)" : "")}")
                    .AppendLine(ArchitectureAi.Cut(section.Content, Math.Min(DeepPerSection, DeepBudget - material.Length))).AppendLine();
                response.SourcesRead.Add($"{projectKey}/{section.Key}");
            }
            foreach (var number in candidates.Where(h => h.ArticleNumber is not null).Select(h => h.ArticleNumber!.Value).Distinct().Take(3))
            {
                var article = await knowledge.GetArticleAsync(number, cancellationToken);
                material.AppendLine($"## Knowledge Center ART-{article.ArticleNumber} — {article.Title}").AppendLine(ArchitectureAi.Cut(article.Content, 4_000)).AppendLine();
                response.SourcesRead.Add($"ART-{article.ArticleNumber}");
            }

            var prompt = new StringBuilder()
                .AppendLine("Você é o especialista da Base Solvace. Responda à PERGUNTA usando SÓ o MATERIAL abaixo (seções inteiras da base e artigos do Knowledge Center) — não invente.")
                .AppendLine(kind == ArchitectureQuestionKind.Operation
                    ? "A pergunta é de OPERAÇÃO: responda em passo a passo (onde fica, quem pode, o que acontece depois, o que conferir se não funcionar). Na proposta, prefira a seção técnica \"operacao\" (Configuração e operação) do módulo — ou do projeto operacao-plataforma, quando vale para todos os módulos — ou a do Guia \"guia-como-configurar\"."
                    : "")
                .AppendLine("Depois proponha a seção que deixaria esse assunto bem documentado na base: um projeto do CATÁLOGO, uma chave (minúsculas com hífen; use uma seção existente do projeto se o assunto cabe nela, ou uma nova), o título e o texto COMPLETO da seção proposta em markdown.")
                .AppendLine("Público: human (Guia, linguagem simples) quando a pergunta é de uso/regra de negócio, como as de QA, gestores e suporte; llm (técnico) quando é de implementação. Para human: " + solvace.knowledge.domain.Entities.ArchitectureGuideTemplate.WritingRules.Replace("\n", " "))
                .AppendLine("O que o material não confirma fica como \"a confirmar\" no texto, e você diz no bloco CODIGO o que olhar no código para confirmar (telas, serviços, tabelas, configurações prováveis). Se o material basta, não devolva o bloco CODIGO.")
                .AppendLine()
                .AppendLine("Devolva sem texto fora dos blocos:")
                .AppendLine(ArchitectureBlocks.Format("RESPOSTA", "cobertura").Replace("...", "answered | partial | not-found"))
                .AppendLine(ArchitectureBlocks.Format("SECAO", "projeto", "chave", "titulo", "publico", "motivo"))
                .AppendLine("<<<CODIGO\n(o que confirmar no código)\nCODIGO>>>")
                .AppendLine().AppendLine("PERGUNTA: " + q)
                .AppendLine().AppendLine("## CATÁLOGO").AppendLine(ArchitectureAi.Cut(catalog, 8_000))
                .AppendLine().AppendLine("## MATERIAL").AppendLine(material.Length == 0 ? "(nada encontrado na base)" : material.ToString());
            var (content, provider2, model2) = await ArchitectureAi.GenerateAsync(ai, prompt.ToString(), cancellationToken);
            response.Provider = provider2;
            response.Model = model2;
            var known = (await architecture.ListProjectsAsync(cancellationToken)).Select(p => p.Key).ToHashSet();
            ApplyDeep(response, content, known);
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException or HttpRequestException)
        {
            response.AiUnavailableReason = $"A IA não respondeu ({e.Message}).";
        }
        return response;
    }

    public static void ApplyDeep(ArchitectureDeepAnswerResponse response, string content, IReadOnlySet<string> knownProjects)
    {
        var answer = ArchitectureBlocks.First(content, "RESPOSTA");
        if (answer is not null)
        {
            response.Answer = answer.Body.Length > 0 ? answer.Body : null;
            response.Coverage = ArchitectureCoverage.Normalize(answer.Field("cobertura"));
        }
        var section = ArchitectureBlocks.First(content, "SECAO");
        if (section is not null && section.Body.Length > 0)
        {
            var project = section.Field("projeto")?.Trim().ToLowerInvariant() ?? string.Empty;
            var key = section.Field("chave")?.Trim().ToLowerInvariant();
            response.Proposal = new ArchitectureSectionProposal
            {
                ProjectKey = knownProjects.Contains(project) ? project : string.Empty,
                SectionKey = string.IsNullOrWhiteSpace(key) ? null : key,
                Audience = section.Field("publico")?.Trim().ToLowerInvariant() is "human" or "guia" ? "human" : "llm",
                Title = section.Field("titulo") ?? response.Question,
                Content = section.Body,
                Reason = section.Field("motivo")
            };
        }
        var code = ArchitectureBlocks.First(content, "CODIGO");
        var hints = code is null ? null : string.Join("\n", code.Fields.Select(f => $"{f.Key}: {f.Value}").Append(code.Body)).Trim();
        response.NeedsCodeAnalysis = !string.IsNullOrWhiteSpace(hints) || response.Coverage == ArchitectureCoverage.NotFound;
        response.CodeHints = string.IsNullOrWhiteSpace(hints) ? null : hints;
    }

    private const int ExpandedCandidates = 3;
    private const int MinCandidates = 3;

    /// <summary>
    /// Termos demais da IA (frases, nomes técnicos de outros módulos) aumentam o denominador da cobertura mínima e a
    /// busca pode voltar vazia mesmo com a resposta na base — com poucos candidatos, busca também só com a pergunta.
    /// </summary>
    private async Task<List<ArchitectureSearchHit>> WithQuestionOnlyAsync(string q, int limit, List<ArchitectureSearchHit> found,
        IReadOnlyCollection<string> boostProjects, IReadOnlyCollection<string>? boostSections, CancellationToken cancellationToken)
    {
        if (found.Count >= MinCandidates) return found;
        var plain = await architecture.SearchAsync(q, limit, null, boostProjects, boostSections, cancellationToken);
        string Key(ArchitectureSearchHit h) => h.Type == "article" ? $"art:{h.ArticleNumber}" : $"{h.ProjectKey}/{h.SectionKey}";
        var seen = found.Select(Key).ToHashSet();
        return found.Concat(plain.Where(h => seen.Add(Key(h)))).Take(limit).ToList();
    }
    private const int BlockChars = 2_400;
    private const int BlockSourceChars = 40_000;

    /// <summary>O bloco da seção sob o título do trecho (até o próximo título do mesmo nível ou acima).</summary>
    private async Task<string?> BlockAsync(ArchitectureSearchHit hit, IReadOnlyList<(string Term, double Weight)> terms, CancellationToken cancellationToken)
    {
        try
        {
            // seção grande: só o trecho onde os termos se concentram (antes lia a seção inteira, até milhões de caracteres)
            var section = await architecture.GetSectionExcerptAsync(hit.ProjectKey!, hit.SectionKey!, BlockSourceChars, terms.Select(t => t.Term).ToList(), cancellationToken);
            return solvace.knowledge.application.ArchitectureSearch.BestBlocks(section.Content, terms, BlockChars) ?? ExtractBlock(section.Content, hit.Heading, BlockChars);
        }
        catch (Exception e) when (e is not OperationCanceledException) { return null; }
    }

    public static string ExtractBlock(string content, string? heading, int max)
    {
        var lines = (content ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        var start = 0;
        var level = 0;
        if (!string.IsNullOrWhiteSpace(heading))
        {
            var target = solvace.knowledge.application.ArchitectureSearch.Normalize(heading).Trim();
            for (var i = 0; i < lines.Length; i++)
            {
                var m = System.Text.RegularExpressions.Regex.Match(lines[i], @"^(#{1,6})\s+(.+?)\s*#*\s*$");
                if (m.Success && solvace.knowledge.application.ArchitectureSearch.Normalize(m.Groups[2].Value).Trim() == target)
                {
                    start = i;
                    level = m.Groups[1].Value.Length;
                    break;
                }
            }
        }
        var sb = new StringBuilder();
        for (var i = start; i < lines.Length; i++)
        {
            if (i > start && level > 0)
            {
                var m = System.Text.RegularExpressions.Regex.Match(lines[i], @"^(#{1,6})\s");
                if (m.Success && m.Groups[1].Value.Length <= level) break;
            }
            sb.AppendLine(lines[i]);
            if (sb.Length >= max) break;
        }
        var text = sb.ToString().Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }

    private static (List<string> Terms, List<string> Projects, string Kind) ParsePlan(string content)
    {
        using var doc = JsonDocument.Parse(ExtractJson(content));
        var root = doc.RootElement;
        var kind = ArchitectureQuestionKind.Normalize(root.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null);
        return (Strings(root, "terms", 14, 60), Strings(root, "projects", 4, 100), kind);
    }

    /// <summary>Projeto transversal da operação da plataforma (0040).</summary>
    public const string PlatformOperationProject = "operacao-plataforma";

    /// <summary>Seções que respondem "como fazer" (0040): a técnica de operação e as do Guia.</summary>
    public static readonly IReadOnlyCollection<string> OperationSections =
        new HashSet<string> { "operacao", "guia-como-configurar", "guia-perguntas", "guia-regras", "guia-como-funciona" };

    /// <summary>Pergunta de operação: reforça o projeto da plataforma e as seções de "como fazer".</summary>
    private static (IReadOnlyCollection<string> Projects, IReadOnlyCollection<string>? Sections) Boosts(string kind, List<string> projects) =>
        kind == ArchitectureQuestionKind.Operation
            ? (projects.Append(PlatformOperationProject).Distinct().ToList(), OperationSections)
            : (projects, null);

    /// <summary>A seção principal para ler (0038): a que a IA indicou; senão a do primeiro trecho de seção escolhido.</summary>
    private static ArchitectureSuggestedSection? Suggested(List<ArchitectureSearchHit> candidates, int sectionRef, List<(int Ref, string? Reason)> refs)
    {
        var pick = sectionRef >= 1 && sectionRef <= candidates.Count && candidates[sectionRef - 1].Type == "section" ? candidates[sectionRef - 1]
            : refs.Where(r => r.Ref >= 1 && r.Ref <= candidates.Count).Select(r => candidates[r.Ref - 1]).FirstOrDefault(h => h.Type == "section");
        if (pick?.ProjectKey is null || pick.SectionKey is null) return null;
        var reason = refs.FirstOrDefault(r => r.Ref >= 1 && r.Ref <= candidates.Count && candidates[r.Ref - 1] == pick).Reason;
        return new ArchitectureSuggestedSection
        {
            ProjectKey = pick.ProjectKey, SectionKey = pick.SectionKey, Heading = pick.Heading,
            Title = $"{pick.ProjectName} › {pick.SectionTitle}", Reason = reason
        };
    }

    private static List<string> Strings(JsonElement root, string name, int max, int maxLength) =>
        root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim())
                .Where(x => x.Length > 1).Select(x => x.Length <= maxLength ? x : x[..maxLength]).Distinct().Take(max).ToList()
            : [];

    /// <summary>O modelo às vezes cerca o JSON com ```json ou texto — pega do primeiro { ao último }.</summary>
    private static string ExtractJson(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) throw new JsonException("resposta sem JSON");
        return content[start..(end + 1)];
    }
}
