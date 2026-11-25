# Feature 0083 — Primeiros passos (tour de configuração inicial)

Pedido do usuário (2026-11-04):

> Temos um recurso de tour na aplicação. Eu preciso que, ao realizar o primeiro acesso, a aplicação faça um tour de como
> realizar a primeira configuração do PRMake (Minhas integrações, Skills do Claude, executores).
>
> Objetivo: orientar um novo usuário para deixar o PRMake configurado para analisar cards e ficar 100% funcional.
> Analise todo o processo, oriente como gerar personal access token do Azure (com links), também oriente como gerar
> token do GitHub, como pegar informações pessoais do DevOps, api key dos providers de IA, URL de webhook do Teams.

## O processo completo (levantado do código e da configuração de produção)

| # | Onde | O quê | Obrigatório | Como saber se está pronto |
|---|---|---|---|---|
| 1 | Minhas integrações → AzureDevOps Configurations | `PersonalAccessToken` (PAT, escopo Work Items read & write) — Organization/Project são do admin | sim | `configured` do plugin |
| 2 | Minhas integrações → Github Configurations | `Token` (classic com `repo`, autorizado no SSO da organização `Owner`) | sim | `configured` |
| 3 | Minhas integrações → AI Configurations | `Provider` (Claude/Gemini) + estados/áreas do board do DevOps (têm padrão) | opcional | `configured` |
| 4 | Minhas integrações → Claude Plugin / Gemini Plugin | `ApiKey` do provedor escolhido (+ `WorkspaceId` da Anthropic) — a IA do servidor cobra essa chave | opcional | `configured` |
| 5 | Minhas integrações → Teams Configurations | `WebhookUrl` do Workflow "Enviar alertas de webhook para um chat" no grupo `GroupName` | opcional | `configured` |
| 6 | Máquina | Claude Code instalado e logado | sim | doctor do executor ("Claude Code", "Login do Claude Code") |
| 7 | Skills do Claude | API Key pessoal + comando de instalação (macOS/Linux ou Windows) — também registra o MCP | sim | doctor ("Skills do PRMake", "api-key do usuário", "MCP do PRMake") ou marcado à mão |
| 8 | Skills do Claude (etapa 4) | VPN + `prmake-skills.sh db-credentials` (bancos dos clientes, só leitura) | recomendado | doctor ("Acesso aos bancos", "Banco prod…") |
| 9 | Meus executores | `prmake-skills.sh agent install` — máquina online; repositórios clonados com acesso git | sim (para analisar pela tela) | worker online + doctor sem problemas + mapa de repositórios |
| 10 | Tela do card | primeira análise ("Analisar com Claude") | — | — |

"Informações pessoais do DevOps": o e-mail/nome no DevOps (perfil) — usado em "Atribuído a" da regra automática do
executor (@Me = dono do PAT) — e os estados/áreas do board do time (campos do AI Configurations).

## Requisitos

1. **Primeiro acesso**: depois do login, se a configuração não estiver completa e a pessoa ainda não tiver visto o guia,
   abre sozinho o **"Primeiros passos no PRMake"** (uma vez por usuário/navegador; versão para reabrir quando mudar).
2. O guia mostra cada etapa com **status ao vivo** (integrações, executor e diagnóstico), o **passo a passo com links**
   (PAT do Azure, token do GitHub, dados do DevOps, chave da Anthropic/Gemini, webhook do Teams, Claude Code, skills,
   bancos/VPN, executor) e um botão que abre a tela real já no ponto certo ("Preencher agora" abre Minhas integrações
   na integração escolhida).
3. Links montados da configuração (organização do DevOps, owner do GitHub, grupo do Teams) — nada fixo no código.
4. "Minhas integrações" ganha o mesmo passo a passo por integração ("Como obter"), não só o do Teams.
5. Rótulos e ajudas curtas dos campos pessoais semeados por migração (só onde o admin não definiu).
6. Ao fechar o guia pela primeira vez, um balão do tour (driver.js) mostra onde ficam essas opções (menu do usuário).
7. O guia fica acessível no menu do usuário ("Primeiros passos", com o progresso).
