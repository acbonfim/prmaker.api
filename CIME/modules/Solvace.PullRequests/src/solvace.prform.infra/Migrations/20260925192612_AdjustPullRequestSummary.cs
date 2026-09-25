using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// Ajustes da feature 0011:
    /// - Datas do resumo não técnico (<c>SummaryUpdatedAt</c> / <c>SummaryPublishedAt</c>): o resumo pode
    ///   ser salvo no PRMake sem publicar, e a tela mostra se há alterações não publicadas.
    /// - Novo prompt padrão do resumo (<c>BugSummaryPrompt</c>): usa o contexto completo do card
    ///   (<c>{context}</c>) e proíbe inventar uma correção que o contexto não mostra. Só troca se o
    ///   valor ainda for o padrão anterior (não sobrescreve prompt editado pelo admin).
    /// </summary>
    public partial class AdjustPullRequestSummary : Migration
    {
        private const string PluginName = "AI Configurations";

        // Padrão gravado pela AddDevOpsActionsToAIConfigurations (cópia exata).
        private const string OldSummaryPrompt =
            "Você é um redator que explica correções de software para usuários de negócio.\n\n" +
            "Escreva um resumo NÃO técnico do card {cardNumber} ({title}), com um panorama do problema e da solução adotada, " +
            "em linguagem de negócio/usual: sem jargão técnico, sem nomes de arquivos, métodos, classes, tabelas ou código. " +
            "Foque no impacto para o usuário e no que passou a funcionar.\n\n" +
            "Problema reportado:\n{reproSteps}\n\n" +
            "Descrição técnica do Pull Request:\n{description}\n\n" +
            "Root cause (técnico):\n{rootCause}\n\n" +
            "Responda SOMENTE com o texto final em Markdown, exatamente neste formato (dois blocos com o mesmo conteúdo):\n\n" +
            "**PT**\n\n---\n\n<texto em português do Brasil>\n\n**EN**\n\n---\n\n<same text in en-US>\n\n" +
            "Regras: parágrafos curtos; explique o que acontecia de errado e o que muda para o usuário agora; " +
            "não mencione que o texto foi gerado por IA.";

        private const string NewSummaryPrompt =
            "Você escreve, para pessoas de negócio, um resumo NÃO técnico do card {cardNumber} do Azure DevOps.\n\n" +
            "REGRAS DE VERACIDADE (obrigatórias):\n" +
            "- Use SOMENTE as informações do CONTEXTO abaixo. Não invente fatos, causas, correções, datas nem resultados.\n" +
            "- Só diga que o problema foi corrigido/resolvido se o CONTEXTO trouxer evidência da solução: Pull Request, descrição do PR, root cause ou alterações de código. " +
            "Sem essa evidência, descreva apenas o problema relatado e diga que ele está em análise pela equipe.\n" +
            "- Respeite a situação atual do card (estado, área) informada no CONTEXTO.\n" +
            "- Se uma informação não estiver no CONTEXTO, não a mencione.\n\n" +
            "ESTILO: linguagem de negócio/usual, sem jargão técnico, sem nomes de arquivos, métodos, classes, tabelas, branches ou código; " +
            "foque no impacto para o usuário; parágrafos curtos; não mencione que o texto foi gerado por IA.\n\n" +
            "FORMATO: responda SOMENTE com o texto final em Markdown, exatamente assim (dois blocos com o mesmo conteúdo):\n\n" +
            "**PT**\n\n---\n\n<texto em português do Brasil>\n\n**EN**\n\n---\n\n<same text in en-US>\n\n" +
            "CONTEXTO:\n{context}";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SummaryPublishedAt",
                table: "PullRequests",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SummaryUpdatedAt",
                table: "PullRequests",
                type: "datetime(6)",
                nullable: true);

            // Resumos já existentes foram gravados publicando (versão anterior): salvo = publicado.
            migrationBuilder.Sql(@"
UPDATE `PullRequests`
SET `SummaryUpdatedAt` = COALESCE(`UpdatedAt`, UTC_TIMESTAMP(6)), `SummaryPublishedAt` = COALESCE(`UpdatedAt`, UTC_TIMESTAMP(6))
WHERE `Summary` IS NOT NULL AND `SummaryCommentId` IS NOT NULL;");

            migrationBuilder.Sql(ReplacePromptSql(OldSummaryPrompt, NewSummaryPrompt));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ReplacePromptSql(NewSummaryPrompt, OldSummaryPrompt));

            migrationBuilder.DropColumn(
                name: "SummaryPublishedAt",
                table: "PullRequests");

            migrationBuilder.DropColumn(
                name: "SummaryUpdatedAt",
                table: "PullRequests");
        }

        private static string ReplacePromptSql(string from, string to) => $@"
UPDATE `PluginConfigurations` c
JOIN `Plugins` p ON p.`Id` = c.`PluginId`
SET c.`Options` = JSON_SET(c.`Options`, '$[0].BugSummaryPrompt', {Literal(to)})
WHERE p.`Description` = {Literal(PluginName)} AND p.`IsDeleted` = 0
  AND c.`Options` IS NOT NULL AND JSON_VALID(c.`Options`)
  AND JSON_UNQUOTE(JSON_EXTRACT(c.`Options`, '$[0].BugSummaryPrompt')) = {Literal(from)};";

        /// <summary>Literal de string MySQL: escapa barra invertida e aspas.</summary>
        private static string Literal(string value) =>
            "'" + value.Replace("\\", "\\\\").Replace("'", "''") + "'";
    }
}
