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

    /// <summary>
    /// Grupo global das pendências do usuário (0037): sinal de que as pendências de um plano podem ter mudado.
    /// Payload: { cardNumber, planId, userId } — o front só refaz <c>GET ExecutionPlan/pending</c> se o plano for dele.
    /// </summary>
    public const string PendingGroup = "execplan-pending";

    public const string EventPendingChanged = "executionPlanPendingChanged";

    /// <summary>
    /// Executores ou pedidos de um usuário mudaram (0039), no grupo <see cref="PendingGroup"/>. Payload: { userId } —
    /// a tela "Meus executores" só recarrega se for dela.
    /// </summary>
    public const string EventWorkersChanged = "executionWorkersChanged";

    /// <summary>
    /// 0068: pode haver pedido para os executores do usuário — o <c>prmake-agent</c> conectado ao relay consulta a fila
    /// (<c>GET ExecutionQueue/next</c>). Só um sinal, sem payload: a fila em si só sai pela credencial do executor.
    /// </summary>
    public const string EventQueueReady = "executionQueueReady";

    /// <summary>0068: grupo dos executores de um usuário (o executor entra com o token de <c>GET ExecutionWorker/realtime</c>).</summary>
    public static string WorkerGroup(Guid ownerUserId) => $"execworkers:{ownerUserId}";

    /// <summary>
    /// 0080: a conversa do caso (Especialista do caso) mudou — no mesmo grupo do card. Payload:
    /// <c>{ cardNumber, action, number?, requestId?, status?, activity?, recent?, by?, userId? }</c>.
    /// </summary>
    public const string EventCaseUpdated = "caseChatUpdated";

    /// <summary>
    /// 0085: itens de trabalho do card mudaram — no mesmo grupo do card. Payload:
    /// <c>{ cardNumber, number, taskNumber?, action, status }</c>. É só sinal: a tela refaz <c>GET WorkItems/card/{card}</c>.
    /// </summary>
    public const string EventWorkItemsUpdated = "workItemsUpdated";

    public static class WorkItemActions
    {
        public const string Created = "created";
        public const string Updated = "updated";
        public const string Task = "task";
        public const string Status = "status";
        public const string Request = "request";
        public const string Activity = "activity";
        public const string Deleted = "deleted";
        /// <summary>0085 r5: arquivo gravado no item (<c>WorkItems/{n}/files</c>, <c>prmake_work_item_file</c>).</summary>
        public const string File = "file";
        /// <summary>0091: entrega gravada (PR, código, chamado...) — a tela recarrega o item aberto.</summary>
        public const string Deliverable = "deliverable";
    }

    public static class CaseActions
    {
        public const string Message = "message";
        public const string Edited = "edited";
        public const string Deleted = "deleted";
        /// <summary>0090: alguém limpou a conversa (<c>/clear</c>).</summary>
        public const string Cleared = "cleared";
        public const string Typing = "typing";
        /// <summary>Pedido do especialista mudou (na fila, rodando, terminou).</summary>
        public const string Run = "run";
        /// <summary>O que o especialista está fazendo agora (payload com activity/recent).</summary>
        public const string Activity = "activity";
        public const string Point = "point";
        public const string Proposal = "proposal";
        public const string Action = "action";
    }

    public static class Actions
    {
        public const string Created = "created";
        public const string Steps = "steps";
        public const string Step = "step";
        public const string Log = "log";
        public const string Status = "status";
        public const string Artifact = "artifact";
        public const string Question = "question";
        public const string Link = "link";
        public const string Note = "note";
        /// <summary>Pedido de execução do card mudou (0039).</summary>
        public const string Request = "request";
        /// <summary>
        /// 0050: o Claude começou outra atividade. Payload traz <c>activity { label, tool, at }</c> — a tela atualiza
        /// a linha "agora" sem refazer o GET do plano.
        /// </summary>
        public const string Activity = "activity";
        /// <summary>0075: fotografias de código do card mudaram (a tela refaz <c>GET card/{card}/code-refs</c>).</summary>
        public const string CodeRef = "codeRef";
        /// <summary>0075: a situação do card (o que está sendo aguardado) ou o contexto da passagem mudou.</summary>
        public const string Situation = "situation";
        /// <summary>0078: o assistente do card tem sugestão nova ou respondida.</summary>
        public const string Suggestion = "suggestion";
    }
}
