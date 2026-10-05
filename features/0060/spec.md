# Feature 0060 — Análise sempre parte da engenharia reversa (caso do card 75294)

## Achado (acompanhando o card 75294, 2026-10-05)
A sessão usou a engenharia reversa (REL-031, UI-318, RN-343, UI-009) por disciplina da skill, mas o caminho padrão falhou:
1. `ForCardAsync`/`MatchModules`: Module "Checklist" casou com 4 módulos revamp (palavra-chave de outras áreas + corte em 4
   com revamp primeiro); o `legado-checklist` — único com engenharia reversa e dono da tela — ficou de fora. O contexto
   mandou "use a base antiga" e a trava de `investigar-codigo` ficou desligada.
2. O card vem em inglês ("Compliance per Checklist") e a engenharia reversa só tem "Cumprimento por Checklist" — a busca
   não acha (traduções do Multilingual não entraram por falta de credencial).
3. A conversão de datas do filtro salvo (causa do bug) não está em nenhum item — a sessão foi ao código sem registrar lacuna.

## Solução
- **A (servidor)**: casamento forte (nome, área, sufixo da chave, apelido sem o mundo) antes de palavra-chave; pares da
  mesma área com engenharia reversa entram como candidatos (salvo mundo explícito no campo); o título/repro ordena os
  módulos (placar da busca); módulo que casa com o card nunca é cortado. Mensagem sem itens manda `prmake_base_search`
  no índice inteiro antes da base antiga.
- **B (engenharia reversa)**: `re.sh termos` exige o nome EN/ES de cada termo traduzido nos sinônimos do glossário; sem
  traduções, aviso no terminal e na tela; modelo pede EN/ES nas Tags de TELA/REL/FN.
- **C (analisar-bug)**: nunca pular a engenharia reversa pelo contexto (busca sem módulo, PT e EN); lacuna obrigatória
  para o código lido sem item.
