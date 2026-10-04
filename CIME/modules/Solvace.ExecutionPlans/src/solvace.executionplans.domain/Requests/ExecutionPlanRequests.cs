namespace solvace.executionplans.domain.Requests;

/// <summary>Definição de uma etapa enviada pela skill (upsert pela <see cref="Key"/>).</summary>
public class ExecutionStepDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Quem executa: "claude" (padrão) ou "user" (0024).</summary>
    public string? Executor { get; set; }

    /// <summary>task (padrão) | code | pr | ticket | question | validation (0024).</summary>
    public string? Kind { get; set; }

    /// <summary>Repositório da etapa (código/PR), ex.: "edv-solvace" (0024).</summary>
    public string? Repository { get; set; }

    /// <summary>Keys das etapas que precisam terminar antes desta (0024).</summary>
    public List<string>? DependsOn { get; set; }
}

public class CreateExecutionPlanRequest
{
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>Quem gerou o plano (ex.: "analisar-bug").</summary>
    public string Kind { get; set; } = "analisar-bug";

    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public List<ExecutionStepDefinition> Steps { get; set; } = [];

    /// <summary>"analysis" (padrão) ou "correction" (0024).</summary>
    public string? Phase { get; set; }

    /// <summary>Plano de análise que originou este plano de correção (0024).</summary>
    public Guid? ParentPlanId { get; set; }
}

public class UpsertExecutionStepsRequest
{
    public List<ExecutionStepDefinition> Steps { get; set; } = [];
}

/// <summary>Atualização parcial de uma etapa: só os campos informados mudam.</summary>
public class UpdateExecutionStepRequest
{
    public string? Status { get; set; }
    public string? Reason { get; set; }

    /// <summary>
    /// Com <c>status: "waiting"</c>: de quem a etapa depende — <c>user</c> (pendência do usuário; <c>reason</c> diz o
    /// que ele precisa fazer) ou <c>external</c> (padrão) — 0037.
    /// </summary>
    public string? WaitingOn { get; set; }
    public string? Activity { get; set; }
    public string? Checkpoint { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    /// <summary>
    /// 0052: resumo do <c>advance</c> que conclui a etapa (não é gravado aqui — vai no log) — a trava da engenharia
    /// reversa procura nele os itens citados (RN-…, UC-…) ou "lacuna".
    /// </summary>
    public string? Message { get; set; }
}

public class CancelExecutionStepRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class ExecutionLogItem
{
    /// <summary>Id gerado por quem envia: reenviar o mesmo pedaço (fila local da skill) não duplica.</summary>
    public string? ClientId { get; set; }
    public string? StepKey { get; set; }
    public string? Kind { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class AppendExecutionLogsRequest
{
    public List<ExecutionLogItem> Logs { get; set; } = [];
}

public class ChangeExecutionPlanStatusRequest
{
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }

    /// <summary>Resumo final (opcional; a skill manda ao concluir).</summary>
    public string? Summary { get; set; }
}

/// <summary>Opção de resposta de uma pergunta (0024).</summary>
public class ExecutionQuestionOption
{
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Recommended { get; set; }
}

public class ExecutionQuestionItem
{
    /// <summary>Etapa que espera a resposta (fica "aguardando" até responderem).</summary>
    public string? StepKey { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<ExecutionQuestionOption>? Options { get; set; }
    public bool AllowFreeText { get; set; } = true;
}

public class AskExecutionQuestionsRequest
{
    public List<ExecutionQuestionItem> Questions { get; set; } = [];
}

public class AnswerExecutionQuestionRequest
{
    public string Answer { get; set; } = string.Empty;
}

public class AddExecutionLinkRequest
{
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }

    /// <summary>ticket | pr | doc | other — vazio: pela URL.</summary>
    public string? Kind { get; set; }

    /// <summary>A etapa fica "aguardando" enquanto este chamado estiver aberto.</summary>
    public bool BlocksStep { get; set; }

    /// <summary>PR: número, repositório e branch de destino (a skill informa ao abrir o PR pelo PRMake).</summary>
    public int? PullRequestNumber { get; set; }
    public string? Repository { get; set; }
    public string? TargetBranch { get; set; }
}

public class UpdateExecutionLinkRequest
{
    /// <summary>Chamado: open | resolved | closed (marcado à mão).</summary>
    public string? Status { get; set; }
    public string? Title { get; set; }
}

/// <summary>Usuário inicia/conclui uma etapa pela tela (0024).</summary>
public class ExecutionStepActionRequest
{
    public string? Reason { get; set; }
}

/// <summary>Editar o texto de um comentário (0031).</summary>
public class EditExecutionNoteRequest
{
    public string? Text { get; set; }
}

/// <summary>Sessão do Claude Code que está trabalhando no plano (0033).</summary>
public class RegisterExecutionSessionRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string? Host { get; set; }
    public string? Cwd { get; set; }
}

/// <summary>Custo acumulado de uma sessão (totais lidos do transcript pela skill).</summary>
public class RecordExecutionUsageRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string? Host { get; set; }
    public int Turns { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public string? Model { get; set; }
    /// <summary>0041: chamadas pelo MCP e pelo script na sessão (medição).</summary>
    public int? McpCalls { get; set; }
    public int? ScriptCalls { get; set; }
    /// <summary>0045: consultas à Base Solvace (kb.sh / espelho) e buscas no código (grep/find/Grep/Glob) na sessão.</summary>
    public int? KbCalls { get; set; }
    public int? SearchCalls { get; set; }
    /// <summary>0047: o mesmo consumo separado por modelo (acumulado na sessão).</summary>
    public List<ExecutionModelTokens>? Models { get; set; }
    /// <summary>0055: de onde a sessão leu (re | base | code-confirm | code-explore | code-search) — chamadas e tokens estimados.</summary>
    public List<ExecutionReadSourceDto>? Sources { get; set; }
    /// <summary>0055: arquivos de código lidos sem item da engenharia reversa que os cite (os que mais pesaram).</summary>
    public List<ExecutionExploredFileDto>? ExploredFiles { get; set; }
}

/// <summary>0055: leituras de uma origem — chamadas e tokens estimados do que entrou no contexto.</summary>
public class ExecutionReadSourceDto
{
    public string Key { get; set; } = string.Empty;
    public int Calls { get; set; }
    public long Tokens { get; set; }
}

/// <summary>0055: arquivo de código explorado (sem item da engenharia reversa que o cite).</summary>
public class ExecutionExploredFileDto
{
    public string Path { get; set; } = string.Empty;
    public int Reads { get; set; }
    public long Tokens { get; set; }
}

/// <summary>Consumo em um modelo (0047): respostas, entrada nova, saída, cache lido e cache escrito.</summary>
public class ExecutionModelTokens
{
    public string Model { get; set; } = string.Empty;
    public int Turns { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
}
