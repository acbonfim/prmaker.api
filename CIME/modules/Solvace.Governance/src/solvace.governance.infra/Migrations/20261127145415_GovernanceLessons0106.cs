using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace solvace.governance.infra.Migrations
{
    /// <summary>
    /// 0106: lições aprendidas e sinais de erro. Semeia as lições do card 79047 (origem <c>seed</c>) — o que deu errado ali
    /// e não deve se repetir; dali em diante os agentes gravam as próprias.
    /// </summary>
    public partial class GovernanceLessons0106 : Migration
    {
        /// <summary>(escopo, quando, lição, por quê, referência).</summary>
        public static readonly (string Scope, string Trigger, string Text, string Why, string Ref)[] Seed =
        [
            ("[\"especialista-do-caso\"]", "a pessoa pede a análise inicial do card (bug ou User Story)",
             "inicie a análise do padrão com prmake_analysis_start (o mesmo botão da Bancada); nunca monte um item de análise à mão",
             "Card 79047: o especialista montou o #1174 à mão, fora do padrão da análise, e a pessoa cancelou.", "#1174"),
            ("[]", "a pessoa pede para ler ou avaliar o review, os comentários ou o pedido de mudanças de um PR",
             "leia com prmake_pr_reviews — os repositórios dos cards ficam no GitHub, não no Azure DevOps; nunca diga que não consegue sem chamar a ferramenta",
             "Card 79047: o especialista disse que não lia os comentários do PR 31430 porque os repositórios ficariam no Azure DevOps.", "msg 20"),
            ("[]", "avaliar um pedido de mudanças (request changes) ou revisar um PR do card",
             "avalie só o código que o PR muda (linhas novas do diff); achado fora do diff vai à parte, como seguimento, nunca como ajuste do review",
             "Card 79047: a revisão #1203 misturou achados de código antigo e a pessoa teve de pedir só o código novo.", "#1203"),
            ("[]", "levar um ajuste novo para um destino cujo PR já foi mesclado",
             "faça o cherry-pick na MESMA branch do destino pela regra (ex.: hotfix/<card>-dev) e abra um PR novo dela; não pergunte nome de branch nem deixe o destino sem o ajuste",
             "Card 79047: o PR de development (#31429) já estava mesclado e o ajuste do review ficou fora do development.", "#1191"),
            ("[]", "terminar os ajustes pedidos num review de PR",
             "responda cada comentário no PR com o que mudou e o commit (prmake_pr_review_reply) e peça nova revisão (prmake_pr_request_review)",
             "Card 79047: os ajustes subiram para o PR 31430 sem resposta aos comentários do revisor.", "#1206"),
            ("[]", "criar item de revisão de PR, de ajuste ou de resumo",
             "publique na Timeline só se a pessoa pediu; a conclusão fica no resultado da sub-tarefa",
             "Card 79047: o item #1203 publicou a revisão na Timeline sem ninguém pedir.", "#1205"),
            ("[\"especialista-do-caso\"]", "uma etapa ou um item pode não tratar algo que você previu",
             "ajuste você mesmo a instrução da etapa ou da sub-tarefa (prmake_step / prmake_work_task_update) em vez de pedir para a pessoa avisar se der errado",
             "Card 79047: \"a etapa de PRs deve tratar isso; se não tratar, me avise\" — o development ficou sem o ajuste.", "msg 25")
        ];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GovernanceLessons",
                schema: "governance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    Scope = table.Column<string>(type: "jsonb", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Text = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    Why = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    Sources = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Hits = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RetiredReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceLessons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceLessonSignals",
                schema: "governance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    Card = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LessonId = table.Column<long>(type: "bigint", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceLessonSignals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceLessons_Status_Hits",
                schema: "governance",
                table: "GovernanceLessons",
                columns: new[] { "Status", "Hits" });

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceLessonSignals_Card_Status",
                schema: "governance",
                table: "GovernanceLessonSignals",
                columns: new[] { "Card", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceLessonSignals_Key",
                schema: "governance",
                table: "GovernanceLessonSignals",
                column: "Key",
                unique: true);

            foreach (var (scope, trigger, text, why, reference) in Seed)
                migrationBuilder.Sql($$"""
                    INSERT INTO governance."GovernanceLessons"
                        ("Scope", "Trigger", "Text", "Why", "Sources", "Status", "Origin", "Hits", "CreatedAt", "CreatedBy", "LastSeenAt")
                    VALUES ('{{scope}}'::jsonb, {{Q(trigger)}}, {{Q(text)}}, {{Q(why)}},
                        jsonb_build_array(jsonb_build_object('card', '79047', 'ref', {{Q(reference)}}, 'at', now(), 'by', 'PRMake')),
                        'active', 'seed', 1, now(), 'PRMake (card 79047)', now());
                    """);
        }

        private static string Q(string v) => "'" + v.Replace("'", "''") + "'";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GovernanceLessons",
                schema: "governance");

            migrationBuilder.DropTable(
                name: "GovernanceLessonSignals",
                schema: "governance");
        }
    }
}
