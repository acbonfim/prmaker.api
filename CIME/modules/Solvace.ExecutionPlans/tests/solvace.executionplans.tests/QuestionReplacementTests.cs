using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>Card 75648: a nova rodada da análise substitui/cancela as perguntas que perderam o sentido.</summary>
public class QuestionReplacementTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-07T12:00:00Z");

    private static ExecutionQuestion Q(int order, IEnumerable<ExecutionQuestionOption>? options = null) =>
        new(Guid.NewGuid(), "propor-solucoes", order, $"Pergunta {order}", options, true, "claude", Now);

    [Fact]
    public void Open_question_replaced_is_cancelled_with_the_reason()
    {
        var q = Q(1);
        q.ReplaceWith(5);

        var r = q.ToResponse();
        Assert.Equal(ExecutionQuestionStatus.Cancelled, r.Status);
        Assert.Equal(5, r.ReplacedBy);
        Assert.Equal("Substituída pela pergunta 5", r.CancelReason);
        Assert.Throws<DomainException>(() => q.AnswerWith("Sim", "alex", false, Now));
    }

    [Fact]
    public void Answered_question_replaced_keeps_the_answer_marked_as_superseded()
    {
        var q = Q(2, [new ExecutionQuestionOption { Label = "Code Fix" }]);
        q.AnswerWith("Code Fix", "alex", false, Now);
        q.ReplaceWith(6);

        Assert.Equal(ExecutionQuestionStatus.Answered, q.Status);
        Assert.Equal("Code Fix", q.Answer);
        Assert.Equal(6, q.ReplacedBy);
        Assert.Null(q.CancelReason);
    }

    [Fact]
    public void Cancel_keeps_a_short_reason_and_ignores_answered_questions()
    {
        var open = Q(3);
        open.Cancel("  Já não se aplica: o print mostrou que o erro é de permissão  " + new string('x', 600));
        Assert.Equal(ExecutionQuestionStatus.Cancelled, open.Status);
        Assert.StartsWith("Já não se aplica", open.CancelReason);
        Assert.Equal(ExecutionQuestion.MaxCancelReasonLength, open.CancelReason!.Length);

        var answered = Q(4);
        answered.AnswerWith("Sim", "alex", false, Now);
        answered.Cancel("motivo");
        Assert.Equal(ExecutionQuestionStatus.Answered, answered.Status);
        Assert.Null(answered.CancelReason);
    }

    [Fact]
    public void A_question_cannot_replace_itself()
    {
        Assert.Throws<DomainException>(() => Q(7).ReplaceWith(7));
    }
}
