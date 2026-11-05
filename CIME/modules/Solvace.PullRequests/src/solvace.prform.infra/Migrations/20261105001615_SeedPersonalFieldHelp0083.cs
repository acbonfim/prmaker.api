using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0083 (primeiros passos): nome amigável e ajuda curta dos campos de uso pessoal ("Minhas integrações") — PAT do
    /// Azure DevOps, token do GitHub, chaves da Anthropic/Gemini, URL do Workflow do Teams. Só preenche o que falta:
    /// a chave do campo é casada sem diferenciar maiúsculas (o dicionário do <c>Plugin</c> não aceita duas iguais) e
    /// Label/Help que o admin já definiu são preservados.
    /// </summary>
    public partial class SeedPersonalFieldHelp0083 : Migration
    {
        private const string Seed = """
{
  "AzureDevOps Configurations": {
    "PersonalAccessToken": {"Label": "Personal Access Token (PAT) do Azure DevOps", "Help": "Azure DevOps → User settings → Personal access tokens. Escopo: Work Items (Read & write)."},
    "TitleFieldName": {"Label": "Campo do título no DevOps", "Help": "Deixe o sugerido, salvo orientação do administrador."},
    "RootCauseFieldPath": {"Label": "Campo do Root Cause no DevOps (caminho)", "Help": "Deixe o sugerido, salvo orientação do administrador."},
    "RetroStepsFieldName": {"Label": "Campo dos passos para reproduzir (Repro Steps)", "Help": "Deixe o sugerido, salvo orientação do administrador."}
  },
  "Github Configurations": {
    "Token": {"Label": "Token pessoal do GitHub", "Help": "GitHub → Settings → Developer settings → Tokens (classic), escopo repo; autorize o SSO da organização."}
  },
  "Claude Plugin": {
    "ApiKey": {"Label": "Chave da API da Anthropic (sk-ant-…)", "Help": "platform.claude.com → API keys. As ações de IA do PRMake são cobradas nesta chave."},
    "Model": {"Label": "Modelo do Claude", "Help": "Deixe o sugerido, salvo orientação do administrador."},
    "WorkspaceId": {"Label": "Workspace da Anthropic (wrkspc_…)", "Help": "Só para chave de organização (fora de um workspace): platform.claude.com → Workspaces."}
  },
  "Gemini Plugin": {
    "ApiKey": {"Label": "Chave da API do Gemini", "Help": "aistudio.google.com → Get API key. As ações de IA do PRMake são cobradas nesta chave."}
  },
  "Teams Configurations": {
    "WebhookUrl": {"Label": "URL do Workflow do Teams", "Help": "No grupo do Teams: ⋯ → Workflows → \"Enviar alertas de webhook para um chat\"; copie a URL."}
  },
  "AI Configurations": {
    "Provider": {"Help": "Claude ou Gemini — preencha a chave do provedor escolhido na integração dele."}
  }
}
""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DO $seed$
                DECLARE
                    seed jsonb := '{{Seed.Replace("'", "''")}}'::jsonb;
                    r record;
                    s record;
                    fs jsonb;
                    k text;
                    cur jsonb;
                BEGIN
                    FOR r IN SELECT "Id", "Description", "FieldSettings" FROM prform."Plugins"
                             WHERE NOT "IsDeleted" AND seed ? "Description" LOOP
                        fs := COALESCE(NULLIF(r."FieldSettings", '')::jsonb, '{}'::jsonb);
                        IF jsonb_typeof(fs) <> 'object' THEN CONTINUE; END IF;
                        FOR s IN SELECT key, value FROM jsonb_each(seed -> r."Description") LOOP
                            SELECT x INTO k FROM jsonb_object_keys(fs) AS x WHERE lower(x) = lower(s.key) LIMIT 1;
                            k := COALESCE(k, s.key);
                            -- O que o admin já definiu (sem null/vazio) vence o semeado.
                            SELECT COALESCE(jsonb_object_agg(e.key, e.value), '{}'::jsonb) INTO cur
                            FROM jsonb_each(CASE WHEN jsonb_typeof(fs -> k) = 'object' THEN fs -> k ELSE '{}'::jsonb END) AS e
                            WHERE e.value NOT IN ('null'::jsonb, '""'::jsonb);
                            fs := (fs - k) || jsonb_build_object(k, s.value || cur);
                        END LOOP;
                        UPDATE prform."Plugins" SET "FieldSettings" = fs::text, "UpdatedAt" = now() WHERE "Id" = r."Id";
                    END LOOP;
                END
                $seed$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tira só o Label/Help que ainda é igual ao semeado (o que o admin mudou fica).
            migrationBuilder.Sql($$"""
                DO $seed$
                DECLARE
                    seed jsonb := '{{Seed.Replace("'", "''")}}'::jsonb;
                    r record;
                    s record;
                    fs jsonb;
                    k text;
                    cur jsonb;
                BEGIN
                    FOR r IN SELECT "Id", "Description", "FieldSettings" FROM prform."Plugins"
                             WHERE seed ? "Description" AND NULLIF("FieldSettings", '') IS NOT NULL LOOP
                        fs := r."FieldSettings"::jsonb;
                        IF jsonb_typeof(fs) <> 'object' THEN CONTINUE; END IF;
                        FOR s IN SELECT key, value FROM jsonb_each(seed -> r."Description") LOOP
                            SELECT x INTO k FROM jsonb_object_keys(fs) AS x WHERE lower(x) = lower(s.key) LIMIT 1;
                            IF k IS NULL OR jsonb_typeof(fs -> k) <> 'object' THEN CONTINUE; END IF;
                            SELECT COALESCE(jsonb_object_agg(e.key, e.value), '{}'::jsonb) INTO cur
                            FROM jsonb_each(fs -> k) AS e
                            WHERE NOT (s.value ? e.key AND s.value -> e.key = e.value);
                            fs := jsonb_set(fs, ARRAY[k], cur);
                        END LOOP;
                        UPDATE prform."Plugins" SET "FieldSettings" = fs::text WHERE "Id" = r."Id";
                    END LOOP;
                END
                $seed$;
                """);
        }
    }
}
