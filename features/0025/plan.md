# Feature 0025 — PRs e plano atualizados sem cliques; o PRMake acorda o Claude

> Spec: [`spec.md`](./spec.md) · Status: [`status.md`](./status.md)
> Branch: `feature/0025` no backend (`../prform.api-0025`) e no front (`../solvace.prform.web/prform-app-0025`).

## 1. Diagnóstico (2026-09-29)
- Logs de produção: a seção do plano já consultava o plano a cada 30 s, mas a sincronização de PRs do plano só rodava 1× por minuto — mesmo com o status já gravado por outra tela, o plano esperava até 1 min.
- A lista de PRs da tela do card só se atualizava pelo ⟳ ou por evento de outra ação.
- O PRMake não tem como chamar uma sessão do Claude Code: a sessão só percebe mudanças quando roda um comando.
- Webhook do GitHub (instantâneo) exigiria administrador no repositório `electradv/edv-solvace` — fica como passo futuro opcional.

## 2. Decisões
- **Backend**: a sincronização de PRs do plano passa a rodar a cada leitura (só uma proteção de 8 s contra rajada). O GitHub continua protegido pelo cache de 1 min por PR do módulo de PRs, que já lê do banco os mesclados/fechados e avisa as telas quando um status muda.
- **Front**: vigia silencioso na tela do card — a cada 45 s (aba visível, algum PR aberto) consulta o status sem skeleton/spinner e só troca a lista se algo mudou; ao voltar para a aba, confere na hora. A seção do plano escuta o evento de PR do card (`pullRequestCardUpdated`/`github-pr-*`) e se recarrega na hora.
- **Skill**: comando `watch` (vigia) rodando em segundo plano no Claude Code — consulta o `control` (que também sincroniza os PRs), 20 s nos primeiros 10 min e 60 s depois, e termina com o resumo do que mudou (etapa, perguntas, plano). A `SKILL.md` manda sempre deixar o vigia rodando ao terminar a vez esperando algo de fora e continuar sozinha quando ele terminar. Limite: sessão fechada = vigia parado (`/analisar-bug <card>` retoma).

## 3. Fases
| Onda | Fases |
|---|---|
| 1 | B1 (sincronização sem espera de 1 min), F1 (vigia de PRs + plano reage ao evento), S1 (`watch` + SKILL.md) |
| 2 | T1 (teste local), Q1 (PRs e deploy) |
