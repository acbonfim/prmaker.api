using System.Text;
using solvace.ai.application.Contract;
using solvace.knowledge.application.Contracts;
using solvace.prform.application.UserIntegrations;
using solvace.knowledge.domain.Filtering;
using solvace.prform.domain.Extensions;

namespace solvace.prform.Knowledge;

public class ArchitectureChatMessage
{
    /// <summary>user | assistant.</summary>
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}

public class ArchitectureChatRequest
{
    public List<ArchitectureChatMessage> Messages { get; set; } = [];
}

public class ArchitectureChatResponse
{
    /// <summary>Resposta do especialista (sem o bloco da seção proposta).</summary>
    public string Reply { get; set; } = string.Empty;
    /// <summary>Seção inteira proposta (markdown) — o admin aplica ou descarta; null = só conversa.</summary>
    public string? Suggestion { get; set; }
    /// <summary>Seção grande: só os blocos alterados (aplicados com <c>chat/apply</c>); null = seção inteira ou só conversa.</summary>
    public List<ArchitectureChatBlock>? Blocks { get; set; }
    /// <summary>Versão da seção em que a proposta foi feita (a troca dos blocos confere).</summary>
    public int Version { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public int? TokensUsed { get; set; }
}

/// <summary>Bloco de uma seção grande proposto pela IA: o original (conferido na posição ao aplicar) e o novo texto.</summary>
public class ArchitectureChatBlock
{
    public int Index { get; set; }
    public int Start { get; set; }
    public string? Heading { get; set; }
    public string Original { get; set; } = string.Empty;
    public string Proposed { get; set; } = string.Empty;
}

public class ArchitectureChatApplyRequest
{
    public int Version { get; set; }
    public string? Note { get; set; }
    public List<ArchitectureChatBlock> Blocks { get; set; } = [];
}

public class ArchitectureChatStatus
{
    public bool Available { get; set; }
    public string? Provider { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// "Sugerir melhoria" de uma seção da engenharia reversa (feature 0033): conversa com o especialista Solvace pelo
/// provedor de IA configurado (plugin "AI Configurations" → "{Provider} Plugin"). Os provedores só aceitam prompt
/// único, então cada turno manda a conversa inteira (limitada). A proposta vem entre marcadores próprios — a seção
/// pode ter blocos ```mermaid``` e um bloco de código dentro de outro quebraria a extração. Nada é gravado aqui: o
/// admin aplica a sugestão pela tela (vira uma versão nova, fonte "ai").
/// Seção grande (documentos da engenharia reversa de milhões de caracteres): a IA recebe o sumário e só os blocos do
/// assunto da conversa e devolve só os blocos que mudou — a seção inteira estourava o limite do prompt (1 milhão de tokens).
/// </summary>
public class ArchitectureChatService(IAIService ai, IArchitectureApplication architecture, IPluginConfigurationResolver resolver)
{
    public const int MaxMessages = 20;
    private const int MaxConversationChars = 30_000;
    private const int MaxIndexChars = 12_000;
    private const int MaxTechnicalChars = 30_000;
    /// <summary>Até aqui a seção vai inteira (e volta inteira — a resposta da IA tem limite de tokens de saída).</summary>
    public const int WholeSectionChars = 24_000;
    private const int MaxBlocksChars = 48_000;
    private const int MaxBlocks = 12;
    private const int MaxOutlineChars = 6_000;
    private const string BlockTag = "BLOCO";
    private const string AIConfigurationsPlugin = "AI Configurations";
    private const string Open = solvace.knowledge.domain.Filtering.ArchitectureProposal.Open;
    private const string Close = solvace.knowledge.domain.Filtering.ArchitectureProposal.Close;

    public async Task<ArchitectureChatStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = await resolver.GetEffectiveConfigurationAsync(AIConfigurationsPlugin, cancellationToken);
            var provider = config.GetConfigurationValue("Provider")?.Trim();
            if (string.IsNullOrWhiteSpace(provider))
                return new ArchitectureChatStatus { Reason = $"Provider não configurado no plugin {AIConfigurationsPlugin}." };
            var providerConfig = await resolver.GetEffectiveConfigurationAsync($"{provider} Plugin", cancellationToken);
            return string.IsNullOrWhiteSpace(providerConfig.GetConfigurationValue("ApiKey"))
                ? new ArchitectureChatStatus { Provider = provider, Reason = $"ApiKey não configurada no plugin {provider} Plugin." }
                : new ArchitectureChatStatus { Available = true, Provider = provider };
        }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException)
        {
            return new ArchitectureChatStatus { Reason = e.Message };
        }
    }

    public async Task<ArchitectureChatResponse> ChatAsync(string projectKey, string sectionKey, ArchitectureChatRequest request, CancellationToken cancellationToken)
    {
        var messages = request.Messages.Where(m => !string.IsNullOrWhiteSpace(m.Content)).TakeLast(MaxMessages).ToList();
        if (messages.Count == 0 || !string.Equals(messages[^1].Role, "user", StringComparison.OrdinalIgnoreCase))
            throw new solvace.knowledge.domain.Entities.DomainException("Envie a mensagem do usuário.");

        var project = await architecture.GetProjectAsync(projectKey, cancellationToken);
        var section = await architecture.GetSectionAsync(projectKey, sectionKey, cancellationToken);

        var guide = section.Audience == solvace.knowledge.domain.Entities.ArchitectureSectionAudience.Human;
        var prompt = new StringBuilder();
        if (guide)
        {
            // 0038: seção do Guia — o texto é para QA, gestores e suporte, não para a LLM.
            prompt.AppendLine("Você é o redator do Guia da Base Solvace: documentação de um sistema da Solvace (plataforma de melhoria contínua para manufatura) para pessoas que não programam. Ajude o administrador a melhorar a seção do Guia abaixo, usando a documentação técnica do mesmo sistema como fonte.");
            prompt.AppendLine(solvace.knowledge.domain.Entities.ArchitectureGuideTemplate.WritingRules);
        }
        else
            prompt.AppendLine("Você é o especialista em arquitetura da Solvace (plataforma de melhoria contínua/manufatura: legado edv-solvace .NET + ASP clássico, apps Angular, API de integrações, módulos revamp .NET, AWS). Ajude o administrador a melhorar a documentação de engenharia reversa abaixo.");
        prompt.AppendLine("Regras: responda em português, direto; não invente — o que não dá para afirmar pela documentação vira pergunta ou \"a confirmar\"; nunca inclua credenciais, senhas, tokens ou connection strings.");
        // Seção grande: só o sumário e os blocos do assunto da conversa (a seção inteira não cabe no prompt nem na resposta).
        var blocks = section.Content.Length > WholeSectionChars ? PickBlocks(section.Content, messages) : null;
        if (blocks is null)
            prompt.AppendLine($"Quando propuser uma nova versão da seção, devolva a seção COMPLETA (markdown, mermaid permitido) entre as linhas {Open} e {Close}, uma única vez, e explique antes em poucas linhas o que mudou. Sem proposta, não use os marcadores.");
        else
        {
            prompt.AppendLine($"A seção é grande ({section.Content.Length:N0} caracteres): você recebe o sumário dela e só os blocos que tratam do assunto da conversa, numerados.");
            prompt.AppendLine("Quando propuser mudanças, devolva SÓ os blocos que mudam, cada um COMPLETO (do cabeçalho até o fim do bloco, markdown, mermaid permitido) no formato abaixo, e explique antes em poucas linhas o que mudou. Para acrescentar um assunto novo, inclua-o no fim do bloco mais próximo. Não altere blocos que você não recebeu. Sem proposta, não use os marcadores.");
            prompt.AppendLine(solvace.knowledge.domain.Filtering.ArchitectureBlocks.Format(BlockTag, "bloco").Replace("bloco: ...", "bloco: (número do bloco)"));
        }
        prompt.AppendLine();
        prompt.AppendLine($"## Projeto: {project.Name} (`{project.Key}`, {project.Kind})");
        if (project.Summary is not null) prompt.AppendLine(project.Summary);
        if (project.Keywords.Count > 0) prompt.AppendLine($"Palavras-chave: {string.Join(", ", project.Keywords)}");
        prompt.AppendLine();
        prompt.AppendLine($"## Seção atual: {section.Title} (`{section.Key}`, versão {section.Version})");
        if (blocks is null) prompt.AppendLine(section.Content);
        else
        {
            prompt.AppendLine("### Sumário da seção");
            prompt.AppendLine(ArchitectureAi.Cut(string.Join("\n", blocks.All.Where(b => b.Heading is not null).Select(b => $"- {b.Heading}")), MaxOutlineChars));
            prompt.AppendLine();
            prompt.AppendLine("### Blocos do assunto");
            foreach (var b in blocks.Chosen)
                prompt.AppendLine($"----- BLOCO {b.Index}{(b.Heading is null ? "" : $" ({b.Heading})")} -----")
                    .AppendLine(SectionBlocks.Text(section.Content, b).TrimEnd())
                    .AppendLine($"----- fim do BLOCO {b.Index} -----");
        }
        prompt.AppendLine();
        if (guide)
        {
            var used = 0;
            foreach (var technical in project.Sections.Where(x => x.Audience != solvace.knowledge.domain.Entities.ArchitectureSectionAudience.Human).OrderBy(x => x.Order))
            {
                if (used >= MaxTechnicalChars) break;
                var limit = Math.Min(10_000, MaxTechnicalChars - used);
                var source = await architecture.GetSectionExcerptAsync(projectKey, technical.Key, limit, null, cancellationToken);
                var excerpt = ArchitectureAi.Cut(source.Content, limit);
                used += excerpt.Length;
                prompt.AppendLine($"## Documentação técnica: {source.Title} ({source.Key})").AppendLine(excerpt).AppendLine();
            }
        }
        else
        {
            var index = await architecture.BuildIndexAsync(cancellationToken);
            prompt.AppendLine("## Índice da base (contexto do parque)");
            prompt.AppendLine(index.Length <= MaxIndexChars ? index : index[..MaxIndexChars] + "\n…(índice cortado)");
        }
        prompt.AppendLine();
        prompt.AppendLine("## Conversa");
        var conversation = new StringBuilder();
        foreach (var m in messages)
            conversation.AppendLine($"{(string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "Especialista" : "Administrador")}: {m.Content.Trim()}").AppendLine();
        var text = conversation.ToString();
        prompt.Append(text.Length <= MaxConversationChars ? text : "…(início da conversa omitido)\n" + text[^MaxConversationChars..]);
        prompt.AppendLine("Especialista:");

        var result = await ai.GenerateContentAsync(prompt.ToString(), cancellationToken);
        if (result is null || !string.IsNullOrWhiteSpace(result.Error) || string.IsNullOrWhiteSpace(result.Content))
            throw new InvalidOperationException(result?.Error ?? "O provedor de IA não respondeu.");

        if (blocks is null)
        {
            var (reply, suggestion) = solvace.knowledge.domain.Filtering.ArchitectureProposal.Split(result.Content);
            return new ArchitectureChatResponse
            {
                Reply = reply,
                Suggestion = suggestion,
                Version = section.Version,
                Provider = result.Provider,
                Model = result.Model,
                TokensUsed = result.TokensUsed
            };
        }

        var chosen = blocks.Chosen.ToDictionary(b => b.Index);
        var proposed = solvace.knowledge.domain.Filtering.ArchitectureBlocks.Parse(result.Content, BlockTag)
            .Select(p => (Ok: int.TryParse(p.Field("bloco")?.Trim().TrimStart('#'), out var n), Number: n, p.Body))
            .Where(p => p.Ok && chosen.ContainsKey(p.Number) && p.Body.Length > 0)
            .DistinctBy(p => p.Number)
            .Select(p =>
            {
                var b = chosen[p.Number];
                return new ArchitectureChatBlock { Index = b.Index, Start = b.Start, Heading = b.Heading, Original = SectionBlocks.Text(section.Content, b), Proposed = p.Body };
            })
            .Where(p => p.Proposed.Trim() != p.Original.Trim())
            .OrderBy(p => p.Start)
            .ToList();
        var said = solvace.knowledge.domain.Filtering.ArchitectureBlocks.Strip(result.Content, BlockTag);
        var read = string.Join("; ", blocks.Chosen.Select(b => b.Heading ?? $"bloco {b.Index}"));
        return new ArchitectureChatResponse
        {
            Reply = (said.Length == 0 && proposed.Count > 0 ? "Proposta dos blocos abaixo." : said)
                    + $"\n\n_Seção grande: li o sumário e {blocks.Chosen.Count} de {blocks.All.Count} blocos ({ArchitectureAi.Cut(read, 400)})._",
            Blocks = proposed.Count > 0 ? proposed : null,
            Version = section.Version,
            Provider = result.Provider,
            Model = result.Model,
            TokensUsed = result.TokensUsed
        };
    }

    private sealed record BlockSelection(List<SectionBlock> All, List<SectionBlock> Chosen);

    /// <summary>
    /// Os blocos do assunto: termos das mensagens do administrador (IDs de itens e nomes técnicos pesam mais; a última
    /// mensagem pesa mais que as anteriores) ranqueados por BM25 entre os blocos; até <see cref="MaxBlocks"/> blocos /
    /// <see cref="MaxBlocksChars"/> caracteres, na ordem da seção. Sem nenhum termo casando, vão os primeiros blocos.
    /// </summary>
    private static BlockSelection PickBlocks(string content, List<ArchitectureChatMessage> messages)
    {
        var all = SectionBlocks.Split(content);
        var users = messages.Where(m => !string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase)).Select(m => m.Content).ToList();
        var terms = solvace.knowledge.application.ArchitectureSearch.WeightedTerms(users[^1], string.Join("\n", users.SkipLast(1).TakeLast(4)));
        var ranked = solvace.knowledge.application.ArchitectureSearch.RankBlocks(all.Select(b => (SectionBlocks.Text(content, b), b.Heading)).ToList(), terms);
        var chosen = new List<SectionBlock>();
        var used = 0;
        foreach (var b in ranked.Count > 0 ? ranked.Select(i => all[i]) : all)
        {
            if (chosen.Count >= MaxBlocks) break;
            if (chosen.Count > 0 && used + b.Length > MaxBlocksChars) continue;
            chosen.Add(b);
            used += b.Length;
        }
        return new BlockSelection(all, chosen.OrderBy(b => b.Index).ToList());
    }

    /// <summary>Aplica os blocos propostos sobre a versão atual da seção (nova versão, fonte "ai").</summary>
    public async Task<solvace.knowledge.domain.Responses.ArchitectureSectionResponse> ApplyBlocksAsync(string projectKey, string sectionKey,
        ArchitectureChatApplyRequest request, string actor, CancellationToken cancellationToken)
    {
        if (request.Blocks.Count == 0) throw new solvace.knowledge.domain.Entities.DomainException("Nenhum bloco para aplicar.");
        var section = await architecture.GetSectionAsync(projectKey, sectionKey, cancellationToken);
        if (section.Version != request.Version)
            throw new solvace.knowledge.domain.Entities.DomainException($"A seção mudou desde a proposta (versão {request.Version} → {section.Version}) — peça a proposta de novo.");
        var content = SectionBlocks.Apply(section.Content, request.Blocks.Select(b => new SectionBlockEdit(b.Start, b.Original, b.Proposed)));
        return await architecture.WriteSectionAsync(projectKey, sectionKey, new solvace.knowledge.domain.Requests.WriteArchitectureSectionRequest
        {
            Title = section.Title,
            Content = content,
            Source = "ai",
            Note = string.IsNullOrWhiteSpace(request.Note) ? "blocos aplicados com a IA" : request.Note.Trim()
        }, actor, cancellationToken);
    }
}
