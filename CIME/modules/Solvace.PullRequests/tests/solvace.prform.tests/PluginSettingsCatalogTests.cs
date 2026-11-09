using System.Reflection;
using solvace.azure.domain.Options;
using solvace.prform.application.Security;
using solvace.prform.domain.Enums;
using solvace.prform.Knowledge;
using solvace.prform.PluginSettings;
using solvace.prform.Skills;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0086: formato do catálogo das configurações — toda chave com seção, rótulo, tipo e padrão válidos.</summary>
public class PluginSettingsCatalogTests
{
    private static readonly IReadOnlyList<SettingDef> Fields = PluginSettingsCatalog.Fields;

    [Fact]
    public void No_plugin_key_is_cataloged_twice()
    {
        var duplicated = Fields.GroupBy(f => (f.Plugin, f.Key)).Where(g => g.Count() > 1).Select(g => $"{g.Key.Plugin.Name}:{g.Key.Key}").ToList();
        Assert.Empty(duplicated);
    }

    [Fact]
    public void Every_field_has_a_known_section_group_label_type_and_readers()
    {
        foreach (var f in Fields)
        {
            var section = PluginSettingsCatalog.Sections.SingleOrDefault(s => s.Key == f.Section);
            Assert.True(section is not null, $"{f.Key}: seção {f.Section} não existe");
            Assert.True(section!.Groups.Any(g => g.Key == f.Group), $"{f.Key}: grupo {f.Group} não existe em {f.Section}");
            Assert.False(string.IsNullOrWhiteSpace(f.Label), $"{f.Key}: sem rótulo");
            Assert.Contains(f.Type, SettingTypes.All);
            Assert.All(f.ReadBy, r => Assert.Contains(r, SettingReaders.All));
            Assert.True(f.Obsolete || f.ReadBy.Count > 0, $"{f.Key}: ninguém lê e não está obsoleta");
            Assert.Equal(f.Key.Trim(), f.Key);
        }
    }

    [Fact]
    public void Sections_have_the_contract_keys_in_order_and_no_empty_group()
    {
        Assert.Equal(["devops", "branches", "analise", "executor", "itens", "assistente", "ia", "prompts", "engenharia-reversa", "knowledge", "integracoes"],
            PluginSettingsCatalog.Sections.Select(s => s.Key));
        foreach (var section in PluginSettingsCatalog.Sections)
        {
            Assert.Equal(section.Groups.Count, section.Groups.Select(g => g.Key).Distinct().Count());
            foreach (var group in section.Groups)
                Assert.True(Fields.Any(f => f.Section == section.Key && f.Group == group.Key), $"{section.Key}/{group.Key} sem campos");
        }
    }

    [Fact]
    public void Structured_types_carry_their_metadata()
    {
        foreach (var f in Fields)
        {
            switch (f.Type)
            {
                case SettingTypes.Enum:
                    Assert.True(f.Options is { Count: > 0 }, $"{f.Key}: enum sem opções");
                    break;
                case SettingTypes.Table:
                    Assert.True(f.Columns is { Count: > 0 }, $"{f.Key}: tabela sem colunas");
                    Assert.Equal(f.Columns!.Count, f.Columns.Select(c => c.Key).Distinct().Count());
                    Assert.All(f.Columns, c => Assert.Contains(c.Type, SettingTypes.ColumnTypes));
                    Assert.All(f.Columns.Where(c => c.Type == SettingTypes.Enum), c => Assert.True(c.Options is { Count: > 0 }, $"{f.Key}.{c.Key}: enum sem opções"));
                    if (f.RowKey is not null)
                        Assert.Contains(f.Columns, c => c.Key == f.RowKey);
                    break;
                case SettingTypes.Map:
                    Assert.Contains(f.MapValueType!, SettingTypes.MapValueTypes);
                    Assert.False(string.IsNullOrWhiteSpace(f.MapKeyLabel));
                    Assert.False(string.IsNullOrWhiteSpace(f.MapValueLabel));
                    break;
                case SettingTypes.List:
                    Assert.Contains(f.ListFormat, new[] { "csv", "json" });
                    break;
                case SettingTypes.Steps:
                    Assert.Contains(f.ListFormat, new[] { "csv", "json" });
                    Assert.True(f.StepSources is { Count: > 0 }, $"{f.Key}: etapas sem origem");
                    break;
                case SettingTypes.Number when f.Min is { } min && f.Max is { } max:
                    Assert.True(min <= max, $"{f.Key}: mínimo maior que o máximo");
                    break;
            }
            if (f.Variables is not null)
                Assert.Equal(f.Variables.Count, f.Variables.Select(v => v.Name).Distinct().Count());
        }
    }

    [Fact]
    public void Defaults_are_valid_for_their_type()
    {
        foreach (var f in Fields.Where(f => f.Default is not null))
            Assert.True(PluginSettingsRules.Validate(f, f.Default) is null, $"{f.Key}: padrão inválido — {PluginSettingsRules.Validate(f, f.Default)}");
    }

    [Fact]
    public void Secrets_have_no_default_and_every_sensitive_name_is_secret()
    {
        Assert.All(Fields.Where(f => f.Type == SettingTypes.Secret), f => Assert.Null(f.Default));
        var notSecret = Fields.Where(f => SensitiveFieldPolicy.IsSensitive(f.Key) && f.Type != SettingTypes.Secret).Select(f => f.Key).ToList();
        Assert.Empty(notSecret);
        // CloudinaryUrl contém o segredo e o nome não casa com a política: secreto pelo tipo.
        Assert.False(SensitiveFieldPolicy.IsSensitive("CloudinaryUrl"));
        Assert.Equal(SettingTypes.Secret, Fields.Single(f => f.Plugin == PluginSettingsCatalog.Bucket && f.Key == "CloudinaryUrl").Type);
    }

    [Fact]
    public void Plugins_9_and_11_are_found_by_id()
    {
        Assert.Equal(EPlugin.PULLREQUEST, PluginSettingsCatalog.PullRequest.Id);
        Assert.Equal(EPlugin.BUCKET_IMAGE, PluginSettingsCatalog.Bucket.Id);
        Assert.Contains(Fields, f => f.Plugin == PluginSettingsCatalog.PullRequest && f.Key == "ActiveBranchs");
        Assert.Contains(Fields, f => f.Plugin == PluginSettingsCatalog.PullRequest && f.Key == "ActiveRepositories");
    }

    [Fact]
    public void Dead_keys_are_obsolete()
    {
        Assert.True(Fields.Single(f => f.Key == SkillsConfigurationKeys.ExecutorNoteDelaySeconds).Obsolete);
        Assert.True(Fields.Single(f => f.Plugin == PluginSettingsCatalog.GitHub && f.Key == "Branch").Obsolete);
        var ticket = Fields.Single(f => f.Key == SkillsConfigurationKeys.TicketSystem);
        Assert.False(ticket.Obsolete);
        Assert.Equal(SettingTypes.Text, ticket.Type);
        Assert.Equal([SettingReaders.Skills], ticket.ReadBy);
    }

    public static TheoryData<string, string> CodeKeys()
    {
        var data = new TheoryData<string, string>();
        void Add(Type type, string plugin, params string[] skip)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string)))
            {
                var key = (string)field.GetRawConstantValue()!;
                if (field.Name != "PluginName" && !skip.Contains(key))
                    data.Add(plugin, key);
            }
        }
        Add(typeof(SkillsConfigurationKeys), SkillsConfigurationKeys.PluginName);
        Add(typeof(KnowledgeCenterConfigurationKeys), KnowledgeCenterConfigurationKeys.PluginName);
        Add(typeof(AIConfigurationKeys), AIConfigurationKeys.PluginName);
        Add(typeof(AzureDevOpsFieldKeys), "AzureDevOps Configurations");
        return data;
    }

    /// <summary>Toda chave que o código declara está no catálogo, no plugin certo (chave nova no código = entrada nova aqui).</summary>
    [Theory]
    [MemberData(nameof(CodeKeys))]
    public void Every_key_declared_in_code_is_cataloged(string plugin, string key) =>
        Assert.Contains(Fields, f => f.Key == key && f.Plugin.Name == plugin && f.Plugin.Id is null);

    [Fact]
    public void Catalog_covers_the_inventory()
    {
        // ~130 chaves no inventário (features/0086/inventario.md).
        Assert.True(Fields.Count >= 125, $"só {Fields.Count} chaves no catálogo");
        // 46 do inventário + WorkItemTypes (0085 r4) + BranchNamePatternUserStory (0085 r6) + ValidationEnvironments (0092).
        Assert.Equal(49, Fields.Count(f => f.Plugin == PluginSettingsCatalog.Skills));
    }
}
