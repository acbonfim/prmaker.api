using solvace.executionplans.domain.Entities;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0090: <c>/clear</c> na conversa do caso — a sessão do especialista recomeça e o que veio antes some.</summary>
public class CaseChatClear0090Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-05T20:00:00Z");

    private static CaseChat ChatWithSession()
    {
        var chat = new CaseChat("77988", Now.AddHours(-2));
        chat.RegisterSession("sess-1", "mac-bruno", "/tmp/case", Now.AddHours(-1));
        chat.MarkRead(10, Now.AddHours(-1));
        chat.SetPendingRun(true, Now.AddMinutes(-1));
        return chat;
    }

    [Fact]
    public void Clear_forgets_the_specialist_session_and_skips_what_was_said()
    {
        var chat = ChatWithSession();
        chat.Clear("Alex Carlos", 14, Now);

        Assert.Null(chat.SessionId);
        Assert.Null(chat.SessionHost);
        Assert.Null(chat.SessionCwd);
        Assert.False(chat.PendingRun);
        Assert.Equal(14, chat.LastReadNumber);
        Assert.Equal(Now, chat.ClearedAt);
        Assert.Equal("Alex Carlos", chat.ClearedBy);
    }

    [Fact]
    public void Clear_never_moves_the_read_mark_back()
    {
        var chat = ChatWithSession();
        chat.Clear("Alex", 3, Now);
        Assert.Equal(10, chat.LastReadNumber);
    }

    [Fact]
    public void Shows_only_what_came_after_the_clear()
    {
        var chat = ChatWithSession();
        Assert.True(chat.Shows(Now.AddDays(-1)));
        chat.Clear("Alex", 14, Now);
        Assert.False(chat.Shows(Now.AddSeconds(-1)));
        Assert.False(chat.Shows(Now));
        Assert.True(chat.Shows(Now.AddSeconds(1)));
    }

    [Fact]
    public void Session_of_a_request_older_than_the_clear_does_not_come_back()
    {
        var chat = ChatWithSession();
        chat.Clear("Alex", 14, Now);

        chat.RegisterSession("sess-old", "mac-bruno", null, Now.AddSeconds(5), requestedAt: Now.AddMinutes(-2));
        Assert.Null(chat.SessionId);

        chat.RegisterSession("sess-new", "mac-bruno", null, Now.AddMinutes(1), requestedAt: Now.AddSeconds(30));
        Assert.Equal("sess-new", chat.SessionId);
    }

    [Fact]
    public void Clear_without_a_name_is_still_recorded() =>
        Assert.Equal("Usuário", Cleared(" ").ClearedBy);

    private static CaseChat Cleared(string by)
    {
        var chat = new CaseChat("77988", Now);
        chat.Clear(by, 0, Now);
        return chat;
    }
}
