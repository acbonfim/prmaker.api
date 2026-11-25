# 0093 — Status

| Fase | Status | Notas |
|---|---|---|
| Back | concluída | kind `env-reproduction`, item `reproduction`, modelo `reproduzir-bug-no-ambiente` (só Bug; ask `environments` com `{reproductionEnvironments}` = "Ambiente do cliente (relatado no card)" + ambientes, padrão `cliente`), migração `SeedEnvReproduction0093`, MCP `prmake_env_reproduction` (especialista), testes `EnvReproduction0093Tests` |
| Skills | concluída | `env.sh card-env <card>` (Environment/Site do card + URLs dos passos → ambiente, sigla da planta, admin), `browser start cliente --card`, seção "Reproduzir o bug" na `validar-ambiente`, guias executar/especialista/analisar-bug |
| Front | sem mudança | ferramenta, ícone, grupo, tipo de item e opções vêm do catálogo |

## Testes (2026-11-09)
- PullRequests 386 ok; SkillTests 49 ok; build ok.
- Migração em banco isolado: Up adiciona kind/tipo/modelo, 2ª execução igual, Down restaura (md5).
- `card-env` com card simulado no DEV (Environment=Desenvolvimento, Site=Milan) → planta MIL, `adminmil@solvacelabs.com`;
  `browser start cliente --card` → sem senha, exit 4 com o comando. Card 77698 (Lear - Production, Valença): ambiente
  achado no corporativo (`https://lear.solvacelabs.com/`, somente leitura); a sigla da planta depende de ler o banco
  global da Lear na máquina de quem executa (o auto mode desta sessão barrou a leitura de produção).
