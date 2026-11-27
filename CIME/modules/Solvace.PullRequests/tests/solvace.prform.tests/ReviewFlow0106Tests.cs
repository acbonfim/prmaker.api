using solvace.github.application.Services;
using solvace.github.domain.Responses;
using solvace.prform.Execution;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0106: leitura do review do PR (código novo × contexto) e o que o especialista pode fazer a pedido.</summary>
public class ReviewFlow0106Tests
{
    [Theory]
    [InlineData("@@ -10,3 +10,4 @@\n context\n+    return RoomMessageResult(\"Room not found.\");", true)]
    [InlineData("@@ -10,3 +10,4 @@\n+added\n context line", false)]
    [InlineData("@@ -10,3 +10,3 @@\n-removed", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_comment_is_on_new_code_when_its_line_was_added_by_the_pr(string? hunk, bool expected) =>
        Assert.Equal(expected, GitHubService.IsNewLine(hunk));

    [Fact]
    public void Reviews_markdown_groups_threads_and_marks_new_code()
    {
        var r = new PullRequestReviewsResponse
        {
            Repository = "edv-solvace", Number = 31430, Head = "hotfix/79047-qa", Base = "qa", State = "open",
            ChangesRequestedBy = ["rui-rodrigues-solvace"],
            Reviews = [new PullRequestReviewItem { Id = 1, User = "rui-rodrigues-solvace", State = "changes_requested", Body = "3 pontos" }],
            Comments =
            [
                new PullRequestLineComment { Id = 10, User = "rui", Path = "RoomController.cs", Line = 240, Body = "mensagem fixa em inglês", OnNewCode = true, Snippet = "+ \"Room not found.\"" },
                new PullRequestLineComment { Id = 11, User = "rui", Path = "dob_brd_index.cshtml", Line = 1230, Body = "e aqui?", OnNewCode = false, Outdated = true },
                new PullRequestLineComment { Id = 12, InReplyTo = 10, User = "alex", Path = "RoomController.cs", Line = 240, Body = "Ajustado em 3ab27e9" }
            ]
        };
        var md = PullRequestMcpTools.ReviewsMarkdown(r);
        Assert.Contains("**Mudancas pedidas por:** rui-rodrigues-solvace", md);
        Assert.Contains("**10** · `RoomController.cs:240` · codigo NOVO do PR", md);
        Assert.Contains("**11** · `dob_brd_index.cshtml:1230` · codigo que o PR nao mudou (contexto), desatualizado", md);
        Assert.Contains("  - resposta 12 · alex: Ajustado em 3ab27e9", md);
        Assert.DoesNotContain("- **12**", md);
    }

    [Theory]
    [InlineData("prmake_analysis_start")]
    [InlineData("prmake_pr_review_reply")]
    [InlineData("prmake_pr_request_review")]
    public void Review_and_analysis_actions_run_as_who_asked(string tool)
    {
        Assert.Contains(tool, CaseSpecialistMiddleware.ActingTools);
        Assert.True(CaseSpecialistMiddleware.IsAllowed(tool));
    }

    [Theory]
    [InlineData("prmake_pr_reviews")]
    [InlineData("prmake_lessons")]
    [InlineData("prmake_lesson_record")]
    [InlineData("prmake_lesson_signal_dismiss")]
    public void The_specialist_reads_reviews_and_records_lessons(string tool) =>
        Assert.True(CaseSpecialistMiddleware.IsAllowed(tool));
}
