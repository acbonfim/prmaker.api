using System.Text.Json;
using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

/// <summary>0100: ambiente da aba AWS (<c>AwsDeployEnvironments</c> do "Skills Configurations").</summary>
public class AwsEnvironmentDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    /// <summary>Tier fixo (dev|qa|edge|rc|rt|prod) — vence a descoberta pelo CloudFront.</summary>
    public string? Tier { get; set; }
    public int Order { get; set; }
}

/// <summary>0100: <c>GET AwsDeploy/config</c>.</summary>
public class AwsDeployConfigResponse
{
    public List<AwsEnvironmentDto> Environments { get; set; } = [];
    public int RefreshFollowMinutes { get; set; }
    public int RefreshFollowMax { get; set; }
}

/// <summary>0100: linha do <c>GET AwsDeploy/pipelines/index</c> (só nome + <c>updated</c> da AWS).</summary>
public class AwsPipelineIndexItem
{
    public string Name { get; set; } = string.Empty;
    public string? Updated { get; set; }
}

public class AwsPipelineIndexResponse
{
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<AwsPipelineIndexItem> Items { get; set; } = [];
}

/// <summary>0100: resultado do <c>POST AwsDeploy/pipelines/index</c>. <see cref="Skipped"/>: nomes recusados (sem nome ou &gt; 16 KB).</summary>
public class AwsPipelineIndexResult
{
    public int Total { get; set; }
    public int Upserted { get; set; }
    public int Removed { get; set; }
    public List<string> Skipped { get; set; } = [];
}

/// <summary>0100: PR do card no contexto da coleta.</summary>
public class AwsContextPr
{
    public int? Number { get; set; }
    public string? Base { get; set; }
    public string? Head { get; set; }
    public string? Status { get; set; }
    public string? Url { get; set; }
}

public class AwsContextRepository
{
    public string Repository { get; set; } = string.Empty;
    public List<string> Branches { get; set; } = [];
    public List<string> Shas { get; set; } = [];
    public List<AwsContextPr> Prs { get; set; } = [];
}

/// <summary>0100: <c>GET AwsDeploy/card/{card}/context</c> — o que o script precisa para achar "o nosso código".</summary>
public class AwsCardContextResponse
{
    public string Card { get; set; } = string.Empty;
    public string? Title { get; set; }
    public List<AwsContextRepository> Repositories { get; set; } = [];
    public List<string> Markers { get; set; } = [];
    public List<AwsEnvironmentDto> Environments { get; set; } = [];
}

/// <summary>0100: pedido de coleta como a tela mostra.</summary>
public class AwsRefreshResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = AwsRefreshStatus.Queued;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public string? Error { get; set; }
    /// <summary>button | follow | task.</summary>
    public string Source { get; set; } = AwsRefreshSource.Button;
    public DateTimeOffset? NotBefore { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public static AwsRefreshResponse From(AwsRefresh r) => new()
    {
        Id = r.Id,
        Status = r.Status,
        RequestedBy = r.RequestedByName,
        RequestedAt = r.CreatedAt,
        Error = r.Error,
        Source = r.Source,
        NotBefore = r.NotBefore,
        FinishedAt = r.FinishedAt,
    };
}

/// <summary>0100: <c>GET AwsDeploy/card/{card}</c>.</summary>
public class AwsCardResponse
{
    /// <summary>O retrato como o script mandou (null = ainda não coletado).</summary>
    public JsonElement? Snapshot { get; set; }
    public DateTimeOffset? CollectedAt { get; set; }
    public string? CollectedBy { get; set; }
    public AwsRefreshResponse? Refresh { get; set; }
    public bool ExecutorOnline { get; set; }
}

/// <summary>0100: <c>POST AwsDeployExecutor/next</c> — o pedido que o executor pegou.</summary>
public class AwsRefreshClaimResponse
{
    public Guid Id { get; set; }
    public string Card { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public int Attempt { get; set; }
    /// <summary>button | follow | task.</summary>
    public string Source { get; set; } = AwsRefreshSource.Button;
}
