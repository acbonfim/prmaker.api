using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0106: pedido de mudanças nos PRs — idempotente, só no "Skills Configurations":
    /// <list type="bullet">
    /// <item>tipos de sub-tarefa <c>pr-update</c> (levar o ajuste aos PRs do card: PR aberto se atualiza, PR já mesclado ganha PR
    /// novo da mesma branch) e <c>review-reply</c> (responder cada comentário no PR e pedir nova revisão) se faltarem;</item>
    /// <item>modelo <c>atender-review-pr</c> (o PRMake cria sozinho quando um PR do card recebe "changes requested") se faltar;</item>
    /// <item>modelo <c>revisar-pr</c> e tipo <c>pr-review</c>: a Timeline vira pergunta (padrão "não" — card 79047: a revisão
    /// publicou sem ninguém pedir), SÓ se ainda forem exatamente os semeados pela <c>SeedSpecialistTools0085c</c>.</item>
    /// </list>
    /// JSON inválido do admin: a linha fica como está. Down volta só o que ainda está exatamente como esta migração pôs.
    /// </summary>
    public partial class SeedReviewFlow0106 : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        public const string ReviewTemplateKey = "atender-review-pr";

        public const string NewKinds = """
[
  { "kind": "pr-update", "label": "Atualizar os PRs do card", "icon": "sync_alt", "executor": "claude",
    "description": "Leva os commits novos da branch do card para a branch de cada destino pela regra do card: PR aberto → cherry-pick na branch dele e push (o PR se atualiza); PR já mesclado → cherry-pick na MESMA branch do destino e PR novo dela; PR fechado sem merge → pergunta. Nunca mescla.",
    "params": [
      { "key": "repository", "label": "Repositório", "required": true, "input": "repository" },
      { "key": "from", "label": "Branch de origem (a do card)", "required": true, "input": "text" },
      { "key": "targets", "label": "Destinos", "input": "text" }
    ] },
  { "kind": "review-reply", "label": "Responder o review no PR", "icon": "forum", "executor": "claude",
    "description": "Responde cada comentário do pedido de mudanças no próprio PR (o que mudou e o commit, ou por que não mudou) e pede nova revisão a quem pediu as mudanças. Nunca aprova nem mescla.",
    "params": [
      { "key": "repository", "label": "Repositório", "required": true, "input": "repository" },
      { "key": "pullRequest", "label": "Pull request", "required": true, "input": "pull-request" },
      { "key": "reviewers", "label": "Quem pediu mudanças", "input": "text" }
    ] }
]
""";

        public const string ReviewTemplate = """
{
  "key": "atender-review-pr", "type": "adjustment", "group": "PRs e promoção",
  "title": "Atender o review do PR #{ask:pullRequest}",
  "description": "O revisor pediu mudanças no PR #{ask:pullRequest}: avaliar os pontos (só o código novo do PR), ajustar na {branch}, levar o ajuste aos PRs do card (PR aberto se atualiza; PR já mesclado ganha PR novo da mesma branch) e responder cada comentário no PR, pedindo nova revisão. Nunca aprova nem mescla.",
  "when": { "type": ["bug", "user-story"] },
  "ask": [
    { "key": "repository", "label": "Repositório", "input": "repository", "default": "{repository}", "required": true },
    { "key": "pullRequest", "label": "Pull request", "input": "pull-request", "required": true },
    { "key": "reviewers", "label": "Quem pediu mudanças (opcional)", "input": "text", "required": false }
  ],
  "tasks": [
    { "title": "Ler o review do PR #{ask:pullRequest} e decidir ponto a ponto (só o código novo do PR)", "kind": "research",
      "params": { "repository": "{ask:repository}", "pullRequest": "{ask:pullRequest}", "scope": "pr-diff" } },
    { "title": "Ajustar o código na {branch} conforme o review", "kind": "code",
      "params": { "repository": "{ask:repository}", "branch": "{branch}", "instructions": "" } },
    { "title": "Validar o ajuste", "kind": "validation", "params": { "how": "build e testes do que mudou" } },
    { "title": "Levar o ajuste aos PRs do card ({flowTargets})", "kind": "pr-update",
      "params": { "repository": "{ask:repository}", "from": "{branch}", "targets": "{flowTargets}" } },
    { "title": "Responder o review no PR #{ask:pullRequest} e pedir nova revisão", "kind": "review-reply",
      "params": { "repository": "{ask:repository}", "pullRequest": "{ask:pullRequest}", "reviewers": "{ask:reviewers}" } }
  ]
}
""";

        /// <summary><c>revisar-pr</c> como a <c>SeedSpecialistTools0085c</c> semeou.</summary>
        public const string OldReview = """
{
  "key": "revisar-pr", "type": "review", "group": "PRs e promoção",
  "title": "Revisar um PR",
  "description": "Revisão do PR escolhido (de outra pessoa ou do turno contrário): diff contra a análise do card, riscos e testes; a conclusão vai para a Timeline. Nunca aprova nem mescla.",
  "when": { "type": ["bug", "user-story"] },
  "ask": [
    { "key": "repository", "label": "Repositório", "input": "repository", "default": "{repository}", "required": true },
    { "key": "pullRequest", "label": "Pull request", "input": "pull-request", "required": true },
    { "key": "focus", "label": "O que olhar", "input": "textarea" }
  ],
  "tasks": [
    { "title": "Revisar o PR {ask:pullRequest}", "kind": "pr-review",
      "params": { "repository": "{ask:repository}", "pullRequest": "{ask:pullRequest}", "focus": "{ask:focus}" } },
    { "title": "Registrar a conclusão da revisão na Timeline", "kind": "comment",
      "params": { "target": "timeline", "text": "" } }
  ]
}
""";

        public const string NewReview = """
{
  "key": "revisar-pr", "type": "review", "group": "PRs e promoção",
  "title": "Revisar um PR",
  "description": "Revisão do PR escolhido (de outra pessoa ou do turno contrário): só o código que o PR muda, contra a análise do card, riscos e testes; a conclusão fica na sub-tarefa (na Timeline só se pedir). Nunca aprova nem mescla.",
  "when": { "type": ["bug", "user-story"] },
  "ask": [
    { "key": "repository", "label": "Repositório", "input": "repository", "default": "{repository}", "required": true },
    { "key": "pullRequest", "label": "Pull request", "input": "pull-request", "required": true },
    { "key": "focus", "label": "O que olhar", "input": "textarea" },
    { "key": "timeline", "label": "Publicar a conclusão na Timeline?", "input": "select", "options": ["não", "sim"], "default": "não", "required": true }
  ],
  "tasks": [
    { "title": "Revisar o PR {ask:pullRequest}", "kind": "pr-review",
      "params": { "repository": "{ask:repository}", "pullRequest": "{ask:pullRequest}", "focus": "{ask:focus}", "timeline": "{ask:timeline}" } }
  ]
}
""";

        public const string OldReviewKind = """
{ "kind": "pr-review", "label": "Revisar PR", "icon": "rate_review", "executor": "claude",
  "description": "Revisa um PR (diff contra a análise do card, riscos e testes) e registra a conclusão; nunca aprova nem mescla.",
  "params": [
    { "key": "repository", "label": "Repositório", "required": true, "input": "repository" },
    { "key": "pullRequest", "label": "Pull request", "required": true, "input": "pull-request" },
    { "key": "focus", "label": "O que olhar", "input": "textarea" }
  ] }
""";

        public const string NewReviewKind = """
{ "kind": "pr-review", "label": "Revisar PR", "icon": "rate_review", "executor": "claude",
  "description": "Revisa um PR — só o código que o PR muda (diff contra a análise do card, riscos e testes) — e registra a conclusão na sub-tarefa; na Timeline só com timeline = sim. Nunca aprova nem mescla.",
  "params": [
    { "key": "repository", "label": "Repositório", "required": true, "input": "repository" },
    { "key": "pullRequest", "label": "Pull request", "required": true, "input": "pull-request" },
    { "key": "focus", "label": "O que olhar", "input": "textarea" },
    { "key": "timeline", "label": "Publicar na Timeline", "input": "select", "options": ["não", "sim"] }
  ] }
""";

        private static string Swap(bool up) => $$"""
            DO $mig$
            DECLARE
                r record; opts jsonb; kinds jsonb; tpls jsonb; changed boolean; nk jsonb; e jsonb;
                newkinds jsonb := $nk${{NewKinds}}$nk$::jsonb;
                rt jsonb := $rt${{ReviewTemplate}}$rt$::jsonb;
                oldrev jsonb := $or${{OldReview}}$or$::jsonb;
                newrev jsonb := $nr${{NewReview}}$nr$::jsonb;
                oldkind jsonb := $ok${{OldReviewKind}}$ok$::jsonb;
                newkind jsonb := $nw${{NewReviewKind}}$nw$::jsonb;
            BEGIN
                FOR r IN
                    SELECT c."Id", c."Options" FROM prform."PluginConfigurations" c
                    JOIN prform."Plugins" p ON p."Id" = c."PluginId"
                    WHERE p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted" AND NULLIF(c."Options", '') IS NOT NULL
                LOOP
                    BEGIN
                        opts := r."Options"::jsonb;
                        IF jsonb_typeof(opts) <> 'array' OR jsonb_typeof(opts -> 0) <> 'object' THEN CONTINUE; END IF;
                        kinds := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTaskKinds', '')), '')::jsonb;
                        tpls := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTemplates', '')), '')::jsonb;
                    EXCEPTION WHEN others THEN
                        CONTINUE;
                    END;
                    changed := false;

                    IF kinds IS NOT NULL AND jsonb_typeof(kinds) = 'array' THEN
                        FOR nk IN SELECT value FROM jsonb_array_elements(newkinds) LOOP
                            IF {{(up ? "true" : "false")}} AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(kinds) x WHERE x ->> 'kind' = nk ->> 'kind') THEN
                                kinds := kinds || jsonb_build_array(nk); changed := true;
                            ELSIF {{(up ? "false" : "true")}} AND EXISTS (SELECT 1 FROM jsonb_array_elements(kinds) x WHERE x = nk) THEN
                                SELECT COALESCE(jsonb_agg(t.e ORDER BY t.o), '[]'::jsonb) INTO kinds
                                FROM jsonb_array_elements(kinds) WITH ORDINALITY AS t(e, o) WHERE t.e <> nk;
                                changed := true;
                            END IF;
                        END LOOP;
                        -- pr-review: troca só se ainda for o semeado (o do admin fica).
                        SELECT COALESCE(jsonb_agg(CASE WHEN t.e = {{(up ? "oldkind" : "newkind")}} THEN {{(up ? "newkind" : "oldkind")}} ELSE t.e END ORDER BY t.o), '[]'::jsonb)
                          INTO e FROM jsonb_array_elements(kinds) WITH ORDINALITY AS t(e, o);
                        IF e <> kinds THEN kinds := e; changed := true; END IF;
                        opts := jsonb_set(opts, '{0,WorkItemTaskKinds}', to_jsonb(jsonb_pretty(kinds)), true);
                    END IF;

                    IF tpls IS NOT NULL AND jsonb_typeof(tpls) = 'array' THEN
                        IF {{(up ? "true" : "false")}} AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(tpls) x WHERE x ->> 'key' = rt ->> 'key') THEN
                            tpls := tpls || jsonb_build_array(rt); changed := true;
                        ELSIF {{(up ? "false" : "true")}} AND EXISTS (SELECT 1 FROM jsonb_array_elements(tpls) x WHERE x = rt) THEN
                            SELECT COALESCE(jsonb_agg(t.e ORDER BY t.o), '[]'::jsonb) INTO tpls
                            FROM jsonb_array_elements(tpls) WITH ORDINALITY AS t(e, o) WHERE t.e <> rt;
                            changed := true;
                        END IF;
                        SELECT COALESCE(jsonb_agg(CASE WHEN t.e = {{(up ? "oldrev" : "newrev")}} THEN {{(up ? "newrev" : "oldrev")}} ELSE t.e END ORDER BY t.o), '[]'::jsonb)
                          INTO e FROM jsonb_array_elements(tpls) WITH ORDINALITY AS t(e, o);
                        IF e <> tpls THEN tpls := e; changed := true; END IF;
                        opts := jsonb_set(opts, '{0,WorkItemTemplates}', to_jsonb(jsonb_pretty(tpls)), true);
                    END IF;

                    IF changed THEN
                        UPDATE prform."PluginConfigurations" SET "Options" = opts::text WHERE "Id" = r."Id";
                    END IF;
                END LOOP;
            END
            $mig$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Swap(true));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Swap(false));
    }
}
