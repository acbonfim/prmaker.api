namespace solvace.executionplans.domain.RealTime;

/// <summary>
/// Contrato de tempo real do plano de execução. Nomes compartilhados com o frontend
/// (ExecutionPlanComponent) — mantenha em sincronia.
/// </summary>
public static class ExecutionPlanRealTimeEvents
{
    /// <summary>Evento emitido quando qualquer coisa do plano de um card muda (sinal; o front refaz o GET).</summary>
    public const string EventPlanUpdated = "executionPlanUpdated";

    /// <summary>Grupo por card: só quem está no card recebe.</summary>
    public static string Group(string cardNumber) => $"execplan:{cardNumber}";

    public static class Actions
    {
        public const string Created = "created";
        public const string Steps = "steps";
        public const string Step = "step";
        public const string Log = "log";
        public const string Status = "status";
        public const string Artifact = "artifact";
    }
}
