using System.Text.Json;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Requests;
using solvace.prform.PluginSettings;
using solvace.prform.Skills;
using Xunit;
using Xunit.Abstractions;

namespace solvace.prform.tests;

/// <summary>0086: validação por tipo, padrão, conflito pelo valor esperado, mescla e o catálogo resolvido (sem banco).</summary>
public class PluginSettingsRulesTests(ITestOutputHelper output)
{
    private static SettingDef Def(string key) => PluginSettingsCatalog.Fields.First(f => f.Key == key);

    private static SettingDef Of(SettingPluginRef plugin, string key) => PluginSettingsCatalog.Fields.Single(f => f.Plugin == plugin && f.Key == key);

    // ------------------------------------------------------------------ validação

    [Theory]
    [InlineData("true", true)]
    [InlineData("False", true)]
    [InlineData("sim", false)]
    [InlineData("1", false)]
    public void Bool_accepts_only_true_or_false(string value, bool valid) =>
        Assert.Equal(valid, PluginSettingsRules.Validate(Def(SkillsConfigurationKeys.AutoDevTestInQa), value) is null);

    [Theory]
    [InlineData("2", true)]
    [InlineData("10", true)]
    [InlineData("0", false)]   // mínimo 1
    [InlineData("11", false)]  // máximo 10
    [InlineData("dois", false)]
    [InlineData("1,5", false)] // decimal com ponto
    public void Number_respects_format_and_limits(string value, bool valid) =>
        Assert.Equal(valid, PluginSettingsRules.Validate(Def(SkillsConfigurationKeys.WorkItemSlots), value) is null);

    [Fact]
    public void Decimal_number_uses_the_invariant_point()
    {
        var coverage = Def(SkillsConfigurationKeys.ReverseEngineeringMinCoverage);
        Assert.Null(PluginSettingsRules.Validate(coverage, "0.85"));
        Assert.NotNull(PluginSettingsRules.Validate(coverage, "1.5"));
    }

    [Fact]
    public void Enum_without_custom_only_accepts_its_options()
    {
        var tests = Def(SkillsConfigurationKeys.CorrectionLocalTests);
        Assert.Null(PluginSettingsRules.Validate(tests, "off"));
        Assert.NotNull(PluginSettingsRules.Validate(tests, "sempre"));
        // modelos aceitam id fora da lista
        Assert.Null(PluginSettingsRules.Validate(Def(SkillsConfigurationKeys.ExecutorAnalysisModel), "claude-opus-5"));
    }

    [Fact]
    public void Empty_value_is_accepted_and_null_is_not()
    {
        Assert.Null(PluginSettingsRules.Validate(Def(SkillsConfigurationKeys.WorkItemSlots), ""));
        Assert.NotNull(PluginSettingsRules.Validate(Def(SkillsConfigurationKeys.WorkItemSlots), null));
        Assert.NotNull(PluginSettingsRules.Validate(null, null));
        Assert.Null(PluginSettingsRules.Validate(null, "qualquer texto")); // fora do catálogo: só texto
    }

    [Fact]
    public void Json_types_check_parse_and_root()
    {
        var strategy = Def(SkillsConfigurationKeys.BranchStrategy);
        Assert.Null(PluginSettingsRules.Validate(strategy, """{"repositories": []}"""));
        Assert.NotNull(PluginSettingsRules.Validate(strategy, """[1, 2]"""));
        Assert.NotNull(PluginSettingsRules.Validate(strategy, """{"repositories": [}"""));

        var kinds = Def(SkillsConfigurationKeys.WorkItemTaskKinds);
        Assert.Null(PluginSettingsRules.Validate(kinds, """[{"kind": "pr"}]"""));
        Assert.NotNull(PluginSettingsRules.Validate(kinds, """{"kind": "pr"}"""));
    }

    [Fact]
    public void Table_requires_its_required_columns_and_numbers()
    {
        var flow = Def(SkillsConfigurationKeys.BranchFlowByArea);
        Assert.Null(PluginSettingsRules.Validate(flow, """[{"areaContains": "Release Management", "flow": "release", "extra": 1}]"""));
        Assert.NotNull(PluginSettingsRules.Validate(flow, """[{"areaContains": "", "flow": "release"}]"""));
        Assert.NotNull(PluginSettingsRules.Validate(flow, """[{"areaContains": "X"}]"""));
        Assert.NotNull(PluginSettingsRules.Validate(flow, """{"areaContains": "X", "flow": "release"}"""));

        var prices = Def(solvace.prform.AiUsage.AiUsageRecorder.PricesKey);
        Assert.Null(PluginSettingsRules.Validate(prices, """{"claude-opus": {"input": 5, "output": 25}}"""));
        Assert.NotNull(PluginSettingsRules.Validate(prices, """{"claude-opus": {"input": "caro", "output": 25}}"""));
        Assert.NotNull(PluginSettingsRules.Validate(prices, """[{"model": "claude-opus", "input": 5, "output": 25}]"""));
    }

    [Fact]
    public void Map_checks_the_value_type()
    {
        var suffixes = Def(SkillsConfigurationKeys.BranchSuffixByTarget);
        Assert.Null(PluginSettingsRules.Validate(suffixes, """{"qa": "-qa"}"""));
        Assert.NotNull(PluginSettingsRules.Validate(suffixes, """{"qa": ["-qa"]}"""));

        var supersedes = Def(SkillsConfigurationKeys.ReverseEngineeringSupersedes);
        Assert.Null(PluginSettingsRules.Validate(supersedes, """{"modulos": ["funcional", "uiux"]}"""));
        Assert.NotNull(PluginSettingsRules.Validate(supersedes, """{"modulos": "funcional"}"""));
    }

    [Fact]
    public void Json_list_must_be_an_array_of_values()
    {
        var exclusions = Def(SkillsConfigurationKeys.ReverseEngineeringGlossaryExclusions);
        Assert.Null(PluginSettingsRules.Validate(exclusions, """["Salvar", "Cancelar"]"""));
        Assert.NotNull(PluginSettingsRules.Validate(exclusions, "Salvar, Cancelar"));
        Assert.NotNull(PluginSettingsRules.Validate(exclusions, """[{"a": 1}]"""));
    }

    [Fact]
    public void Secret_mask_is_not_a_value() =>
        Assert.NotNull(PluginSettingsRules.Validate(Of(PluginSettingsCatalog.DevOps, "PersonalAccessToken"), PluginSettingChange.SecretMask));

    // ------------------------------------------------------------------ padrão

    [Fact]
    public void Default_compares_by_type()
    {
        Assert.True(PluginSettingsRules.IsDefault(Def(SkillsConfigurationKeys.CodeRefGateSteps), true, "montar-analise, propor-solucoes"));
        Assert.False(PluginSettingsRules.IsDefault(Def(SkillsConfigurationKeys.CodeRefGateSteps), true, "montar-analise"));
        Assert.True(PluginSettingsRules.IsDefault(Def(SkillsConfigurationKeys.WorkItemSlots), true, "2.0"));
        Assert.True(PluginSettingsRules.IsDefault(Def(SkillsConfigurationKeys.AutoDevTestInQa), true, "True"));
        Assert.True(PluginSettingsRules.IsDefault(Def(SkillsConfigurationKeys.BranchSuffixByTarget), true,
            """{ "development": "-dev", "qa": "-qa", "release-version": "-rv", "hotfix-version": "-hv", "edge": "-edge" }"""));
        // chave que não existe: o código usa a reserva
        Assert.True(PluginSettingsRules.IsDefault(Def(SkillsConfigurationKeys.WorkItemSlots), false, null));
        // sem padrão no catálogo: vazio é o padrão
        Assert.True(PluginSettingsRules.IsDefault(Def(SkillsConfigurationKeys.DefaultRepository), true, ""));
        Assert.False(PluginSettingsRules.IsDefault(Def(SkillsConfigurationKeys.DefaultRepository), true, "edv-solvace"));
    }

    // ------------------------------------------------------------------ conflito e mescla

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("a", "a", true)]
    [InlineData("a\r\nb", "a\nb", true)]
    [InlineData(null, "a", false)]
    [InlineData("a", null, false)]
    [InlineData("a", "b", false)]
    public void Expected_value_detects_another_admin(string? current, string? expected, bool matches) =>
        Assert.Equal(matches, PluginSettingsRules.MatchesExpected(current, expected));

    [Fact]
    public void Merge_changes_only_the_sent_keys_and_keeps_the_order()
    {
        var current = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2", ["C"] = "3", ["Old"] = "x" };
        var merged = PluginSettingsRules.Merge(current, [("B", "20", false), ("New", "n", false), ("Old", null, true), ("Missing", null, true)]);
        Assert.Equal(["A", "B", "C", "New"], merged.Keys);
        Assert.Equal("20", merged["B"]);
        Assert.Equal("1", merged["A"]);
        Assert.Equal("3", merged["C"]);
    }

    [Fact]
    public void Keys_are_case_sensitive_like_the_readers()
    {
        var merged = PluginSettingsRules.Merge(new Dictionary<string, string> { ["Key"] = "1" }, [("key", "2", false)]);
        Assert.Equal(["Key", "key"], merged.Keys);
    }

    [Fact]
    public void Options_keep_the_stored_format()
    {
        var options = new PluginConfiguration([new Dictionary<string, string> { ["A"] = "1", ["Prompt"] = "linha 1\nção \"x\"" }]).Options;
        var values = PluginSettingsRules.ReadValues(options);
        var merged = PluginSettingsRules.Merge(values, [("A", "2", false)]);
        var written = PluginSettingsRules.WriteValues(options, merged);
        Assert.Equal("""[{"A":"2","Prompt":"linha 1\nção \"x\""}]""", written);
        // mesmo formato do construtor
        Assert.Equal(new PluginConfiguration([merged]).Options, written);
        // demais itens da lista ficam
        Assert.Equal("""[{"A":"9"},{"Z":"z"}]""", PluginSettingsRules.WriteValues("""[{"A":"1"},{"Z":"z"}]""", new Dictionary<string, string> { ["A"] = "9" }));
        Assert.Empty(PluginSettingsRules.ReadValues(null));
        Assert.ThrowsAny<Exception>(() => PluginSettingsRules.ReadValues("{nao"));
    }

    [Fact]
    public void Diff_lists_added_changed_and_removed_keys()
    {
        var diff = PluginSettingDiff.Compute(
            new Dictionary<string, string> { ["A"] = "1", ["B"] = "2", ["C"] = "3" },
            new Dictionary<string, string> { ["A"] = "1", ["B"] = "20", ["D"] = "4" });
        Assert.Equal(3, diff.Count);
        Assert.Contains(("B", "2", "20"), diff);
        Assert.Contains(("D", (string?)null, "4"), diff);
        Assert.Contains(("C", "3", (string?)null), diff);
    }

    [Fact]
    public void History_masks_secrets()
    {
        Assert.Equal(PluginSettingChange.SecretMask, PluginSettingHistory.Mask("ghp_abc"));
        Assert.Equal(string.Empty, PluginSettingHistory.Mask(""));
        Assert.Null(PluginSettingHistory.Mask(null));
    }

    // ------------------------------------------------------------------ PATCH: erros antes do banco

    [Fact]
    public void Patch_validation_reports_each_change()
    {
        var plugins = new[] { Plugin(20, SkillsConfigurationKeys.PluginName, new() { ["WorkItemSlots"] = "2" }) };
        var errors = PluginSettingsService.Validate(
        [
            new() { PluginId = 20, Key = SkillsConfigurationKeys.WorkItemSlots, Value = "50", Expected = "2" },
            new() { PluginId = 20, Key = SkillsConfigurationKeys.AutoDevTestInQa, Value = "talvez" },
            new() { PluginId = 20, Key = SkillsConfigurationKeys.AutoDevTestInQa, Value = "true" },
            new() { PluginId = 99, Key = "Foo", Value = "x" },
            new() { PluginId = 20, Key = " ", Value = "x" },
            new() { PluginId = 20, Key = "ForaDoCatalogo", Value = "qualquer" },
            new() { PluginId = 20, Key = SkillsConfigurationKeys.ExecutorNoteDelaySeconds, Remove = true }
        ], plugins);
        Assert.Equal(["20:WorkItemSlots", "20:AutoDevTestInQa", "20:AutoDevTestInQa", "99:Foo", "20: "], errors.Select(e => e.Id));
    }

    // ------------------------------------------------------------------ catálogo resolvido

    [Fact]
    public void Resolved_catalog_never_exposes_secrets_and_uses_the_admin_label()
    {
        var devops = Plugin(8, "AzureDevOps Configurations", new() { ["Organization"] = "solvace", ["PersonalAccessToken"] = "pat-123", ["MySecretKey"] = "k", ["Extra"] = "e" },
            personal: true, personalFields: ["PersonalAccessToken"],
            fieldSettings: new() { ["Organization"] = new PluginFieldSetting { Label = "Org do admin", Help = "Ajuda do admin" } });
        devops.Configurations.Version = 4711;
        var bucket = Plugin(11, "Bucket", new() { ["CloudinaryUrl"] = "cloudinary://k:s@nuvem" });

        var settings = PluginSettingsResolver.Build([devops, bucket]);
        var fields = settings.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Fields).ToList();

        var org = fields.Single(f => f.Id == "8:Organization");
        Assert.Equal("solvace", org.Value);
        Assert.Equal("Org do admin", org.Label);
        Assert.Equal("Ajuda do admin", org.Help);
        Assert.False(org.Personal!.UserFills);

        var pat = fields.Single(f => f.Id == "8:PersonalAccessToken");
        Assert.Null(pat.Value);
        Assert.True(pat.HasValue);
        Assert.True(pat.Personal!.UserFills);
        Assert.True(pat.Personal.Required);

        var cloudinary = fields.Single(f => f.Id == "11:CloudinaryUrl");
        Assert.Null(cloudinary.Value);
        Assert.True(cloudinary.HasValue);
        Assert.Equal("Bucket", cloudinary.PluginName);

        Assert.Contains(settings.Uncataloged, u => u.Key == "Extra" && u.Value == "e" && !u.Secret);
        Assert.Contains(settings.Uncataloged, u => u.Key == "MySecretKey" && u.Value is null && u.Secret);
        Assert.DoesNotContain("pat-123", JsonSerializer.Serialize(settings));
        Assert.DoesNotContain("nuvem", JsonSerializer.Serialize(settings));

        Assert.Equal("4711", settings.Plugins.Single(p => p.Id == 8).Version);
        Assert.Contains(SkillsConfigurationKeys.PluginName, settings.MissingPlugins);
        var missing = fields.Single(f => f.Id == $"{SkillsConfigurationKeys.PluginName}:{SkillsConfigurationKeys.WorkItemSlots}");
        Assert.Null(missing.PluginId);
        Assert.False(missing.Exists);
        Assert.True(missing.IsDefault);
    }

    /// <summary>Imprime a seção "branches" como o GET devolve (dotnet test --logger "console;verbosity=detailed").</summary>
    [Fact]
    public void Branches_section_serializes_as_the_contract()
    {
        var skills = Plugin(20, SkillsConfigurationKeys.PluginName, new()
        {
            [SkillsConfigurationKeys.BranchNamePattern] = "hotfix/{card}",
            [SkillsConfigurationKeys.BranchFlowByArea] = """[{"areaContains":"Release Management","flow":"release"}]""",
            [SkillsConfigurationKeys.PromotionBranchPattern] = "{branch}{suffix}"
        }, fieldSettings: new() { [SkillsConfigurationKeys.BranchNamePattern] = new PluginFieldSetting { Label = "Nome da branch de correção ({card})" } });
        var pr = Plugin(9, "PullRequest", new() { ["ActiveBranchs"] = "[{label: 'QA', branchName: 'qa'}]" });

        var section = PluginSettingsResolver.Build([skills, pr]).Sections.Single(s => s.Key == "branches");
        var json = JsonSerializer.Serialize(section, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        output.WriteLine(json);

        var fields = section.Groups.SelectMany(g => g.Fields).ToList();
        var promotion = fields.Single(f => f.Key == SkillsConfigurationKeys.PromotionBranchPattern);
        Assert.False(promotion.IsDefault);
        Assert.Equal("{branch}-{target}", promotion.Default);
        Assert.Contains(promotion.Variables!, v => v.Name == "suffix");
        Assert.Equal("9:ActiveBranchs", fields.Single(f => f.Key == "ActiveBranchs").Id);
        Assert.Contains("\"rowKey\": null", json);
    }

    private static Plugin Plugin(int id, string name, Dictionary<string, string> values, bool personal = false, List<string>? personalFields = null,
        Dictionary<string, PluginFieldSetting>? fieldSettings = null)
    {
        var plugin = new Plugin(new PluginRequest
        {
            Description = name, Configurations = values, IsPersonal = personal, PersonalFields = personalFields, FieldSettings = fieldSettings
        });
        plugin.Id = id;
        return plugin;
    }
}
