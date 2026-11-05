using System.ComponentModel;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.prform.Skills;

namespace solvace.prform.Execution;

/// <summary>
/// 0085 r5 (1.2): arquivos do item de trabalho pelo MCP — gravar o script/chamado/texto de uma entrega no próprio item
/// (<c>prmake_work_item_file</c>) e listar/ler os arquivos do item com os do plano ligado (<c>prmake_work_item_files</c>).
/// </summary>
[McpServerToolType]
public class WorkItemFileMcpTools(
    IWorkItemFileApplication files,
    IHttpContextAccessor http,
    solvace.timeline.application.Contracts.IUserRepository users)
{
    /// <summary>Texto devolvido ao ler um arquivo do item (o resto: baixar pela contentUrl).</summary>
    private const int MaxReadChars = 200_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [McpServerTool(Name = "prmake_work_item_file")]
    [Description("Grava um arquivo de texto no ITEM de trabalho (secao 'Arquivos' do item): script .sql com rollback, texto do chamado " +
                 "(chamado-<nome>.md, kind ticket), analise .md, dados .csv/.json ou texto livre (kind text). Mesmo nome + tipo substitui a " +
                 "versao anterior. taskNumber = a sub-tarefa do item a que o arquivo pertence (opcional). Arquivo do plano continua em prmake_file; " +
                 "use este para o que e da entrega (item avulso, chamado do item, script que o item executa).")]
    public Task<string> SaveFile(string card,
        [Description("Numero do item de trabalho (#n)")] long number,
        [Description("Nome do arquivo com extensao, ex.: 01_corrigir_status.sql, chamado-ajuste.md")] string name,
        [Description("Conteudo completo do arquivo (texto UTF-8)")] string content,
        [Description("script | analysis | ticket | data | attachment | text (padrao: pela extensao; chamado*.md = ticket)")] string? kind = null,
        [Description("Numero da sub-tarefa do item (opcional)")] long? taskNumber = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        if (string.IsNullOrWhiteSpace(name)) throw new McpException("Informe o nome do arquivo (com extensao).");
        if (string.IsNullOrEmpty(content)) throw new McpException("Conteudo vazio.");
        await EnsureCardAsync(card, number, ct);
        var fileName = Path.GetFileName(name.Trim().Replace('\\', '/'));
        var upload = new WorkItemFileUpload(fileName, kind, taskNumber,
            solvace.prform.Controllers.ExecutionPlanController.ResolveContentType(fileName, "text/plain; charset=utf-8"),
            Encoding.UTF8.GetBytes(content));
        var saved = await files.UploadAsync(number, upload, await ActorAsync(ct), ct);
        return Serialize(new { saved.Id, saved.Number, saved.Name, saved.Kind, saved.Size, saved.TaskNumber, saved.ContentUrl });
    });

    [McpServerTool(Name = "prmake_work_item_evidence")]
    [Description("0089: EVIDENCIAS do card no DevOps para o ITEM de trabalho — importa (idempotente) para os arquivos do item os anexos do " +
                 "card e as imagens/arquivos colados na descricao, nos repro steps, nos campos e na discussion (vale com o card sem plano) e " +
                 "lista cada uma com o arquivo do item (#n), a origem e se da para ler. Abra cada legivel com " +
                 "prmake_work_item_files(card, number, file: \"#n\") — imagem volta como imagem.")]
    public Task<string> Evidence(string card,
        [Description("Numero do item de trabalho (#n)")] long number,
        CancellationToken ct = default) => Safe(async () =>
    {
        await EnsureCardAsync(card, number, ct);
        var result = await files.ImportEvidenceAsync(number, await ActorAsync(ct), ct);
        return Serialize(new
        {
            card = result.CardNumber,
            item = result.Number,
            error = result.Error,
            total = result.Items.Count,
            evidences = result.Items.Select(i => new { file = i.File, i.Name, i.Source, i.Description, i.At, i.ContentType, i.Size, i.Status, i.Error, i.Readable }),
            next = result.Items.Any(i => i.File is not null && i.Readable)
                ? "Abra cada evidencia legivel com prmake_work_item_files(card, number, file: \"#n\") e diga no resumo o que ela mostra."
                : null
        });
    });

    [McpServerTool(Name = "prmake_work_item_files", ReadOnly = true)]
    [Description("Arquivos do item de trabalho: os do item (source item, 'arquivo #n') e os do plano ligado (source plan, 'anexo #n', com a " +
                 "sub-tarefa da mesma etapa). Sem 'file' devolve a lista; com 'file' (numero do arquivo do item ou o nome) devolve o texto " +
                 "desse arquivo do item (imagem volta como imagem). Arquivo do plano se le com prmake_attachment (#n ou nome).")]
    public Task<CallToolResult> ListFiles(string card,
        [Description("Numero do item de trabalho (#n)")] long number,
        [Description("Opcional: numero (#n) ou nome de um arquivo DO ITEM para ler o conteudo")] string? file = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var c = await EnsureCardAsync(card, number, ct);
        var list = await files.ListAsync(number, ct);
        if (string.IsNullOrWhiteSpace(file))
            return Text(Serialize(new
            {
                card = c,
                item = number,
                files = list.Select(f => new { f.Source, f.Number, f.Name, f.Kind, f.Size, f.TaskNumber, f.StepKey, f.CreatedBy, f.CreatedAt })
            }));

        var reference = file.Trim().TrimStart('#');
        var own = list.Where(f => f.Source == "item").ToList();
        var found = int.TryParse(reference, out var n)
            ? own.FirstOrDefault(f => f.Number == n)
            : own.FirstOrDefault(f => string.Equals(f.Name, reference, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            var inPlan = list.FirstOrDefault(f => f.Source == "plan" && string.Equals(f.Name, reference, StringComparison.OrdinalIgnoreCase));
            throw new McpException(inPlan is not null
                ? $"'{inPlan.Name}' e arquivo do plano (anexo #{inPlan.Number}) — leia com prmake_attachment."
                : $"Arquivo '{file}' nao encontrado no item #{number}. Arquivos do item: " +
                  (own.Count == 0 ? "nenhum" : string.Join(", ", own.Select(f => $"#{f.Number} {f.Name}"))));
        }
        var data = await files.GetContentAsync(number, found.Id, ct);
        if (IsImage(found.ContentType))
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"arquivo #{found.Number} {found.Name} ({found.ContentType}, {found.Size} bytes)" },
                    ImageContentBlock.FromBytes(data.Data, found.ContentType)]
            };
        if (!IsText(found.ContentType))
            return Text(Serialize(new { found.Number, found.Name, found.Kind, found.Size, found.ContentType, found.ContentUrl, text = (string?)null,
                note = "Arquivo binario — baixe pela contentUrl." }));
        var text = Encoding.UTF8.GetString(data.Data);
        var cut = text.Length > MaxReadChars;
        return Text(Serialize(new
        {
            found.Number, found.Name, found.Kind, found.Size, found.TaskNumber,
            text = cut ? text[..MaxReadChars] : text,
            truncated = cut ? true : (bool?)null
        }));
    });

    // ── Internos ─────────────────────────────────────────────────────────────────────────────────

    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();

    /// <summary>O item precisa ser do card informado (e, com o token do caso, do card do caso).</summary>
    private async Task<string> EnsureCardAsync(string card, long number, CancellationToken ct)
    {
        var c = SkillsRulesService.NormalizeCard(card);
        if (c.Length == 0) throw new McpException("Informe o card.");
        if (CaseClaims.SpecialistOf(User) is not null && CaseClaims.CardOf(User) is { } own && !string.Equals(own.TrimStart('#'), c, StringComparison.Ordinal))
            throw new McpException("O Especialista do caso so mexe nos itens do card dele.");
        var itemCard = await files.CardOfAsync(number, ct);
        if (!string.Equals(itemCard, c, StringComparison.Ordinal))
            throw new McpException($"O item #{number} e do card {itemCard}, nao do {c}.");
        return c;
    }

    private async Task<ExecutionActor> ActorAsync(CancellationToken ct)
    {
        if (CaseClaims.SpecialistOf(User) is { } specialist)
            return specialist.AsExecutionActor();
        Guid? userId = Guid.TryParse(User.FindFirst("ExternalId")?.Value, out var id) ? id : null;
        string? name = userId.HasValue ? await users.GetFullNameAsync(userId.Value, ct) : null;
        name ??= User.FindFirst(ClaimTypes.Name)?.Value;
        return new ExecutionActor(userId, string.IsNullOrWhiteSpace(name) ? "Claude" : name.Trim(), true);
    }

    private static bool IsImage(string contentType) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && !contentType.Contains("svg", StringComparison.OrdinalIgnoreCase);

    private static CallToolResult Text(string json) => new() { Content = [new TextContentBlock { Text = json }] };

    private static bool IsText(string contentType) =>
        contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
        || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
        || contentType.Contains("yaml", StringComparison.OrdinalIgnoreCase);

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    /// <summary>Erro de regra (não encontrado, limite, recusa da fase) vai com a mensagem para o Claude.</summary>
    private static async Task<T> Safe<T>(Func<Task<T>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception e) when (e is DomainException or ExecutionPlanNotFoundException or ExecutionForbiddenException
                                       or ExecutionPlanConcurrencyException or ArgumentException or InvalidOperationException)
        {
            throw new McpException(e.Message, e);
        }
    }
}
