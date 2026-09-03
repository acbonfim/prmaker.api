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
}
