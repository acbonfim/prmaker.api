namespace solvace.prform.application.UserIntegrations;

/// <summary>
/// O usuário precisa configurar (em "Minhas integrações") os plugins de uso pessoal listados para
/// usar esta função. A API responde 403 com code PERSONAL_INTEGRATION_REQUIRED.
/// </summary>
public class PersonalIntegrationRequiredException : Exception
{
    public const string Code = "PERSONAL_INTEGRATION_REQUIRED";

    public IReadOnlyList<string> Plugins { get; }

    public PersonalIntegrationRequiredException(IReadOnlyList<string> plugins)
        : base($"Configure suas integrações pessoais: {string.Join(", ", plugins)}")
    {
        Plugins = plugins;
    }
}
