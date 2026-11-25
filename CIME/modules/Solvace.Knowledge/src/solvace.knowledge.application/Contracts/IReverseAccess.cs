namespace solvace.knowledge.application.Contracts;

/// <summary>
/// Quem pode ler a engenharia reversa e o que ela publica na Base Solvace (seções <c>re-*</c>, itens RN/UC/API…, o texto que
/// a "Pergunte" tira dela). 0097: todo usuário autenticado (0074: só administradores). Implementado no host (vem do usuário
/// da requisição). Sem implementação registrada (testes, tarefas de fundo) a leitura é livre — a restrição é da requisição.
/// </summary>
public interface IReverseAccess
{
    bool CanRead { get; }
}
