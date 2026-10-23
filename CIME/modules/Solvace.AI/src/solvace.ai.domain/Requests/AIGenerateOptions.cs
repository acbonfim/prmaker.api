namespace solvace.ai.domain.Requests;

/// <summary>
/// Ajustes de UMA chamada de IA, sobre a configuração do plugin (0071). <see cref="Effort"/>: low | medium | high | max — só o
/// Claude usa; perguntas curtas (plano de busca, resposta do Pergunte) não precisam do raciocínio longo do padrão.
/// </summary>
public sealed record AIGenerateOptions(string? Effort = null);
