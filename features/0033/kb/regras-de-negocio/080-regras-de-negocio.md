## O que é
Base de artigos do produto (revamp-KnowledgeCenter): categorias → subcategorias → artigos (`ART-n`) com tags. A cópia usada pelas
análises só tem **artigos publicados** e passa por um **filtro fixo de dados de teste**; é sincronizada pelas skills e aparece na
tela Base Solvace e no espelho local (`knowledge/ART-n.md`).

## Cobertura atual (ambiente DEV)
| Módulo | Artigos |
|---|---|
| Action Plan | ART-21 (busca e visualizações), ART-23 (criar/editar, campos obrigatórios, status), ART-24 (checklist), ART-25 (feed), ART-26 (histórico), ART-45 (notificações), ART-60 (permissões), ART-61 (kanbans e travas), ART-62 (calendário), ART-63 (analytics) |
| Produto (visão geral) | ART-46, ART-48, ART-50 |
| Demais módulos | **sem artigo** — regra só pelo código/histórico de cards; registre a lacuna na análise |

## Como usar na análise
- "É bug ou é assim mesmo?" → `kc.sh search <módulo tela comportamento>`; havendo artigo, ele define o esperado (padrão E — user
  education — quando o sistema segue o artigo). Cite o `ART-n`.
- Artigo diverge do código → registre (sugestão para a base e para o time do KC).
- Em produção o KC terá mais conteúdo: trocar o `Environment` do plugin para `prod` (credencial `prod` local de quem sincroniza).
