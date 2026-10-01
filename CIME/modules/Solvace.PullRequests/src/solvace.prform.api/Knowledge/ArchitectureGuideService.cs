using System.Text;
using solvace.ai.application.Contract;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Filtering;
using solvace.knowledge.domain.Responses;

namespace solvace.prform.Knowledge;

public class ArchitectureGuideRequest
{
    /// <summary>O que o admin quer destacar/corrigir (opcional).</summary>
    public string? Instructions { get; set; }
}

/// <summary>
/// "Gerar guia com a IA" (0038): a partir das seções técnicas do projeto, das relações e dos artigos do Knowledge
/// Center ligados, a IA escreve o Guia (seções <c>guia-*</c> em linguagem simples) e propõe nome amigável, frase e
/// área de negócio. Nada é gravado: o admin revisa e aplica pela tela.
/// </summary>
public class ArchitectureGuideService(IAIService ai, IArchitectureApplication architecture, IKnowledgeApplication knowledge, ArchitectureChatService chat)
{
    private const int TechnicalBudget = 45_000;
    private const int PerSection = 12_000;
    private const int PerArticle = 3_500;
    private const int MaxInstructions = 2_000;
    private const int MaxWordsPerSection = 280;

    public async Task<ArchitectureGuideResponse> GenerateAsync(string projectKey, ArchitectureGuideRequest request, CancellationToken cancellationToken)
    {
        var status = await chat.GetStatusAsync(cancellationToken);
        if (!status.Available) throw new ArchitectureAiUnavailableException(status.Reason ?? "IA não configurada.");

        var project = await architecture.GetProjectAsync(projectKey, cancellationToken);
        var names = (await architecture.ListProjectsAsync(cancellationToken)).ToDictionary(p => p.Key, p => p.DisplayName ?? p.Name);
        var template = ArchitectureGuideTemplate.For(project.Key);

        var prompt = new StringBuilder()
            .AppendLine("Você escreve o Guia da Base Solvace: a documentação de um sistema da Solvace (plataforma de melhoria contínua para manufatura) para pessoas que não programam.")
            .AppendLine(ArchitectureGuideTemplate.WritingRules)
            .AppendLine("Use SÓ o material abaixo (documentação técnica, relações e artigos do Knowledge Center). Traduza o técnico para o que o usuário vê e faz.")
            // O provedor tem teto de saída (ex.: 8000 tokens no Claude): 6 seções longas não cabem e a resposta é cortada.
            .AppendLine($"Seja conciso: no máximo {MaxWordsPerSection} palavras por seção, só o essencial; sem tabelas longas.")
            .AppendLine()
            .AppendLine("Devolva, nesta ordem e sem texto fora dos blocos:")
            .AppendLine("1) um bloco de dados do projeto:")
            .AppendLine(ArchitectureBlocks.Format("PROJETO", "nome", "frase", "area"))
            .AppendLine("   (nome: como o usuário chama o sistema, até 60 caracteres, ex.: \"Plano de Ação\"; frase: uma frase simples do que ele faz, até 200; area: a área de negócio que agrupa os sistemas do mesmo módulo, ex.: \"Plano de Ação\", \"Usuários e acesso\", \"Checklists\", \"Infraestrutura\"; sem corpo)")
            .AppendLine("2) um bloco por seção do Guia (pule a seção se não houver material — não invente):")
            .AppendLine(ArchitectureBlocks.Format("GUIA", "chave", "titulo"))
            .AppendLine("3) opcional, observações para o admin (o que ficou \"a confirmar\", o que falta na documentação):")
            .AppendLine("<<<NOTAS\n(texto)\nNOTAS>>>")
            .AppendLine()
            .AppendLine("Seções do Guia (chave — título: o que escrever):");
        foreach (var item in template) prompt.AppendLine($"- {item.Key} — {item.Title}: {item.Purpose}");

        var instructions = (request.Instructions ?? string.Empty).Trim();
        if (instructions.Length > 0)
            prompt.AppendLine().AppendLine("Pedido do administrador: " + (instructions.Length <= MaxInstructions ? instructions : instructions[..MaxInstructions]));

        prompt.AppendLine().AppendLine($"## Sistema: {project.Name} (chave {project.Key}, tipo {project.Kind})");
        if (project.Summary is not null) prompt.AppendLine(project.Summary);
        if (project.Keywords.Count > 0) prompt.AppendLine($"Palavras-chave: {string.Join(", ", project.Keywords)}");
        prompt.AppendLine();
        ArchitectureAi.AppendRelations(prompt, project, names);

        var used = 0;
        foreach (var summary in project.Sections.OrderBy(s => s.Audience == ArchitectureSectionAudience.Human).ThenBy(s => s.Order))
        {
            if (used >= TechnicalBudget) break;
            var section = await architecture.GetSectionAsync(project.Key, summary.Key, cancellationToken);
            var text = ArchitectureAi.Cut(section.Content, Math.Min(PerSection, TechnicalBudget - used));
            used += text.Length;
            var label = section.Audience == ArchitectureSectionAudience.Human ? "Guia atual (melhore, não descarte o que está certo)" : "Documentação técnica";
            prompt.AppendLine().AppendLine($"## {label}: {section.Title} ({section.Key})").AppendLine(text);
        }

        var hits = await architecture.SearchAsync($"{project.DisplayName ?? project.Name} {string.Join(" ", project.Keywords.Take(6))}", 12, null,
            [project.Key], null, cancellationToken);
        foreach (var number in hits.Where(h => h.ArticleNumber is not null).Select(h => h.ArticleNumber!.Value).Distinct().Take(3))
        {
            var article = await knowledge.GetArticleAsync(number, cancellationToken);
            prompt.AppendLine().AppendLine($"## Knowledge Center ART-{article.ArticleNumber}: {article.Title}").AppendLine(ArchitectureAi.Cut(article.Content, PerArticle));
        }

        var (content, provider, model) = await ArchitectureAi.GenerateAsync(ai, prompt.ToString(), cancellationToken);
        return Parse(content, project.Key, provider, model);
    }

    public static ArchitectureGuideResponse Parse(string content, string projectKey, string? provider, string? model)
    {
        var response = new ArchitectureGuideResponse { Provider = provider, Model = model };
        var meta = ArchitectureBlocks.First(content, "PROJETO");
        if (meta is not null)
        {
            response.DisplayName = Limit(meta.Field("nome"), ArchitectureProject.MaxDisplayNameLength);
            response.Tagline = Limit(meta.Field("frase"), ArchitectureProject.MaxTaglineLength);
            response.BusinessArea = Limit(meta.Field("area"), ArchitectureProject.MaxBusinessAreaLength);
        }
        var template = ArchitectureGuideTemplate.For(projectKey);
        foreach (var block in ArchitectureBlocks.Parse(content, "GUIA"))
        {
            if (block.Body.Length == 0) continue;
            var key = block.Field("chave")?.Trim().ToLowerInvariant();
            var item = template.FirstOrDefault(t => t.Key == key);
            if (item is null || response.Sections.Any(s => s.Key == item.Key)) continue;
            response.Sections.Add(new ArchitectureGuideSection
            {
                Key = item.Key, Title = Limit(block.Field("titulo"), ArchitectureSection.MaxTitleLength) ?? item.Title, Order = item.Order, Content = block.Body
            });
        }
        response.Sections = response.Sections.OrderBy(s => s.Order).ToList();
        var notes = ArchitectureBlocks.First(content, "NOTAS");
        response.Notes = notes is null ? null : string.Join("\n", notes.Fields.Select(f => $"{f.Key}: {f.Value}").Append(notes.Body)).Trim();
        if (response.Sections.Count == 0 && response.Notes is null)
            response.Notes = "A IA não devolveu o guia no formato esperado — tente de novo ou com instruções.";
        return response;
    }

    private static string? Limit(string? value, int max)
    {
        var v = value?.Trim().Trim('"');
        return string.IsNullOrEmpty(v) ? null : v.Length <= max ? v : v[..max];
    }
}
