namespace solvace.executionplans.domain.Requests;

/// <summary>
/// 0100: pipeline do índice da AWS (JSON camelCase; ≤ 16 KB cada). <see cref="Updated"/> é o <c>updated</c> do
/// <c>list-pipelines</c> como texto — o servidor devolve exatamente como recebeu, para o script comparar.
/// </summary>
public class AwsPipelineDto
{
    public string Name { get; set; } = string.Empty;
    public string? Updated { get; set; }
    /// <summary>V1 | V2.</summary>
    public string? Type { get; set; }
    public List<AwsPipelineRepositoryDto> Repositories { get; set; } = [];
    public List<string> FilePathsInclude { get; set; } = [];
    public List<string> FilePathsExclude { get; set; } = [];
    public string? Tier { get; set; }
    public string? CodebuildEnv { get; set; }
    public List<AwsDeployTargetDto> DeployTargets { get; set; } = [];
    public List<string> Stages { get; set; } = [];
}

/// <summary>0100: fonte da pipeline (<c>FullRepositoryId</c> + <c>BranchName</c> da ação de fonte).</summary>
public class AwsPipelineRepositoryDto
{
    public string? Repository { get; set; }
    public string? Branch { get; set; }
    public string? Action { get; set; }
}

/// <summary>0100: ação de deploy da pipeline (Elastic Beanstalk, S3, ECS, CodeBuild...).</summary>
public class AwsDeployTargetDto
{
    public string? Action { get; set; }
    public string? Stage { get; set; }
    public string? Provider { get; set; }
    public string? Target { get; set; }
}

/// <summary>0100: <c>POST AwsDeploy/pipelines/index</c> — até 1500 por vez; <c>full</c> remove o que não veio.</summary>
public class AwsPipelineIndexRequest
{
    public string? Account { get; set; }
    public string? Region { get; set; }
    public List<AwsPipelineDto> Upserts { get; set; } = [];
    public List<string> Removed { get; set; } = [];
    public bool Full { get; set; }
}

/// <summary>0100: <c>POST AwsDeployExecutor/{id}/report</c>.</summary>
public class AwsRefreshReportRequest
{
    /// <summary>done | failed.</summary>
    public string? Status { get; set; }
    public string? Error { get; set; }
    public long? DurationMs { get; set; }
}
