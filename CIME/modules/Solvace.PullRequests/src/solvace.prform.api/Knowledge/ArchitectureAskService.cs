using System.Text;
using System.Text.Json;
using solvace.ai.application.Contract;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Filtering;
using solvace.knowledge.domain.Responses;

namespace solvace.prform.Knowledge;

public class ArchitectureAskRequest
{
    public string Question { get; set; } = string.Empty;
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
public class ArchitectureAskService(IAIService ai, IArchitectureApplication architecture, ArchitectureChatService chat, IKnowledgeApplication knowledge)
{
    private const int DeepBudget = 60_000;
    private const int DeepPerSection = 14_000;
    private const int DeepSections = 10;
    public const int MaxQuestionChars = 600;
    private const int CatalogChars = 14_000;
    private const int Candidates = 14;
    private const int MaxResults = 5;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ArchitectureAskResponse> AskAsync(string question, CancellationToken cancellationToken)
    {
        var q = (question ?? string.Empty).Trim();
        if (q.Length < 3) throw new solvace.knowledge.domain.Entities.DomainException("Escreva a pergunta.");
        if (q.Length > MaxQuestionChars) q = q[..MaxQuestionChars];
        var response = new ArchitectureAskResponse { Question = q };

        var status = await chat.GetStatusAsync(cancellationToken);
        if (!status.Available)
        {
            response.AiUnavailableReason = status.Reason ?? "IA não configurada.";
            response.Results = await architecture.SearchAsync(q, MaxResults * 2, null, null, null, cancellationToken);
            return response;
        }

        try
        {
            // 1) termos e projetos prováveis
            var catalog = await architecture.BuildCatalogAsync(CatalogChars, cancellationToken);
            var plan = new StringBuilder()
                .AppendLine("Você ajuda a encontrar documentação na Base Solvace (engenharia reversa dos sistemas Solvace: legado edv-solvace .NET/ASP clássico, apps Angular, API de integrações, módulos revamp .NET, AWS, login/Cognito; e regras de negócio do Knowledge Center).")
                .AppendLine("Leia a PERGUNTA e o CATÁLOGO e devolva SÓ um JSON, sem texto fora dele:")
                .AppendLine("{\"kind\": \"operacao (como habilitar/configurar/dar acesso/cadastrar/onde fica/por que não aparece) | regra (como o produto deve se comportar) | tecnica (implementação, código, banco) | outra\", \"terms\": [até 14 termos de busca — sinônimos em português e inglês, nomes técnicos prováveis de tabelas (ex.: TB_WCM_USER), colunas, classes, serviços, telas, eventos e endpoints, curtos], \"projects\": [chaves de até 4 projetos do catálogo mais prováveis]}")
                .AppendLine()
                .AppendLine("PERGUNTA: " + q)
                .AppendLine()
                .AppendLine("CATÁLOGO (chave | nome — seções):")
                .AppendLine(catalog);
            var first = await ai.GenerateContentAsync(plan.ToString(), cancellationToken);
            if (first is null || !string.IsNullOrWhiteSpace(first.Error) || string.IsNullOrWhiteSpace(first.Content))
                throw new InvalidOperationException(first?.Error ?? "O provedor de IA não respondeu.");
            var (terms, projects, kind) = ParsePlan(first.Content);
            response.Terms = terms;
            response.Kind = kind;
            var (boostProjects, boostSections) = Boosts(kind, projects);
            response.Provider = first.Provider;
            response.Model = first.Model;

            // 2) candidatos no conteúdo
            var candidates = await architecture.SearchAsync(q, Candidates, terms, boostProjects, boostSections, cancellationToken);
            if (candidates.Count == 0)
            {
                response.AiUsed = true;
                response.Coverage = ArchitectureCoverage.NotFound;
                response.Answer = "Não encontrei nada na Base Solvace sobre isso. Talvez o assunto ainda não esteja documentado — vale sugerir à base (skill base-solvace) ou consultar o código.";
                return response;
            }

            // 3) a IA escolhe e explica
            var pick = new StringBuilder()
                .AppendLine("Você é o especialista da Base Solvace. Responda à PERGUNTA usando SÓ os TRECHOS numerados abaixo (não invente).")
                .AppendLine("Devolva SÓ um JSON, sem texto fora dele:")
                .AppendLine(kind == ArchitectureQuestionKind.Operation
                    ? "A pergunta é de OPERAÇÃO: responda em passo a passo numerado (onde fica na tela, quem pode fazer, o que acontece depois) e termine com \"Se não funcionar, confira:\" e as causas comuns que os trechos citarem."
                    : "")
                .AppendLine("{\"answer\": \"resposta em português simples, para quem não programa (2 a 4 frases; em pergunta de operação, o passo a passo), citando os trechos como [n]; se os trechos não respondem, diga isso\", \"coverage\": \"answered (os trechos respondem) | partial (respondem em parte) | not-found (não respondem)\", \"section\": n do trecho cuja seção é a melhor para ler sobre o assunto (ou 0), \"results\": [{\"ref\": n, \"reason\": \"por que este trecho responde (1 frase)\"}]}")
                .AppendLine($"Inclua em results só os trechos que ajudam de verdade, do mais útil para o menos útil, no máximo {MaxResults}.")
                .AppendLine()
                .AppendLine("PERGUNTA: " + q)
                .AppendLine()
                .AppendLine("TRECHOS:");
            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                pick.AppendLine($"[{i + 1}] {c.Title}{(c.Heading is null ? "" : " › " + c.Heading)}");
                pick.AppendLine("    " + c.Snippet);
            }
            var second = await ai.GenerateContentAsync(pick.ToString(), cancellationToken);
            if (second is null || !string.IsNullOrWhiteSpace(second.Error) || string.IsNullOrWhiteSpace(second.Content))
                throw new InvalidOperationException(second?.Error ?? "O provedor de IA não respondeu.");

            var (answer, refs, coverage, sectionRef) = ParsePick(second.Content);
            response.AiUsed = true;
            response.Answer = answer;
            response.Coverage = coverage;
            response.SuggestedSection = Suggested(candidates, sectionRef, refs);
            response.Results = refs
                .Where(r => r.Ref >= 1 && r.Ref <= candidates.Count)
                .DistinctBy(r => r.Ref)
                .Take(MaxResults)
                .Select(r => { var hit = candidates[r.Ref - 1]; hit.Reason = r.Reason; return hit; })
                .ToList();
            // A IA não escolheu nada mas há candidatos: mostra os melhores da busca (sem motivo).
            if (response.Results.Count == 0) response.Results = candidates.Take(3).ToList();
            return response;
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException or HttpRequestException)
        {
            response.AiUnavailableReason = $"A IA não respondeu ({e.Message}) — mostrando a busca no conteúdo.";
            response.Results = await architecture.SearchAsync(q, MaxResults * 2, response.Terms, null, null, cancellationToken);
            return response;
        }
    }

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
            var candidates = await architecture.SearchAsync(q, 20, terms, boostProjects, boostSections, cancellationToken);

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
            foreach (var (projectKey, sectionKey) in targets.Distinct().Take(DeepSections))
            {
                if (material.Length >= DeepBudget) break;
                var section = await architecture.GetSectionAsync(projectKey, sectionKey, cancellationToken);
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

    private static (string? Answer, List<(int Ref, string? Reason)> Refs, string Coverage, int Section) ParsePick(string content)
    {
        using var doc = JsonDocument.Parse(ExtractJson(content));
        var root = doc.RootElement;
        var answer = root.TryGetProperty("answer", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        var coverage = ArchitectureCoverage.Normalize(root.TryGetProperty("coverage", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null);
        var section = root.TryGetProperty("section", out var sec)
            ? sec.ValueKind == JsonValueKind.Number ? sec.GetInt32() : int.TryParse(sec.ValueKind == JsonValueKind.String ? sec.GetString() : null, out var sn) ? sn : 0
            : 0;
        var refs = new List<(int, string?)>();
        if (root.TryGetProperty("results", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("ref", out var r)) continue;
                var n = r.ValueKind == JsonValueKind.Number ? r.GetInt32() : int.TryParse(r.GetString(), out var parsed) ? parsed : 0;
                var reason = item.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String ? why.GetString() : null;
                refs.Add((n, reason));
            }
        return (answer?.Trim(), refs, coverage, section);
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
