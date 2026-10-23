using solvace.knowledge.application;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Reverse;
using Xunit;
using Xunit.Abstractions;

namespace solvace.knowledge.tests;

/// <summary>
/// 0071: "o que preciso para transferir um usuário para outra planta?" — a resposta está espalhada em vários itens
/// (UC-065 a tela, RN-293/294 pré-condição e diálogo, RN-266 quem pode, RN-267 efeitos). O contexto do Pergunte tem que trazê-los.
/// </summary>
public class AnswerContext0071Tests(ITestOutputHelper output)
{
    private const string Question = "o que preciso para transferir um usuario para outra planta?";
    // o que a IA de plano costuma devolver (sinônimos, nomes técnicos)
    private static readonly string[] AiTerms = ["transferir usuário", "transfer user", "mudar de planta", "unidade", "área", "UserArea", "TB_WCM_USER_TRANSFER", "papéis", "convidado"];

    private static async Task<ReverseEngineeringApplication> CreateAsync(IEnumerable<(string Doc, string Text)> docs)
    {
        var repo = new InMemoryKnowledgeRepository();
        var now = DateTimeOffset.UtcNow;
        var project = new ArchitectureProject("revamp-users", "admin", now);
        project.Update("Usuários", "revamp", null, "resumo", ["usuários"], null, null, null, "admin", now);
        repo.AddProject(project);
        foreach (var (doc, text) in docs)
            foreach (var item in ReverseDocParser.Parse(text))
                repo.AddIndexEntry(new ReverseIndexEntry("revamp-users", doc, item, 1, now));
        return new ReverseEngineeringApplication(repo, new Settings());
    }

    private sealed class Settings : IReverseSettingsProvider
    {
        public Task<ReverseSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(ReverseSettings.Default);
    }

    private static string Fixture() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "users-funcional.md"));

    [Fact]
    public async Task How_to_transfer_a_user_brings_the_screen_the_preconditions_who_can_and_the_effects()
    {
        var app = await CreateAsync([("funcional", Fixture())]);
        var items = await app.GetAnswerContextAsync(Question, AiTerms, ["revamp-users"], ArchitectureQuestionKind.Operation, 7, 12, 1500, default);
        foreach (var i in items) output.WriteLine($"{i.Score,8:0.0000} {i.ItemId} {(i.Via is null ? "" : "(via " + i.Via + ")")} {i.Title}");
        var ids = items.Select(i => i.ItemId).ToList();
        foreach (var expected in new[] { "UC-065", "RN-293", "RN-294", "RN-266", "RN-267" })
            Assert.Contains(expected, ids);
        Assert.All(items, i => Assert.True(i.Body.Length > 0 && i.Body.Length <= 1510));
    }

    [Fact]
    public async Task Items_cited_by_the_best_ones_come_in_even_when_the_search_did_not_rank_them()
    {
        var app = await CreateAsync([("funcional", Fixture())]);
        var items = await app.GetAnswerContextAsync(Question, AiTerms, [], ArchitectureQuestionKind.Operation, 3, 6, 1500, default);
        Assert.Contains(items, i => i.Via is not null);
        Assert.Equal(items.Count, items.Select(i => i.Ref).Distinct().Count());
    }

    [Theory]
    [InlineData("como faço para transferir um usuário?", "regra", "operacao")]
    [InlineData("o que preciso para transferir um usuário para outra planta?", "outra", "operacao")]
    [InlineData("quem pode excluir um usuário SSO?", "regra", "operacao")]
    [InlineData("onde fica a tela de usuários pendentes", "outra", "operacao")]
    [InlineData("como a API de transferência grava o histórico?", "tecnica", "tecnica")]
    [InlineData("qual a regra de liberação de usuário SSO?", "regra", "regra")]
    public void Intent_is_how_to_when_the_question_asks_how_but_never_overrides_technical(string question, string aiKind, string expected) =>
        Assert.Equal(expected, ReverseAnswerContext.Intent(question, aiKind));

    [Fact]
    public void Kind_weights_favor_screens_and_rules_for_how_to_and_endpoints_for_technical()
    {
        Assert.True(ReverseAnswerContext.KindWeight("UC", "operacao") > ReverseAnswerContext.KindWeight("API", "operacao"));
        Assert.True(ReverseAnswerContext.KindWeight("API", "tecnica") > ReverseAnswerContext.KindWeight("UC", "tecnica"));
        Assert.True(ReverseAnswerContext.KindWeight("RN", "regra") > ReverseAnswerContext.KindWeight("TELA", "regra"));
    }

    /// <summary>Com o espelho local presente, o mesmo teste na engenharia reversa inteira do módulo (24 mil itens no total na Base).</summary>
    [Fact]
    public async Task Real_mirror_of_revamp_users_answers_the_transfer_question()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "solvace-kb", "projects", "revamp-users");
        if (!Directory.Exists(dir)) return;
        var docs = new[] { ("funcional", "110-re-funcional.md"), ("arquitetura", "120-re-arquitetura.md"), ("visao", "130-re-visao.md"),
                ("spec-arquitetura", "140-re-spec-arquitetura.md"), ("design", "150-re-design.md") }
            .Where(d => File.Exists(Path.Combine(dir, d.Item2))).Select(d => (d.Item1, File.ReadAllText(Path.Combine(dir, d.Item2)))).ToList();
        var app = await CreateAsync(docs);
        var items = await app.GetAnswerContextAsync(Question, AiTerms, ["revamp-users"], ArchitectureQuestionKind.Operation, 7, 12, 1500, default);
        foreach (var i in items) output.WriteLine($"{i.Score,8:0.0000} {i.DocType,-10} {i.ItemId} {(i.Via is null ? "" : "(via " + i.Via + ")")} {i.Title}");
        var ids = items.Select(i => i.ItemId).ToList();
        foreach (var expected in new[] { "UC-065", "RN-293", "RN-294", "RN-266", "RN-267" })
            Assert.Contains(expected, ids);
    }
}
