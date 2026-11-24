# 0101 — Padrões de banco de dados e de estrutura de aplicação na governança

Origem: pedido do usuário (2026-11-24), depois da 0098 (governança) em produção:

> Nas regras de governança, quero que seja possível definir padrões de banco de dados e estrutura de aplicação. Como
> carga inicial para banco, tudo que tem na wiki "Data Nomenclature and Rules (Nomenclatura e Regras de dados)"
> (Solvace Product Improvement, página 236). Para aplicações, a estrutura que a nossa engenharia reversa já conhece.

## O que existe hoje (0098)

As regras da governança são **guardrails de ação** (barram/avisam comandos, endpoints e ferramentas). Padrões são outra
coisa: **convenções** que a análise e a correção seguem ao escrever SQL, migrações e código, e que podem ser conferidas
no que foi produzido.

## A carga inicial de banco (lida em 2026-11-24, cópia em `features/0101/wiki/`)

- Página 236 + 9 subpáginas (Check, Default, FK, Índice, PK, Procedure, Tabela, Índice Único, View), v2.0 "Rascunho para
  revisão do DBA" (responsável: Claudio Arruda), SQL Server (bancos Global e Local), bilíngue EN/PT.
- **104 regras com ID** (G1–G8, N1–N14, C1–C2, S1–S5, R1–R7, CK, DF, FK1–FK10, IX1–IX11, PK1–PK10, P1–P7, T1–T7,
  IXU1–IXU6, V1–V7); **28 marcadas (NEW)** = propostas que o DBA ainda precisa validar; padrões de nome por objeto
  (`PK_[schema]_[Table]`, `FK_[schema]_[Table]_[Column]`, `IX_…_NN`, `IXU_…_NN`, `DF_…`, `CK_…`, `vw`, `pr`), exemplos
  certo/errado, checklist de code review e **scripts SQL de validação** contra o catálogo (`sys.*`).
- Escopo da própria wiki: objetos novos e módulos em migração para o Global; legado no `dbo` fica como está.

## O que entregar

1. **Catálogo de padrões** na governança (aba "Padrões"): itens com ID, área (banco · aplicação), assunto (tabela,
   coluna, chave, índice, procedure, view, script; camadas, pastas, nomes), texto EN/PT, exemplos, a que se aplica
   (plataforma, repositórios/módulos, objetos novos × legado), origem (wiki + versão, engenharia reversa + documento),
   estado (proposto · vigente · descontinuado) e efeito (monitorar · avisar · bloquear — começa em monitorar).
2. **Conferência automática** opcional por item: regex sobre DDL/SQL/caminho, ou a consulta de validação da wiki
   (rodada sob demanda, somente leitura, no banco de um ambiente) — achados entram na trilha.
3. **Onde vale**: contexto das skills (só os padrões que valem para o card), gravação de script/migração/PR pelo PRMake
   (trava como as da 0075/0076, com o efeito do item), hook do Claude Code ao editar `.sql`/migrações/arquivos fora da
   estrutura, tela para o admin ver aderência e achados.
4. **Importação da wiki** pelo PRMake (integração DevOps de quem pede), com rascunho → aprovação do admin, e
   reimportação quando a wiki mudar (diferença por item).
5. **Estrutura de aplicação** gerada a partir da engenharia reversa publicada (arquitetura / especificação de
   arquitetura / índice técnico por módulo): padrões por módulo que referenciam os itens da ER, sem copiar o texto, para
   aprovação.
