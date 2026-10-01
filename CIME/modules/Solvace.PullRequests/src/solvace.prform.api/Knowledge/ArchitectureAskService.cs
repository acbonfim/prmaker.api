using System.Text;
using System.Text.Json;
using solvace.ai.application.Contract;
using solvace.knowledge.application.Contracts;
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
public class ArchitectureAskService(IAIService ai, IArchitectureApplication architecture, ArchitectureChatService chat)
{
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
            response.Results = await architecture.SearchAsync(q, MaxResults * 2, null, null, cancellationToken);
            return response;
        }

        try
        {
            // 1) termos e projetos prováveis
            var catalog = await architecture.BuildCatalogAsync(CatalogChars, cancellationToken);
            var plan = new StringBuilder()
                .AppendLine("Você ajuda a encontrar documentação na Base Solvace (engenharia reversa dos sistemas Solvace: legado edv-solvace .NET/ASP clássico, apps Angular, API de integrações, módulos revamp .NET, AWS, login/Cognito; e regras de negócio do Knowledge Center).")
                .AppendLine("Leia a PERGUNTA e o CATÁLOGO e devolva SÓ um JSON, sem texto fora dele:")
                .AppendLine("{\"terms\": [até 14 termos de busca — sinônimos em português e inglês, nomes técnicos prováveis de tabelas (ex.: TB_WCM_USER), colunas, classes, serviços, telas, eventos e endpoints, curtos], \"projects\": [chaves de até 4 projetos do catálogo mais prováveis]}")
                .AppendLine()
                .AppendLine("PERGUNTA: " + q)
                .AppendLine()
                .AppendLine("CATÁLOGO (chave | nome — seções):")
                .AppendLine(catalog);
            var first = await ai.GenerateContentAsync(plan.ToString(), cancellationToken);
            if (first is null || !string.IsNullOrWhiteSpace(first.Error) || string.IsNullOrWhiteSpace(first.Content))
                throw new InvalidOperationException(first?.Error ?? "O provedor de IA não respondeu.");
            var (terms, projects) = ParsePlan(first.Content);
            response.Terms = terms;
            response.Provider = first.Provider;
            response.Model = first.Model;

            // 2) candidatos no conteúdo
            var candidates = await architecture.SearchAsync(q, Candidates, terms, projects, cancellationToken);
            if (candidates.Count == 0)
            {
                response.AiUsed = true;
                response.Answer = "Não encontrei nada na Base Solvace sobre isso. Talvez o assunto ainda não esteja documentado — vale sugerir à base (skill base-solvace) ou consultar o código.";
                return response;
            }

            // 3) a IA escolhe e explica
            var pick = new StringBuilder()
                .AppendLine("Você é o especialista da Base Solvace. Responda à PERGUNTA usando SÓ os TRECHOS numerados abaixo (não invente).")
                .AppendLine("Devolva SÓ um JSON, sem texto fora dele:")
                .AppendLine("{\"answer\": \"resposta curta em português (2 a 4 frases), citando os trechos como [n]; se os trechos não respondem, diga isso\", \"results\": [{\"ref\": n, \"reason\": \"por que este trecho responde (1 frase)\"}]}")
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

            var (answer, refs) = ParsePick(second.Content);
            response.AiUsed = true;
            response.Answer = answer;
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
            response.Results = await architecture.SearchAsync(q, MaxResults * 2, response.Terms, null, cancellationToken);
            return response;
        }
    }

    private static (List<string> Terms, List<string> Projects) ParsePlan(string content)
    {
        using var doc = JsonDocument.Parse(ExtractJson(content));
        var root = doc.RootElement;
        return (Strings(root, "terms", 14, 60), Strings(root, "projects", 4, 100));
    }

    private static (string? Answer, List<(int Ref, string? Reason)> Refs) ParsePick(string content)
    {
        using var doc = JsonDocument.Parse(ExtractJson(content));
        var root = doc.RootElement;
        var answer = root.TryGetProperty("answer", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        var refs = new List<(int, string?)>();
        if (root.TryGetProperty("results", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("ref", out var r)) continue;
                var n = r.ValueKind == JsonValueKind.Number ? r.GetInt32() : int.TryParse(r.GetString(), out var parsed) ? parsed : 0;
                var reason = item.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String ? why.GetString() : null;
                refs.Add((n, reason));
            }
        return (answer?.Trim(), refs);
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
