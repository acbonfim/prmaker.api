using System.Text;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// 0085 r5: seção "Arquivos" do item (1.2 — os do item + os do plano ligado) e áudio de quem conversa fora dos arquivos
/// da conversa (1.7).
/// </summary>
public class WorkItemFileTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-05T12:00:00Z");

    private static WorkItemFile ItemFile(Guid itemId, int number, string name, string? kind = null, long? task = null)
    {
        var f = new WorkItemFile(itemId, number, name, kind, "Bruno", Now);
        f.SetContent(Encoding.UTF8.GetBytes("conteúdo"), "text/plain", task, Now, isNew: true);
        return f;
    }

    private static ExecutionArtifact PlanFile(Guid planId, int number, string name, string? stepKey = null, Guid? noteId = null, string? external = null)
    {
        var a = new ExecutionArtifact(planId, name, null, "Claude", Now, number, noteId);
        a.SetContent(Encoding.UTF8.GetBytes("x"), "text/plain", stepKey, null, Now, isNew: true);
        if (external is not null) a.SetExternalKey(external);
        return a;
    }

    // ---------------------------------------------------------------- listagem (item + plano)

    [Fact]
    public void Listing_has_item_files_first_then_plan_files_with_task_by_step()
    {
        var itemId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var own = new[] { ItemFile(itemId, 2, "chamado-ajuste.md", task: 501), ItemFile(itemId, 1, "01_corrigir.sql") };
        var plan = new[]
        {
            PlanFile(planId, 7, "analise-inicial.md"),
            PlanFile(planId, 5, "02_script.sql", stepKey: "script"),
            PlanFile(planId, 6, "print.png", noteId: Guid.NewGuid()),          // anexo de comentário: fica no comentário
            PlanFile(planId, 4, "evidencia.png", external: "devops:123"),       // evidência do card: não é da entrega
        };
        var taskByStep = new Dictionary<string, long> { ["script"] = 777 };

        var list = WorkItemFileListing.Build(42, own, plan, taskByStep);

        Assert.Equal(["01_corrigir.sql", "chamado-ajuste.md", "02_script.sql", "analise-inicial.md"], list.Select(f => f.Name).ToArray());
        Assert.Equal(["item", "item", "plan", "plan"], list.Select(f => f.Source).ToArray());

        var ticket = list[1];
        Assert.Equal(WorkItemFileKind.Ticket, ticket.Kind);
        Assert.Equal(501, ticket.TaskNumber);
        Assert.Equal($"WorkItems/42/files/{ticket.Id}/content", ticket.ContentUrl);
        Assert.Null(ticket.PlanId);

        var script = list[2];
        Assert.Equal(777, script.TaskNumber);
        Assert.Equal(planId, script.PlanId);
        Assert.Equal("script", script.StepKey);
        Assert.Equal(5, script.Number);
        Assert.Equal($"ExecutionPlan/{planId}/artifacts/{script.Id}/content", script.ContentUrl);

        Assert.Null(list[3].TaskNumber); // sem etapa → do item todo
        Assert.All(list, f => Assert.Equal(42, f.WorkItemNumber));
    }

    [Fact]
    public void Listing_without_plan_has_only_item_files()
    {
        var itemId = Guid.NewGuid();
        var list = WorkItemFileListing.Build(9, [ItemFile(itemId, 1, "texto-pr.txt", WorkItemFileKind.Text)], [], new Dictionary<string, long>());
        var only = Assert.Single(list);
        Assert.Equal("item", only.Source);
        Assert.Equal(WorkItemFileKind.Text, only.Kind);
    }

    // ---------------------------------------------------------------- 0087: arquivos do card inteiro

    private static WorkItem Item(string title, Guid? planId) =>
        new("77988", WorkItemType.Implementation, title, null, WorkItemOrigin.User, null, planId, [], null, "Ana", Now);

    [Fact]
    public void Card_listing_joins_items_plans_and_orphan_plans_without_repeating_a_shared_plan()
    {
        var planA = Guid.NewGuid();
        var legacy = Guid.NewGuid();
        var first = Item("Análise", planA);
        var second = Item("Correção", planA);                    // mesmo plano: os arquivos do plano ficam só no primeiro
        var avulso = Item("Script avulso", null);
        var items = new[] { first, second, avulso };
        var own = new[] { ItemFile(avulso.Id, 1, "script.sql", WorkItemFileKind.Script), ItemFile(second.Id, 1, "chamado.md", WorkItemFileKind.Ticket) };
        var plans = new[]
        {
            PlanFile(planA, 1, "analise.md"),
            PlanFile(legacy, 2, "versao-antiga.md"),               // plano sem item (versão anterior à 0085)
            PlanFile(planA, 3, "print.png", noteId: Guid.NewGuid()) // anexo de comentário: fora
        };

        var list = WorkItemFileListing.BuildForCard(items, own, plans);

        Assert.Equal(4, list.Count);                              // script, chamado, análise e o do plano sem item
        var byName = list.ToDictionary(f => f.Name);
        Assert.Equal(first.Number, byName["analise.md"].WorkItemNumber);
        Assert.Equal(second.Number, byName["chamado.md"].WorkItemNumber);
        Assert.Equal(avulso.Number, byName["script.sql"].WorkItemNumber);
        Assert.Equal(0, byName["versao-antiga.md"].WorkItemNumber);
        Assert.Equal("plan", byName["versao-antiga.md"].Source);
    }

    [Fact]
    public void Card_listing_without_items_or_files_is_empty()
    {
        Assert.Empty(WorkItemFileListing.BuildForCard([], [], []));
    }

    [Theory]
    [InlineData(null, "01_x.sql", WorkItemFileKind.Script)]
    [InlineData(null, "chamado-y.md", WorkItemFileKind.Ticket)]
    [InlineData(null, "analise.md", WorkItemFileKind.Analysis)]
    [InlineData(null, "dados.csv", WorkItemFileKind.Data)]
    [InlineData(null, "print.png", WorkItemFileKind.Attachment)]
    [InlineData("image", "print.png", WorkItemFileKind.Attachment)]
    [InlineData("TEXT", "pr.md", WorkItemFileKind.Text)]
    [InlineData("ticket", "abertura.txt", WorkItemFileKind.Ticket)]
    public void Kind_is_validated_or_inferred(string? kind, string name, string expected) =>
        Assert.Equal(expected, WorkItemFileKind.Normalize(kind, name));

    [Fact]
    public void Invalid_kind_and_empty_or_big_content_are_refused()
    {
        Assert.Throws<DomainException>(() => WorkItemFileKind.Normalize("video", "a.mp4"));
        var f = new WorkItemFile(Guid.NewGuid(), 1, "a.txt", null, "Bruno", Now);
        Assert.Throws<DomainException>(() => f.SetContent([], "text/plain", null, Now, isNew: true));
        Assert.Throws<DomainException>(() => f.SetContent(new byte[WorkItemFile.MaxFileBytes + 1], "text/plain", null, Now, isNew: true));
    }

    [Fact]
    public void Replacing_content_keeps_number_and_task_and_marks_update()
    {
        var f = ItemFile(Guid.NewGuid(), 3, "chamado.md", task: 10);
        f.SetContent(Encoding.UTF8.GetBytes("novo texto"), "text/markdown", null, Now.AddMinutes(5), isNew: false);
        Assert.Equal(3, f.Number);
        Assert.Equal(10, f.TaskNumber);
        Assert.Equal(Now.AddMinutes(5), f.UpdatedAt);
        Assert.Equal("novo texto".Length, f.Size);
    }

    [Fact]
    public void Name_loses_folders()
    {
        var f = new WorkItemFile(Guid.NewGuid(), 1, "scripts/sub/01_x.sql", null, "Bruno", Now);
        Assert.Equal("01_x.sql", f.Name);
    }

    // ---------------------------------------------------------------- áudios fora dos arquivos da conversa (1.7)

    private static CaseFile Case(string name, string contentType, bool fromSpecialist, int number) =>
        new("77988", Guid.NewGuid(), number, name, contentType, [1, 2, 3], fromSpecialist ? "Especialista do caso" : "Bruno", fromSpecialist, Now);

    [Fact]
    public void Person_audio_is_hidden_and_other_files_are_listed()
    {
        var voice = Case("voz-120000.webm", "audio/webm", fromSpecialist: false, 1);
        var image = Case("print.png", "image/png", fromSpecialist: false, 2);
        var doc = Case("log.txt", "text/plain", fromSpecialist: false, 3);
        var specialistAudio = Case("resumo.mp3", "audio/mpeg", fromSpecialist: true, 4);

        Assert.True(voice.Hidden);
        Assert.False(image.Hidden);
        Assert.False(doc.Hidden);
        Assert.False(specialistAudio.Hidden);

        var listed = CaseFile.Listed([voice, image, doc, specialistAudio]).Select(f => f.Number).ToArray();
        Assert.Equal([2, 3, 4], listed);
        Assert.True(voice.ToResponse().Hidden);
    }

    [Fact]
    public void Person_audio_detected_by_extension_is_hidden_too()
    {
        var voice = Case("voz.ogg", "application/octet-stream", fromSpecialist: false, 1);
        Assert.Equal("audio", voice.Kind);
        Assert.True(voice.Hidden);
    }
}
