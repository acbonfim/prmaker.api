using solvace.governance.domain.Entities;

namespace solvace.governance.domain;

/// <summary>
/// 0098: regras e hooks embutidos — o que antes era fixo no <c>SkillsRulesService.FixedRules</c> e no <c>Guard.cs</c> do
/// executor. Semeados no <c>--migrate</c> (só os que faltam, pela chave; nunca sobrescreve o que o admin mudou).
/// </summary>
public static class GovernanceDefaults
{
    private const string Protected = @"(master|main|develop|dev|homolog\S*|release\S*|production|prod|staging)";

    public sealed record RuleDef(string Key, string Name, string Category, string Effect, int Order, string Message,
        string Description, RuleMatch Match, RuleScope? Scope = null, bool Enabled = true);

    public sealed record HookDef(string Key, string Name, string Description, string Event, string Matcher, string Action,
        bool Enabled = true, Dictionary<string, string>? Config = null);

    public static readonly IReadOnlyList<RuleDef> Rules =
    [
        new("no-merge", "Merge e aprovação de PR só por uma pessoa", "git", GovernanceEffect.Block, 10,
            "Merge e aprovação de PR são sempre de uma pessoa — o Claude só abre PRs.",
            "Barra gh pr merge/review, merge pela API do GitHub e merge seguido de push.",
            new RuleMatch
            {
                Tools = ["Bash"],
                Patterns = [@"\bgh\s+pr\s+(merge|review)\b", @"\bgh\s+api\b[^|;&]*/pulls/\d+/(merge|reviews)\b", @"\bgit\s+merge\s+--no-ff\s+\S+\s*&&\s*git\s+push"]
            }),
        new("no-force-push", "Sem push forçado", "git", GovernanceEffect.Block, 20,
            "Push forçado não é permitido.",
            "Barra git push --force, -f e --force-with-lease.",
            new RuleMatch { Tools = ["Bash"], Patterns = [@"\bgit\s+push\b[^|;&]*\s(--force(-with-lease)?|-f)\b"] }),
        new("no-push-protected", "Sem push em branch protegida", "git", GovernanceEffect.Block, 30,
            "Push direto em branch protegida (master/main/develop/release...) não é permitido — use a branch de correção e abra PR.",
            "Barra git push <remoto> <branch protegida>.",
            new RuleMatch { Tools = ["Bash"], Patterns = [@"\bgit\s+push\b[^|;&]*\s\S+\s+(\+?(refs/heads/)?" + Protected + @"|\S*:(refs/heads/)?" + Protected + @")(\s|$|[;&|])"] }),
        new("writes-through-prmake", "Gravações sempre pelo PRMake", "integracoes", GovernanceEffect.Block, 40,
            "Toda gravação no DevOps/GitHub/Teams passa pelos endpoints do PRMake (prmake-plan.sh ou MCP) — nunca direto na API.",
            "Barra chamadas de escrita diretas às APIs do Azure DevOps, GitHub e Microsoft Graph.",
            new RuleMatch
            {
                Tools = ["Bash"],
                Patterns = [@"(dev\.azure\.com|visualstudio\.com|api\.github\.com|graph\.microsoft\.com)"],
                AllOf = [@"(-X\s*(POST|PUT|PATCH|DELETE)|--request\s+(POST|PUT|PATCH|DELETE)|--method\s+(POST|PUT|PATCH|DELETE)|\baz\s+(boards|repos)\s+\S+\s+(create|update|delete))"]
            }),
        new("no-db-writes", "Banco somente leitura", "dados", GovernanceEffect.Block, 50,
            "Escrita em banco não é permitida — análise e correção são somente leitura no banco; script de alteração vai para o chamado.",
            "Barra insert/update/delete/drop/alter/truncate/merge/exec em clientes de banco.",
            new RuleMatch
            {
                Tools = ["Bash"],
                Patterns = [@"\b(sqlcmd|psql|mysql|sql-query\.(sh|py)|mongosh?)\b"],
                AllOf = [@"\b(insert\s+into|update\s+\S+\s+set|delete\s+from|drop\s+(table|database|schema)|truncate\s+|alter\s+table|merge\s+into|exec(ute)?\s+sp_)"]
            }),
        new("no-cognito-writes", "Cognito somente leitura", "dados", GovernanceEffect.Block, 60,
            "Alteração no Cognito não é permitida.",
            "Barra comandos aws cognito-idp que criam, alteram ou apagam.",
            new RuleMatch { Tools = ["Bash"], Patterns = [@"\baws\s+cognito-idp\s+(admin-(create|delete|update|set|disable|enable|reset|add|remove|confirm|link)|sign-up|delete|update|create|set)"] }),
        new("protect-credentials", "Credenciais protegidas", "credenciais", GovernanceEffect.Block, 70,
            "O arquivo de senhas dos ambientes só é lido pelo navegador da validação (env.sh) — nunca abra, copie ou mostre a senha.",
            "Barra comandos que leem/copiam o arquivo de senhas dos ambientes (as ferramentas de arquivo ficam com protect-credentials-files).",
            new RuleMatch { Tools = ["Bash"], Patterns = [@"solvace-env-credentials"] }),
        new("protect-credentials-files", "Arquivos de credenciais protegidos", "credenciais", GovernanceEffect.Block, 71,
            "Arquivos de credenciais não são lidos nem alterados pelo Claude.",
            "Barra Read/Edit/Write em arquivos de credenciais conhecidos.",
            new RuleMatch { Tools = ["Read|Edit|Write|MultiEdit|NotebookEdit"], Paths = ["**/solvace-env-credentials.json", "**/.aws/credentials", "**/.prmake/db-credentials*"] }),
        new("no-destructive-rm", "Sem comando destrutivo", "git", GovernanceEffect.Block, 80,
            "Comando destrutivo não é permitido.",
            "Barra rm -rf na raiz ou na pasta do usuário.",
            new RuleMatch { Tools = ["Bash"], Patterns = [@"\brm\s+-(rf|fr)\s+(/|~|\$HOME)(\s|$)"] }),
        new("specialist-read-only", "Especialista do caso não altera código", "fluxo", GovernanceEffect.Block, 90,
            "O Especialista do caso não usa o token pessoal nem altera código/PR — proponha a mudança na conversa (prmake_case_propose).",
            "No especialista: barra token pessoal, commit, push e criação/edição de PR.",
            new RuleMatch { Tools = ["Bash"], Patterns = [@"prmake-token|\bgit\s+(push|commit)\b|\bgh\s+pr\s+(create|edit|close)\b"] },
            new RuleScope { Sources = [GovernanceSource.Specialist] }),
        new("config-changes-audited", "Mudança de configuração auditada", "fluxo", GovernanceEffect.Monitor, 100,
            "Mudança de configuração registrada na governança.",
            "Registra toda alteração de configuração de plugins/regras (só monitora).",
            new RuleMatch { Actions = ["PATCH PluginConfiguration/settings", "* Governance/rules*", "* Governance/hooks*", "PUT Governance/settings", "* PluginConfiguration*"] }),
        new("card-reset-audited", "Reset de card auditado", "fluxo", GovernanceEffect.Warn, 110,
            "O reset apaga planos e itens do card — confira se é isso mesmo.",
            "Avisa quando alguém reseta um card.",
            new RuleMatch { Actions = ["POST ExecutionPlan/card/*/reset"] })
    ];

    public static readonly IReadOnlyList<HookDef> Hooks =
    [
        new("claude-session", "Sessão do Claude Code", "Registra o início de cada sessão (máquina, pasta, card da branch).",
            "claude:SessionStart", "", GovernanceHookAction.Record),
        new("claude-prompt", "Pedido do usuário", "Registra que houve um pedido (só o tamanho e o card — nunca o texto).",
            "claude:UserPromptSubmit", "", GovernanceHookAction.Record),
        new("claude-pretool", "Guardrails antes de cada ferramenta", "Consulta as regras antes do comando/ferramenta; barra ou avisa.",
            "claude:PreToolUse", "Bash|Read|Edit|Write|MultiEdit|NotebookEdit", GovernanceHookAction.Evaluate),
        new("claude-posttool", "Ferramentas que gravam", "Registra comandos, edições e chamadas ao PRMake com a duração.",
            "claude:PostToolUse", "Bash|Write|Edit|MultiEdit|mcp__prmake__*", GovernanceHookAction.Record),
        new("claude-stop", "Fim do turno", "Registra o fim de cada resposta do Claude (duração da sessão).",
            "claude:Stop", "", GovernanceHookAction.Record),
        new("server-blocked-timeline", "Barrado → Timeline do card", "Anota na Timeline do card quando a governança barra uma ação.",
            "server:*", "blocked", GovernanceHookAction.Timeline, Enabled: false,
            Config: new() { ["template"] = "Governança barrou \"{action}\" de {user}: {reason}" })
    ];

    public static GovernanceRule CreateRule(RuleDef d, DateTimeOffset now)
    {
        var rule = new GovernanceRule(Guid.NewGuid(), d.Key, builtin: true);
        rule.Update(d.Name, d.Description, d.Category, d.Enabled, d.Effect, d.Order, d.Message, d.Scope, d.Match, "Sistema", now);
        return rule;
    }

    public static GovernanceHook CreateHook(HookDef d, int order, DateTimeOffset now)
    {
        var hook = new GovernanceHook(Guid.NewGuid(), d.Key, builtin: true);
        hook.Update(d.Name, d.Description, d.Event, d.Matcher, d.Action, d.Config, d.Enabled, order, "Sistema", now);
        return hook;
    }
}
