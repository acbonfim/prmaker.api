# Status — Feature 0083

| Fase | Descrição | Status | Responsável | Commits | Notas |
|---|---|---|---|---|---|
| B1 | Migração de rótulos/ajudas | concluída | Claude | ab52402 | `SeedPersonalFieldHelp0083` (SQL em DO-block): Label/Help de PAT, Token, ApiKey, Model, WorkspaceId, WebhookUrl, campos do DevOps e Help do Provider. Chave casada sem diferenciar maiúsculas (o `Plugin.GetFieldSettings` quebraria com chave duplicada), null/"" do admin não contam, plugin excluído intocado; idempotente; Down tira só o que ainda é igual ao semeado |
| F1 | Passo a passo por integração | concluída | Claude | front 870e40b | `setup-guide/integration-guides.ts`: Azure (PAT, escopo Work Items R&W, link pela `Organization`), GitHub (classic `repo`, SSO do `Owner`), AI Configurations (provedor + State/Area do board), Claude (platform.claude.com, WorkspaceId), Gemini (AI Studio), Teams (Workflow "Enviar alertas de webhook para um chat" no `GroupName`), "Seus dados no Azure DevOps" (perfil/e-mail p/ "Atribuído a", acesso ao projeto); plugin desconhecido = texto genérico |
| F2 | Serviço + guia "Primeiros passos" | concluída | Claude | front 870e40b | `SetupGuideService` (integrações via GET /UserIntegration; máquina via ExecutionWorker/mine + doctor: "Claude Code", "Login do Claude Code", "Skills do PRMake", "api-key do usuário", "Acesso aos bancos", "Banco …"); sem executor, etapas 2–4 marcadas à mão (localStorage). Diálogo com 6 etapas, status ao vivo (poll 30 s + "Verificar"), comandos com copiar, chave do provedor não escolhido = "Não precisa" |
| F3 | Minhas integrações: focus + "Como obter" | concluída | Claude | front 870e40b | `MAT_DIALOG_DATA {focus}` põe a integração primeiro, aberta e com o passo a passo; o bloco fixo do Teams virou o "Como obter" genérico (todas as integrações) |
| F4 | Menu, primeiro acesso, balão | concluída | Claude | front 870e40b | `maybeAutoOpen` no top-menu: lê o status, quem já está pronto só é marcado; senão abre (espera diálogo/tour). `GuidedTourService.held` segura o tour da tela do card enquanto decide. Primeiro fechar → balão `setup-menu` no `data-tour="user-menu"` (balão único sem contador). Item "Primeiros passos" com selo N/4 (lido ao abrir o menu) |
| Q1 | Build + e2e | concluída (local) | Claude | | `dotnet build` e `ng build` ok; e2e em `e2e/` (harness `.t0083`: Postgres 55483, API 5183, fakeauth 8183, front 4283) |

## Log
- 2026-11-04 — spec e plano a partir do pedido do usuário; levantamento dos plugins pessoais de produção (GET /UserIntegration).
- 2026-11-04 — B1 testada num Postgres isolado: chave `personalaccesstoken` minúscula reaproveitada, Label do admin
  preservado, Label null/Help "" substituídos, plugin excluído intocado, segunda execução sem mudança, Down ok.
- 2026-11-04 — F1–F4 + Q1. e2e (usuária nova, plugins iguais aos de produção): guia abre sozinho no 1º acesso (0/4),
  passo a passo do PAT aberto com o link da organização; GitHub e Teams com owner/grupo do admin; provedor Gemini →
  Claude Plugin "Não precisa"; "Preencher" abre Minhas integrações focada no Azure; salvar o PAT atualiza o guia para
  "Configurado"; fechar mostra o balão do menu; recarregar não reabre guia nem balão; menu mostra "Primeiros passos 0/4".
  Com máquina simulada (doctor): Claude Code, Skills e Executor "Pronto" (3/4), bancos "Recomendado", avisos do doctor
  listados. Usuária já pronta (GitHub salvo): o guia não abre e fica marcado; sem selo no menu. Celular ok.

## Pendências
- Validar com um usuário novo de verdade (textos e se o primeiro acesso abre na hora certa).
- Links externos conferidos em 2026-11-04 (dev.azure.com, learn.microsoft.com, github.com/docs, platform.claude.com,
  aistudio.google.com, support.microsoft.com, code.claude.com). Se um serviço mudar a tela, ajustar `integration-guides.ts`.
- Mudar o guia de um jeito que quem já viu deva ver de novo: subir `SETUP_GUIDE_VERSION` (só reabre para quem não está pronto).
