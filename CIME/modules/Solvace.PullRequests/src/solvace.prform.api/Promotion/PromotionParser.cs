using System.Text;
using solvace.ai.application.Contract;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Extensions;
using solvace.prform.domain.Promotion;
using solvace.prform.Skills;
using PrDomainException = solvace.prform.domain.Entities.DomainException;

namespace solvace.prform.Promotion;

/// <summary>
/// 0074: lê a mensagem do Release Manager e devolve o que a tela de promoção preenche — nada é disparado. Os cards saem
/// por regex (sem IA); a IA do próprio usuário (a chave dele paga) interpreta só o texto livre: versão e regras de
/// destino por tipo de ticket. O que ela devolve é validado (tipos conhecidos, nomes de branch bem formados) e, se ela
/// falhar ou não estiver configurada, a leitura por regras entra no lugar, com aviso.
/// </summary>
public class PromotionParser(IAIService ai, IPluginConfigurationResolver configurationResolver, ILogger<PromotionParser> logger)
{
    public const int MaxTextLength = 20_000;
    /// <summary>Chave opcional no "AI Configurations" para trocar o prompt sem deploy; vazio = o padrão abaixo.</summary>
    public const string PromptKey = "PromotionParsePrompt";

    public async Task<PromotionParseResponse> ParseAsync(string? text, CancellationToken ct)
    {
        var raw = text?.Trim() ?? string.Empty;
        if (raw.Length == 0)
            throw new PrDomainException("Cole a mensagem do Release Manager.");
        if (raw.Length > MaxTextLength)
            throw new PrDomainException($"A mensagem passa de {MaxTextLength} caracteres.");

        var response = new PromotionParseResponse();
        var cards = PromotionMessageParser.ExtractCards(raw);
        response.Cards = cards.Select(c => new PromotionParsedCard { Card = c.Card, WorkItemType = c.WorkItemType, Title = c.Title, AssignedTo = c.AssignedTo }).ToList();
        if (cards.Count == 0)
            response.Warnings.Add("Não encontrei cards na mensagem (esperava linhas como \"77317  Bug  Título\").");
        if (cards.Count > PromotionBatch.MaxCards)
            response.Warnings.Add($"A mensagem tem {cards.Count} cards e o lote aceita {PromotionBatch.MaxCards}: divida em dois lotes.");

        var free = PromotionMessageParser.StripCardTable(raw);
        var (textKind, textVersion) = PromotionMessageParser.ExtractKindAndVersion(free);
        var textRules = PromotionMessageParser.ExtractRules(free);

        var profiles = await LoadProfilesAsync(ct);
        var kinds = profiles.All.Select(p => p.Key).ToList();

        // Leitura por regras: o rótulo do ticket vira o tipo de repositório pelo rótulo dos perfis configurados.
        var rules = textRules
            .Select(r => new PromotionParsedRule { TicketType = r.TicketType, Kind = PromotionMessageParser.MatchKind(r.TicketType, profiles), Targets = ToDto(r.Targets), Source = "text" })
            .ToList();

        // IA: só o texto livre (sem a tabela de cards), com os tipos de repositório do PRMake.
        string? aiVersion = null, aiKind = null;
        if (free.Length > 0)
        {
            var aiRules = await TryAiAsync(free, profiles, response, ct);
            if (aiRules is not null)
            {
                (aiVersion, aiKind) = (aiRules.Value.Version, aiRules.Value.Kind);
                foreach (var r in aiRules.Value.Rules)
                {
                    // A regra da IA, com tipo conhecido, vale mais que a lida por regex para o mesmo tipo.
                    rules.RemoveAll(x => string.Equals(x.Kind, r.Kind, StringComparison.OrdinalIgnoreCase) || string.Equals(x.TicketType, r.TicketType, StringComparison.OrdinalIgnoreCase) && x.Kind is null);
                    rules.Add(new PromotionParsedRule { TicketType = r.TicketType, Kind = r.Kind, Targets = ToDto(r.Targets), Source = "ai" });
                }
            }
        }

        // Versão e tipo: o que a mensagem diz com todas as letras (regex) vale mais que a IA.
        response.Version = textVersion ?? aiVersion;
        response.Kind = textKind ?? aiKind;
        if (response.Version is null)
            response.Warnings.Add("Não identifiquei a versão — informe na tela (ex.: 3.274.10).");

        response.Rules = rules;
        foreach (var rule in rules.Where(r => r.Kind is not null))
            response.Profiles[rule.Kind!] = rule.Targets;
        foreach (var rule in rules.Where(r => r.Kind is null))
            response.Warnings.Add($"Não sei a que repositórios se aplica a regra \"{rule.TicketType}\" ({string.Join(", ", rule.Targets.Select(t => t.Branch))}): escolha os destinos na tela.");
        if (rules.Count == 0)
            response.Warnings.Add("Não encontrei regras de destino: valem os perfis padrão do PRMake.");

        return response;
    }

    private async Task<(string? Version, string? Kind, List<ParsedRule> Rules)?> TryAiAsync(string free, PromotionProfileSet profiles, PromotionParseResponse response, CancellationToken ct)
    {
        try
        {
            var prompt = await BuildPromptAsync(free, profiles, ct);
            var result = await ai.GenerateContentAsync(prompt, ct);
            if (result is null || !string.IsNullOrWhiteSpace(result.Error) || string.IsNullOrWhiteSpace(result.Content))
            {
                response.Warnings.Add($"A IA não respondeu ({result?.Error ?? "sem resposta"}): usei a leitura por regras.");
                return null;
            }

            response.AiUsed = true;
            response.Provider = result.Provider;
            response.Model = result.Model;
            var (version, kind, rules, warnings) = PromotionMessageParser.ParseAiJson(result.Content, profiles.All.Select(p => p.Key).ToList());
            response.Warnings.AddRange(warnings);
            return (version, kind, rules);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Sem chave pessoal, provedor fora do ar etc.: a tela segue com o que as regras leram.
            logger.LogInformation(e, "Promoção: a IA não pôde interpretar a mensagem");
            response.Warnings.Add($"IA indisponível ({e.Message}): usei a leitura por regras.");
            return null;
        }
    }

    private async Task<string> BuildPromptAsync(string free, PromotionProfileSet profiles, CancellationToken ct)
    {
        string? custom = null;
        try
        {
            var config = await configurationResolver.GetEffectiveConfigurationAsync("AI Configurations", ct);
            custom = config.GetConfigurationValueOrDefault(PromptKey, string.Empty);
        }
        catch (Exception e) when (e is InvalidOperationException or PersonalIntegrationRequiredException)
        {
            // sem o plugin de IA o provedor também falha, e o chamador avisa
        }

        var kinds = new StringBuilder();
        foreach (var p in profiles.All)
            kinds.AppendLine($"- {p.Key} — {p.Label}");
        if (kinds.Length == 0)
            kinds.AppendLine("(nenhum tipo configurado)");

        var template = string.IsNullOrWhiteSpace(custom) ? DefaultPrompt : custom;
        return template.Replace("{kinds}", kinds.ToString().TrimEnd()).Replace("{text}", free);
    }

    public const string DefaultPrompt = """
        Você lê a mensagem de um Release Manager que pede os PRs dos cards para uma release ou hotfix.
        Responda SOMENTE com um JSON, sem texto fora dele, neste formato:
        {"version": "3.274.10", "kind": "hotfix", "rules": [{"ticketType": "Netcore", "kind": "legacy", "targets": [{"branch": "hotfix-{version}", "draft": false}]}]}

        - "version": só os números da versão (V3.274.10 → 3.274.10); null se não houver.
        - "kind": "hotfix" ou "release" conforme a mensagem; null se não houver.
        - "rules": uma por tipo de ticket que a mensagem cita ("Branch para ticket Netcore: ..."). "ticketType" é o rótulo como está escrito.
        - "kind" de cada regra: UMA das chaves abaixo (a que melhor corresponde ao tipo de ticket); null se nenhuma corresponder.
        - "targets": as branches de destino, na ordem escrita. Troque a palavra "version" por {version} (release-version → release-{version}). "draft": true só se a mensagem disser draft/rascunho para aquele destino.
        - Não invente regras que a mensagem não traz.

        Tipos de repositório (chave — rótulo):
        {kinds}

        Mensagem:
        {text}
        """;

    private static List<PromotionTargetDto> ToDto(IEnumerable<PromotionTarget> targets) =>
        targets.Select(t => new PromotionTargetDto { Branch = t.Branch, Draft = t.Draft, Mode = t.Mode }).ToList();

    private async Task<PromotionProfileSet> LoadProfilesAsync(CancellationToken ct)
    {
        try
        {
            var config = await configurationResolver.GetEffectiveConfigurationAsync(SkillsConfigurationKeys.PluginName, ct);
            return PromotionProfileSet.Parse(config.GetConfigurationValueOrDefault(SkillsConfigurationKeys.PromotionProfiles, string.Empty));
        }
        catch (InvalidOperationException)
        {
            return PromotionProfileSet.Empty;
        }
    }
}
