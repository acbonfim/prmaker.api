- **.NET Core 3.1** fora de suporte: nada de APIs novas; build com SDK 3.1 (Dockerfile da raiz é do `request_download_consumer`).
- **Um app por módulo**: a correção de um módulo não chega aos outros; helpers/data_access compartilhados afetam todos — cuidado
  com o raio de alcance da mudança.
- **Site errado** é a causa mais comum de "registro sumiu/não aparece": conferir o site ativo/`LAST_SITE_ID` antes de caçar bug.
- **Credenciais em código** (`all_conn.asp`): não reproduzir em análises, PRs ou planos.
- Timezone do container fixado em `America/Sao_Paulo` no Dockerfile — datas "erradas" por fuso podem vir daí.
- Scripts de correção de dados: sempre por chamado (padrão B da skill), com rollback.
