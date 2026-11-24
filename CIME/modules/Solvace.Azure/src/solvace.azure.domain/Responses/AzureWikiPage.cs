namespace solvace.azure.domain.Models;

/// <summary>0101: uma página de wiki do DevOps (markdown) lida pela integração de quem pede.</summary>
public record AzureWikiPage(string Path, string Content, string? Url);
