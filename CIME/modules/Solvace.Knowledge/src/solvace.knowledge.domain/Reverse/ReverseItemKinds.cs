using System.Text.RegularExpressions;

namespace solvace.knowledge.domain.Reverse;

/// <summary>Tipo de item da engenharia reversa (0052): o prefixo do ID (<c>RN-012</c>) diz o que o item é.</summary>
public sealed record ReverseItemKind(string Prefix, string Label, string Plural);

/// <summary>Prefixos aceitos nos IDs dos itens — contrato entre a skill, o índice e as análises (não mude um prefixo existente).</summary>
public static partial class ReverseItemKinds
{
    public static readonly IReadOnlyList<ReverseItemKind> All =
    [
        new("FN", "Funcionalidade", "Funcionalidades"),
        new("UC", "Caso de uso", "Casos de uso"),
        new("RN", "Regra de negócio", "Regras de negócio"),
        new("PRF", "Perfil / permissão", "Perfis e permissões"),
        new("EST", "Estado / ciclo de vida", "Estados e ciclos de vida"),
        new("NTF", "Notificação", "Notificações"),
        new("CFG", "Configuração / parâmetro", "Configurações e parâmetros"),
        new("REL", "Relatório / indicador", "Relatórios e indicadores"),
        new("TEC", "Tecnologia", "Tecnologias"),
        new("CMP", "Componente", "Componentes"),
        new("API", "Endpoint / contrato", "Endpoints e contratos"),
        new("DB", "Tabela / entidade", "Tabelas e entidades"),
        new("EVT", "Evento / fila", "Eventos e filas"),
        new("JOB", "Job / rotina", "Jobs e rotinas"),
        new("INT", "Integração", "Integrações"),
        new("TELA", "Tela", "Telas"),
        new("FLX", "Fluxo de navegação", "Fluxos de navegação"),
        new("OBJ", "Objetivo / escopo", "Objetivos e escopo"),
        new("PER", "Persona", "Personas"),
        new("GLO", "Termo do glossário", "Glossário"),
        new("ADR", "Decisão de arquitetura", "Decisões de arquitetura"),
        new("NFR", "Requisito não funcional", "Requisitos não funcionais"),
        new("SEQ", "Fluxo de sequência", "Fluxos de sequência"),
        new("UI", "Componente de UI", "Componentes de UI"),
        new("GAP", "Lacuna / débito / risco", "Lacunas, débitos e riscos")
    ];

    public static readonly IReadOnlyDictionary<string, ReverseItemKind> ByPrefix = All.ToDictionary(k => k.Prefix, StringComparer.Ordinal);

    /// <summary>Alternância dos prefixos para as expressões (mais longos primeiro: TELA antes de outros).</summary>
    public static readonly string Alternation = string.Join("|", All.Select(k => k.Prefix).OrderByDescending(p => p.Length));

    public static bool IsKind(string? prefix) => prefix is not null && ByPrefix.ContainsKey(prefix.Trim().ToUpperInvariant());

    /// <summary>ID canônico: prefixo em maiúsculas e número com 3 dígitos no mínimo (<c>rn-7</c> → <c>RN-007</c>).</summary>
    public static string Canonical(string kind, int number) => $"{kind.ToUpperInvariant()}-{number:000}";

    /// <summary>
    /// Lê uma referência: <c>RN-12</c>, <c>revamp-kaizen#RN-012</c> ou <c>revamp-kaizen RN-012</c>. Devolve módulo (ou null)
    /// e o ID canônico; null quando não é referência de item.
    /// </summary>
    public static (string? Module, string Id)? ParseRef(string? value)
    {
        var m = RefPattern().Match((value ?? string.Empty).Trim());
        if (!m.Success) return null;
        var kind = m.Groups["kind"].Value.ToUpperInvariant();
        if (!ByPrefix.ContainsKey(kind) || !int.TryParse(m.Groups["num"].Value, out var n)) return null;
        var module = m.Groups["module"].Success ? m.Groups["module"].Value.ToLowerInvariant() : null;
        return (module, Canonical(kind, n));
    }

    [GeneratedRegex(@"^(?:(?<module>[a-zA-Z0-9][a-zA-Z0-9._-]*)[#\s/])?\s*(?<kind>[A-Za-z]{2,4})-(?<num>\d{1,4})$")]
    private static partial Regex RefPattern();
}
