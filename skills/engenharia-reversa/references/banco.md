# Banco de dados (DEMO) e glossário — como documentar (0053)

## O catálogo
`re.sh banco <módulo>` grava em `~/.prmake/reverse/<módulo>/banco/`:
- `catalogo.json` — cada objeto: tipo, **escopo** (global / local / global e local), bancos, datas, motivo (tabela do
  módulo, usa tabela do módulo, sigla no nome, chamado pelo código, trigger em tabela do módulo), `uses`/`usedBy`,
  `divergent` (definição diferente entre os locais) e os jobs;
- `views/`, `procedures/`, `functions/`, `triggers/` — o corpo de cada objeto (`.sql`; divergente = um arquivo por banco);
- `tabelas/<TB>.md` — colunas, padrão/fórmula, PK/únicos, FKs, **check constraints**, quem usa;
- `jobs/<job>.md` — agenda e passos do SQL Agent (credenciais mascaradas), e quantos outros clientes têm o mesmo job;
- `inventario-banco.json` — o que a cobertura cobra (cada objeto, constraint e job).
Fonte da verdade = o banco da DEMO (reflete produção). Nunca `solvace-asp/#database/…`.

## No levantamento de arquitetura — seção "Banco de dados: views, procedures, functions, triggers e jobs"
Um item por objeto do catálogo (os de outro módulo que só leem as tabelas deste: um item curto com `**Módulos:**`):
```
### SQL-004 — procedure dbo.STP_SA3_Fecha (fecha A3 vencidos)
- **Banco:** DEMO local (CTB, GLB — DIVERGENTE: GLB grava status 3, CTB grava 2) · alterada em 2026-08-12
- **Onde:** banco DEMO local · dbo.STP_SA3_Fecha (linha 12)
- **Tabelas:** TB_SA3_A3, TB_SA3_STATUS · **Quem chama:** JOB-002 (diário 03:00), API-007
Passo a passo: 1) seleciona A3 com prazo vencido e status 1 ou 2; 2) …  Regras: RN-031, RN-032.
```
```
### TRG-001 — trigger TR_SA3_A3_AUDIT em TB_SA3_A3 (UPDATE)
- **Banco:** DEMO global · alterado em 2026-06-18
- **Onde:** banco DEMO global · dbo.TR_SA3_A3_AUDIT
Efeito escondido: a cada UPDATE grava o histórico em TB_SA3_HIST (quem, quando, status anterior) — RN-040.
```
Job: `### JOB-002 — SQL Agent "SA3 fecha vencidos"` com agenda, passos, objetos que executa e "também em N clientes".
Tabelas (`DB-…`): as check constraints, defaults e FKs são **regras** — cite a constraint (`CK_SA3_STATUS`) e leve a
regra para o funcional (`RN`).

## No levantamento funcional
Regra que só existe no banco (procedure, trigger, check, job, view que filtra) vira `RN` normal com
`- **Onde:** banco DEMO <global|local> · dbo.<objeto> (linha N)` — e o texto diz **em qual banco** ela vale (global ×
local, e se diverge entre plantas). A análise de bug usa isso para saber onde olhar.

## Glossário (seção obrigatória do funcional)
`re.sh termos <módulo>` monta `inventario-termos.json`: rótulos da tela (legado `GetLanguageByName`, i18n do front),
menus e aplicação no banco, siglas — com as traduções EN/ES do `TB_WCM_LANGUAGE`. A cobertura do funcional exige **cada
termo** no glossário (título ou `**Sinônimos:**` de um `GLO`). Junte variações num item só:
```
### GLO-003 — A3
- **Sinônimos:** SA3, RCA, RCA 1-pager, RCAs 1-pager, root cause analysis, Análise de causa raiz, TB_SA3_A3
- **Onde aparece:** TELA-001, TELA-004, menu "Melhoria → Busca de A3"
Relatório de análise de causa raiz em uma página (problema → causas → ações → eficácia). No revamp: RCA (revamp-rca).
```
Termo que não é do domínio (sobrou do filtro) ou de outro módulo: liste numa lacuna com o motivo — conta como
coberto e o revisor vê na revisão ("Fora do glossário (em lacuna)"). Vale (0056) o `GAP` com "termos"/"glossário" no
título (todo o bloco conta) ou, em qualquer `GAP`, a linha `**Termos:**`:
```
### GAP-020 — Termos fora do glossário
- **Termos:** Salvar rascunho, Kaizen, Filtro avançado
Rótulos genéricos de interface e o nome de outro módulo (Kaizen → legado-kz), não são do domínio do A3.
```
Siglas de 2 caracteres com dígito ou em maiúsculas (`A3`, `5S`) contam como termo. Os sinônimos viram busca:
publicado, "RCA" acha os itens que dizem "A3".
