namespace solvace.github.domain.Responses;

/// <summary>0074: commits que uma branch tem a mais que a base (compare base...head), do mais antigo para o mais novo.</summary>
public class BranchDeltaResponse
{
    public string Base { get; set; } = string.Empty;
    public string Head { get; set; } = string.Empty;
    /// <summary>SHA do ancestral comum — o ponto de corte da branch.</summary>
    public string? MergeBaseSha { get; set; }
    /// <summary>Quantos commits a branch tem a mais que a base (inclui merges).</summary>
    public int AheadBy { get; set; }
    public int BehindBy { get; set; }
    /// <summary>Commits sem os merges (o que o cherry-pick leva). A API do GitHub devolve no máximo 250.</summary>
    public List<BranchDeltaCommit> Commits { get; set; } = [];
    /// <summary>true quando há mais commits que os devolvidos pela API (a lista está cortada).</summary>
    public bool Truncated { get; set; }
}

public class BranchDeltaCommit
{
    public string Sha { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
