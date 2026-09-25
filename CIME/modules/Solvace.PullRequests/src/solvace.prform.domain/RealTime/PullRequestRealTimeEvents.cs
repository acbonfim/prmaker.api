namespace solvace.prform.domain.RealTime;

/// <summary>
/// Contrato de tempo real da tela de Pull Request. Os nomes de grupo/evento são compartilhados
/// com o frontend — mantenha em sincronia com o WsService/RegisterComponent.
/// </summary>
public static class PullRequestRealTimeEvents
{
    /// <summary>Grupo global de quem está na tela de PR.</summary>
    public const string Group = "pullrequest-config";

    /// <summary>Evento emitido quando a configuração do plugin de PR (repositórios/branches) muda.</summary>
    public const string EventConfigUpdated = "pullRequestConfigUpdated";

    /// <summary>
    /// Evento emitido quando o registro do card ou um PR do GitHub dele muda (pela tela, por
    /// outro usuário ou por integrações como a skill gerar-prmake). Payload: { cardNumber, action, id }.
    /// </summary>
    public const string EventCardUpdated = "pullRequestCardUpdated";

    /// <summary>Grupo por card: só quem está com aquele card aberto na tela recebe.</summary>
    public static string CardGroup(string cardNumber) => $"pullrequest:{cardNumber.Trim()}";

    /// <summary>O que mudou — o front decide o que recarregar. Mantenha em sincronia com o RegisterComponent.</summary>
    public static class Actions
    {
        /// <summary>Descrição/root cause/dados do card salvos.</summary>
        public const string RegisterSaved = "register-saved";
        /// <summary>PR aberto no GitHub e registrado (ou PR já existente registrado).</summary>
        public const string GithubPrOpened = "github-pr-opened";
        /// <summary>Título/descrição de um PR atualizados.</summary>
        public const string GithubPrUpdated = "github-pr-updated";
        /// <summary>Status de um ou mais PRs mudou no GitHub (open/merged/closed/draft).</summary>
        public const string GithubPrStatusChanged = "github-pr-status";
        /// <summary>Resumo não técnico publicado na discussion e gravado no card (0011) — só o resumo muda.</summary>
        public const string SummarySaved = "summary-saved";
    }
}
