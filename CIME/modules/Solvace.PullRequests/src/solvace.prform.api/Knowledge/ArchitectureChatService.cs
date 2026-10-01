using System.Text;
using solvace.ai.application.Contract;
using solvace.knowledge.application.Contracts;
using solvace.prform.application.UserIntegrations;
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
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public int? TokensUsed { get; set; }
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
/// </summary>
public class ArchitectureChatService(IAIService ai, IArchitectureApplication architecture, IPluginConfigurationResolver resolver)
{
    public const int MaxMessages = 20;
    private const int MaxConversationChars = 30_000;
    private const int MaxIndexChars = 12_000;
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
        var index = await architecture.BuildIndexAsync(cancellationToken);

        var prompt = new StringBuilder();
        prompt.AppendLine("Você é o especialista em arquitetura da Solvace (plataforma de melhoria contínua/manufatura: legado edv-solvace .NET + ASP clássico, apps Angular, API de integrações, módulos revamp .NET, AWS). Ajude o administrador a melhorar a documentação de engenharia reversa abaixo.");
        prompt.AppendLine("Regras: responda em português, direto; não invente — o que não dá para afirmar pela documentação vira pergunta ou \"a confirmar\"; nunca inclua credenciais, senhas, tokens ou connection strings.");
        prompt.AppendLine($"Quando propuser uma nova versão da seção, devolva a seção COMPLETA (markdown, mermaid permitido) entre as linhas {Open} e {Close}, uma única vez, e explique antes em poucas linhas o que mudou. Sem proposta, não use os marcadores.");
        prompt.AppendLine();
        prompt.AppendLine($"## Projeto: {project.Name} (`{project.Key}`, {project.Kind})");
        if (project.Summary is not null) prompt.AppendLine(project.Summary);
        if (project.Keywords.Count > 0) prompt.AppendLine($"Palavras-chave: {string.Join(", ", project.Keywords)}");
        prompt.AppendLine();
        prompt.AppendLine($"## Seção atual: {section.Title} (`{section.Key}`, versão {section.Version})");
        prompt.AppendLine(section.Content);
        prompt.AppendLine();
        prompt.AppendLine("## Índice da base (contexto do parque)");
        prompt.AppendLine(index.Length <= MaxIndexChars ? index : index[..MaxIndexChars] + "\n…(índice cortado)");
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

        var (reply, suggestion) = solvace.knowledge.domain.Filtering.ArchitectureProposal.Split(result.Content);
        return new ArchitectureChatResponse
        {
            Reply = reply,
            Suggestion = suggestion,
            Provider = result.Provider,
            Model = result.Model,
            TokensUsed = result.TokensUsed
        };
    }
}
