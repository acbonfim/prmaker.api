using solvace.prform.Controllers;
using solvace.prform.Execution;
using solvace.prform.Knowledge;
using solvace.prform.PluginSettings;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0102: perguntas da Base Solvace pelo executor — flag pessoal, token restrito e anexos com nome seguro.</summary>
public class BaseAsk0102Tests
{
    [Theory]
    [InlineData("executor", BaseSolvaceRunner.Executor)]
    [InlineData(" Executor ", BaseSolvaceRunner.Executor)]
    [InlineData("Claude Code", BaseSolvaceRunner.Executor)]
    [InlineData("provider", BaseSolvaceRunner.Provider)]
    [InlineData("", BaseSolvaceRunner.Provider)]
    [InlineData(null, BaseSolvaceRunner.Provider)]
    [InlineData("qualquer", BaseSolvaceRunner.Provider)]
    public void Runner_falls_back_to_the_provider(string? value, string expected) =>
        Assert.Equal(expected, BaseSolvaceRunner.Normalize(value));

    [Fact]
    public void Runner_is_a_closed_choice_in_the_catalog_so_it_shows_as_a_list_in_my_integrations()
    {
        var def = Assert.Single(PluginSettingsCatalog.Fields, f => f.Key == BaseSolvaceRunner.Key);
        Assert.Equal(PluginSettingsCatalog.Ai, def.Plugin);
        Assert.Equal(SettingTypes.Enum, def.Type);
        Assert.False(def.AllowCustom);
        Assert.Equal([BaseSolvaceRunner.Provider, BaseSolvaceRunner.Executor], def.Options!.Select(o => o.Value));
        Assert.Equal(BaseSolvaceRunner.Provider, def.Default);
    }

    [Fact]
    public void Question_token_only_reaches_the_base_tools_and_the_answer()
    {
        Assert.Equal(["prmake_base_answer", "prmake_base_get", "prmake_base_impact", "prmake_base_module", "prmake_base_search"],
            BaseAskClaims.Tools.Order());
        Assert.DoesNotContain("prmake_advance", BaseAskClaims.Tools);
        Assert.DoesNotContain("prmake_timeline", BaseAskClaims.Tools);
    }

    [Theory]
    [InlineData("erro.png", "image/png", "erro.png")]
    [InlineData("../../etc/passwd", "text/plain", "passwd.txt")]
    [InlineData("C:\\tmp\\print da tela.jpg", "image/jpeg", "print_da_tela.jpg")]
    [InlineData("", "application/pdf", "anexo-1.pdf")]
    [InlineData(null, "image/png", "anexo-1.png")]
    public void Attachment_names_are_safe_for_the_machine(string? name, string mediaType, string expected) =>
        Assert.Equal(expected, BaseAskController.SafeName(name, mediaType, 1));

    [Fact]
    public void Repeated_attachment_names_get_a_suffix()
    {
        var files = BaseAskController.Files([
            new("print.png", "image/png", [1], "image"),
            new("print.png", "image/png", [2], "image")
        ]);

        Assert.Equal(["print.png", "print-2.png"], files.Select(f => f.Name));
        Assert.Equal(Convert.ToBase64String([2]), files[1].Data);
    }
}
