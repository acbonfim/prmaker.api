namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0075: arquivo do card pelo nome — a imagem <c>![x](anexo:&lt;nome&gt;)</c> do texto. Primeiro os arquivos dos planos
/// (arquivo da skill antes de anexo de comentário, o mais recente); sem nenhum com o nome, os arquivos dos itens de
/// trabalho do card (o card sem plano grava só neles), o mais recente. Nome sem diferenciar maiúsculas. Só metadados:
/// quem chama lê o conteúdo apenas do escolhido.
/// </summary>
public static class CardFileByName
{
    public static ExecutionArtifact? PickArtifact(IEnumerable<ExecutionArtifact> artifacts, string? name)
    {
        var wanted = Normalize(name);
        return wanted.Length == 0
            ? null
            : artifacts.Where(a => string.Equals(a.Name, wanted, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.NoteId is null ? 0 : 1)
                .ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt)
                .FirstOrDefault();
    }

    public static WorkItemFile? PickWorkItemFile(IEnumerable<WorkItemFile> files, string? name)
    {
        var wanted = Normalize(name);
        return wanted.Length == 0
            ? null
            : files.Where(f => string.Equals(f.Name, wanted, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt)
                .ThenByDescending(f => f.Number)
                .FirstOrDefault();
    }

    public static string Normalize(string? name) => (name ?? string.Empty).Trim();
}
