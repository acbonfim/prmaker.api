# Feature 0064 — Traduções do glossário pelo Multilingual de dev (reserva) em PT, EN e ES

A credencial de produção do Multilingual (`~/.claude/multilingual-credentials.json`, bloco `prod`) não existe nas
máquinas; a de dev existe (`~/.claude/postgres-credentials-dev.json`, arquivo "solto", sem o bloco do ambiente). Sem
traduções, o glossário não tem "Compliance per Checklist" e os cards em inglês não acham a tela (card 75294).

- `re_traducoes.py` aceita o arquivo solto e, sem a credencial de produção, usa as de `fallbackCredentials` da
  configuração (padrão: a de dev), com aviso. Somente leitura.
- Idiomas: **português, inglês e espanhol** — o texto pt-BR entra como sinônimo quando difere da chave.
- Configuração: `fallbackCredentials` no `ReverseEngineeringTranslations` (padrão no código e migração que acrescenta
  a chave só se faltar e só em JSON válido).

Testado: migração num Postgres local; `re_traducoes.py` contra o Multilingual de dev (Cumprimento por Checklist →
Compliance per Checklist / Cumplimiento por Checklist) e `re_tool.py termos` exigindo os 3 idiomas.
