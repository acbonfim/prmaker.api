namespace solvace.knowledge.application.Contracts;

/// <summary>
/// Quem pode ler a engenharia reversa e o que ela publica na Base Solvace (seções <c>re-*</c>, itens RN/UC/API…, o texto que
/// a "Pergunte" tira dela). Só administradores. Implementado no host (o papel vem do usuário da requisição). Sem
/// implementação registrada (testes, tarefas de fundo) a leitura é livre — a restrição é da requisição.
/// </summary>
public interface IReverseAccess
{
    bool CanRead { get; }
}
