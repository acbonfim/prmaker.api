# Feature 0059 — Aba Infra no PRMake (engenharia reversa)

A 0058 criou a etapa opcional de infra (`re.sh infra`), mas o resultado ficava só em `~/.prmake/reverse/<modulo>/infra/`.
Esta feature envia o recorte do módulo para o servidor e mostra na tela do módulo, na aba **Infra**.

- Backend: entidade `ReverseInfraSnapshot` (um por módulo, substituído a cada leitura), `GET/PUT api/v1/ReverseEngineering/modules/{key}/infra`, migração do schema `knowledge`.
- Skill: `re.sh infra` monta `infra/payload.json` (só o ligado ao módulo + resumo da conta; sem segredo) e faz o PUT.
- Front: aba **Infra** (Recursos, Esteiras de deploy, Segredos, Logs, Conta) + o que ficou sem permissão.
