# Feature 0076 — Status de execução

> Plano: [`plan.md`](./plan.md) · Spec: [`spec.md`](./spec.md) · Branch `feature/0076` em `prform.api` (back + skills).
> Legenda: ⬜ pendente · 🟡 em andamento · ✅ concluída · 🔴 bloqueada

| Fase | Descrição | Status | Commits |
|---|---|---|---|
| B1 | Azure: evidências do card e download | ✅ | 9da251e |
| B2 | Plano: importar, leitura, portão, MCP | ✅ | 9da251e |
| S1 | Skill: discussion, contexto, evidencias, conversão de Office | ✅ | 5d1b9fe |
| Q1 | Testes + integração com Postgres isolado | ✅ | — |
| M1 | Back: publicação configurável/visível, item da ER pelo card | ✅ | d87a186 |
| M2 | Skill: análise sem emojis (rótulos viram selos) | ✅ | d87a186 |
| M3 | Front: design do markdown, popover da ER, publicação no cartão, textos gerados | 🟡 | |
| Q2 | PR + merge + deploy (back e front juntos — autorizado pelo usuário em 2026-11-04) | 🟡 | |

## Q1
- Parser com JSON/HTML no formato do DevOps: campos → discussion → anexos, sem repetir; `System.History` fora; download só
  da organização configurada em `/_apis/wit/attachments/`; nomes seguros; formatos legíveis (17 testes).
- Integração (Postgres 18 isolado, repositório EF real, fonte do DevOps falsa — o `dev.azure.com` é fixo no serviço):
  portão recusa sem importação → importa 4 (`ev01-descricao-image.png`, `ev02-discussion-image.png`,
  `ev03-anexo-log_erro.txt`, vídeo como "abrir à mão") + 2 não importadas com o motivo (> 10 MB, 404) → portão lista as
  3 não abertas → abertas pela skill = liberado → reimportação só tenta as que falharam → print novo na discussion entra
  como `ev05` → seguir sem (motivo do usuário) libera e registra na Timeline.
- `evidence-text.py`: Word/Excel/e-mail convertidos (sem `xml.etree`, que quebra com o pyexpat do Homebrew 3.14).
- Suítes: executionplans 46/46, prform 131/131, knowledge 202/202.
- Não testado com o DevOps real (precisa da integração pessoal de produção): primeira análise depois do deploy.

## Log
- 2026-11-04 — spec/plano; branch `feature/0076` a partir da master 65c770e. Merge autorizado pelo usuário ao final desta demanda (2026-11-04: "faça tudo aqui nessa demanda, ao final pode mergear").
