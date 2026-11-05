using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using solvace.prform.Skills;

namespace solvace.prform.Controllers;

/// <summary>0085: catálogo dos itens de trabalho — tipos de sub-tarefa e modelos prontos (Skills Configurations).</summary>
public partial class WorkItemsController
{
    /// <summary>
    /// Tipos de sub-tarefa (<c>WorkItemTaskKinds</c>) e modelos (<c>WorkItemTemplates</c>) já preenchidos para o card:
    /// <c>{branch}</c> = branch principal (BranchNamePattern), <c>{suffix:destino}</c> = sufixo do BranchSuffixByTarget
    /// (<c>{branch}{suffix:release-version}</c> → <c>hotfix/77434-rv</c>), <c>{card}</c>, <c>{repository}</c> (repositório
    /// padrão) e <c>{flowTargets}</c> (destinos dos PRs do fluxo do card). Sem card, os modelos voltam como estão.
    /// Cada param dos tipos traz <c>input</c> (repository|branch|target-branch|state|commits|text|textarea|select) e
    /// <c>options?</c> — config antiga sem <c>input</c> recebe o padrão pela chave (<see cref="WorkItemParamInputs"/>).
    /// </summary>
    [HttpGet("catalog")]
    public Task<ActionResult<object>> Catalog([FromQuery] string? card, [FromServices] SkillsRulesService rules, CancellationToken ct) =>
        Run<object>(async () =>
        {
            var c = SkillsRulesService.NormalizeCard(card);
            CardRules? r = c.Length > 0 ? await rules.GetAsync(c, ct) : null;
            var settings = r?.Settings ?? await rules.SettingsOnlyAsync(ct);
            string V(string key) => settings.TryGetValue(key, out var v) ? v : string.Empty;

            var kinds = ParseArray(V(SkillsConfigurationKeys.WorkItemTaskKinds));
            WorkItemParamInputs.Apply(kinds, V(SkillsConfigurationKeys.TicketSystem)); // r3: input/options do editor
            var templates = ParseArray(V(SkillsConfigurationKeys.WorkItemTemplates));
            if (r is not null)
            {
                var extras = r.TemplateExtras();
                foreach (var t in templates.OfType<JsonObject>())
                    ExpandStrings(t, s => BranchRules.Expand(s, r.Card, r.MainBranch, r.Suffixes, extras));
            }

            return Ok(new
            {
                card = r?.Card,
                type = r is null ? null : r.IsUserStory ? "user-story" : "bug",
                mainBranch = r?.MainBranch,
                branchesByTarget = r?.Suffixes.ToDictionary(s => s.Key, s => BranchRules.BranchFor(r.MainBranch, s.Key, r.Suffixes)),
                taskKinds = kinds,
                templates
            });
        });

    private static JsonArray ParseArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonNode.Parse(json) as JsonArray ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Troca os marcadores em todos os textos do modelo (título, descrição, sub-tarefas e params).</summary>
    private static void ExpandStrings(JsonNode node, Func<string, string> expand)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (key == "key") continue; // a chave do modelo não muda
                    if (obj[key] is JsonValue v && v.TryGetValue<string>(out var s))
                        obj[key] = expand(s);
                    else if (obj[key] is { } child)
                        ExpandStrings(child, expand);
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonValue v && v.TryGetValue<string>(out var s))
                        arr[i] = expand(s);
                    else if (arr[i] is { } child)
                        ExpandStrings(child, expand);
                }
                break;
        }
    }
}
