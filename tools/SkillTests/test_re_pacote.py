"""Testes da geração barata da engenharia reversa (0066): áreas, pacotes, juntar/compactar, evidência, inventário das
views do legado e retratos do banco/AWS.

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import gzip
import importlib.util
import json
import os
import stat
import sys
import tempfile
import textwrap
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = os.path.join(HERE, "..", "..", "skills", "engenharia-reversa", "scripts")
sys.path.insert(0, SCRIPTS)


def load(name):
    spec = importlib.util.spec_from_file_location(name, os.path.join(SCRIPTS, f"{name}.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


re_tool = load("re_tool")
re_pacote = load("re_pacote")
re_banco = load("re_banco")
re_infra = load("re_infra")


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(text)


CONTROLLER = textwrap.dedent('''\
    using System;
    namespace kaizen.Controllers
    {
        public class IdeiaController : Controller
        {
            [HttpPost("Ideia/Salvar")]
            public IActionResult Salvar(IdeiaModel m)
            {
                if (m.Aprovador == null) throw new DomainException("Informe o aprovador da etapa.");
                return Ok();
            }
        }
    }
    ''')


def big_view(n_funcs=60):
    parts = ["@model x", "<div>tela</div>", "<script>"]
    for i in range(n_funcs):
        parts += [f"function passo{i}() {{",
                  f"    ewcmAlert('@LanguageHelper.GetLanguageByTerm(ViewBag.actualLanguageTerms, \"Mensagem numero {i} da tela\")', 1);",
                  f"    $.post('@Url.Content(\"~/Ideia/Passo{i}\")', {{ id: {i} }});",
                  *[f"    var linha{j} = {j}; // preenchimento para o arquivo ficar grande" for j in range(25)],
                  "}"]
    parts.append("</script>")
    return "\n".join(parts) + "\n"


class Module:
    def __init__(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = os.path.join(self.tmp.name, "kaizen")
        write(os.path.join(self.root, "Controllers", "IdeiaController.cs"), CONTROLLER)
        write(os.path.join(self.root, "Views", "kz_reg_ideia.cshtml"), big_view())
        write(os.path.join(self.root, "Views", "kz_lst_ideia.cshtml"), "<div>lista</div>\n<script>\nfunction listar(){ ewcmAlert('@LanguageHelper.GetLanguageByTerm(x, \"Nenhuma ideia encontrada na busca\")'); }\nfunction salvar(){ $.post('@Url.Content(\"~/Ideia/Salvar\")', {}); }\n</script>\n")
        write(os.path.join(self.root, "wwwroot", "lib", "jquery.min.js"), "function x(){}\n")
        self.inv = re_tool.inventory([("backend", self.root)])

    def close(self):
        self.tmp.cleanup()


class InventoryTests(unittest.TestCase):
    def test_legacy_views_messages_and_calls_enter_the_inventory(self):
        m = Module()
        try:
            names = {(it["cat"], it["name"]) for it in m.inv["items"]}
            self.assertIn(("validacao", "Mensagem numero 3 da tela"), names)
            self.assertIn(("validacao", "Nenhuma ideia encontrada na busca"), names)
            self.assertIn(("http-front", "~/Ideia/Passo7"), names)
            self.assertIn(("validacao", "Informe o aprovador da etapa."), names)
        finally:
            m.close()


class AreaTests(unittest.TestCase):
    def setUp(self):
        self.m = Module()

    def tearDown(self):
        self.m.close()

    def test_areas_cover_every_line_of_every_file_once_and_respect_the_budget(self):
        areas = re_pacote.plan_areas(self.m.inv, "funcional", budget_kb=24, small=400, max_lines=220, used_ids=["RN-001", "UC-150", "GAP-099"])
        self.assertGreater(len(areas), 2)
        covered = {}
        for a in areas:
            self.assertLessEqual(a["bytes"], 24 * 1024 * 1.1)
            for u in a["units"]:
                for lo, hi in u["ranges"]:
                    covered.setdefault(u["file"], []).append((lo, hi))
        self.assertNotIn(os.path.join("wwwroot", "lib", "jquery.min.js"), covered)
        for rel, spans in covered.items():
            n = len(open(os.path.join(self.m.root, rel), encoding="utf-8").read().splitlines())
            lines = sorted(x for lo, hi in spans for x in range(lo, hi + 1))
            self.assertEqual(list(range(1, n + 1)), lines, rel)
        # faixas de ID: depois do maior usado (150 → 200), uma centena por área, sem sobrepor
        self.assertEqual(201, areas[0]["idRange"][0])
        starts = [a["idRange"][0] for a in areas]
        self.assertEqual(len(starts), len(set(starts)))

    def test_pack_has_real_line_numbers_items_and_output_path(self):
        areas = re_pacote.plan_areas(self.m.inv, "funcional", 24, 400, 220, [])
        plan = {"module": "revamp-kaizen", "docType": "funcional", "areas": areas, "checkpointEvery": 10}
        out = tempfile.mkdtemp()
        area = next(a for a in areas if any(u["file"].endswith("IdeiaController.cs") for u in a["units"]))
        path, size, n = re_pacote.build_pack(self.m.inv, plan, area, out, None, "/doc")
        text = open(path, encoding="utf-8").read()
        self.assertIn("/doc/parte-", text)
        self.assertIn("    9|             if (m.Aprovador == null)", text)
        self.assertGreater(n, 0)

    def test_pack_brings_the_called_action_from_another_area_as_support(self):
        rel = os.path.join("Views", "kz_lst_ideia.cshtml")
        lst = {"units": [{"file": rel, "ranges": [[1, 6]]}]}  # área só com a view; o controller está em outra
        blocks = re_pacote.support_blocks(self.m.inv, lst)
        self.assertTrue(any(f.endswith("IdeiaController.cs") and "Salvar" in "\n".join(lines[a:b + 1]) for f, a, b, lines in blocks))

    def test_cartao_joins_instructions_template_and_module_keys(self):
        d = tempfile.mkdtemp()
        write(os.path.join(d, "sub.md"), "# Cartao")
        write(os.path.join(d, "modelo.md"), "## Regras de negócio")
        write(os.path.join(d, "modulos.tsv"), "revamp-users\tUsuários\tusuarios novo\n")
        out = re_pacote.cartao(os.path.join(d, "sub.md"), os.path.join(d, "modelo.md"), os.path.join(d, "modulos.tsv"), os.path.join(d, "c.md"))
        text = open(out, encoding="utf-8").read()
        self.assertIn("## Regras de negócio", text)
        self.assertIn("`revamp-users` Usuários · usuarios novo", text)


class AdjustmentTests(unittest.TestCase):
    """0066-ajustes: arquivo que cabe não é partido, apoio das funções compartilhadas do legado e pacote do glossário."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        r = self.tmp.name
        os.makedirs(os.path.join(r, ".git"))
        write(os.path.join(r, "systems", "includes", "asp", "fnc_datas.asp"),
              "<%\nFunction GetRealDateX(d)\n  GetRealDateX = DateAdd(\"h\", -3, d)\nEnd Function\n%>\n")
        write(os.path.join(r, "systems", "includes", "js", "jquery.datatables.js"), "function GetRealDateX(){ return 1; }\n")
        page = ["<%", "Dim x", "x = GetRealDateX(Now())", "%>", "<span><%=GetLanguageByName(\"Observação de segurança\")%></span>"]
        page += [f"<p>linha {i} de preenchimento da pagina de registro</p>" for i in range(250)]
        write(os.path.join(r, "systems", "soc", "soc_registro.asp"), "\n".join(page) + "\n")
        write(os.path.join(r, "systems", "soc", "soc_busca.asp"), "<%\nx = GetRealDateX(Now())\n%>\n")
        self.inv = re_tool.inventory([("backend", os.path.join(r, "systems", "soc"))])

    def tearDown(self):
        self.tmp.cleanup()

    def test_file_that_fits_the_budget_is_never_split(self):
        areas = re_pacote.plan_areas(self.inv, "funcional", 20, 50, 220, [])  # ~14 KB: cabe, mesmo com mais linhas que "pequeno"
        owners = [a["name"] for a in areas for u in a["units"] if u["file"] == "soc_registro.asp"]
        self.assertEqual(1, len(owners), owners)

    def test_shared_functions_from_includes_come_as_support_without_vendor_libs(self):
        areas = re_pacote.plan_areas(self.inv, "funcional", 90, 400, 220, [])
        blocks = re_pacote.shared_blocks(self.inv, areas[0])
        files = [f for f, *_ in blocks]
        self.assertTrue(any(f.endswith("fnc_datas.asp") for f in files), files)
        self.assertFalse(any("jquery" in f for f in files))
        f, a, b, lines = next(x for x in blocks if x[0].endswith("fnc_datas.asp"))
        self.assertIn("End Function", "\n".join(lines[a:b + 1]))

    def test_glossary_pack_and_its_coverage(self):
        d = tempfile.mkdtemp()
        inv_path = os.path.join(d, "inventario.json")
        json.dump(self.inv, open(inv_path, "w", encoding="utf-8"))
        termos = os.path.join(d, "inventario-termos.json")
        json.dump({"items": [{"cat": "termo", "name": "Observação de segurança", "file": "soc_registro.asp", "line": 5, "detail": "tela"},
                             {"cat": "termo", "name": "Safety observation", "file": "soc_registro.asp", "line": 5,
                              "detail": 'EN de "Observação de segurança" (traducao)'}]}, open(termos, "w", encoding="utf-8"))
        plan = {"module": "legado-soc", "docType": "funcional", "inventory": inv_path, "idBase": 300,
                "areas": re_pacote.plan_areas(self.inv, "funcional", 90, 400, 220, [])}
        out, n = re_pacote.glossary_pack(plan, termos, d, d)
        text = open(out, encoding="utf-8").read()
        self.assertEqual(1, n)
        self.assertIn("EN: Safety observation", text)
        self.assertIn("GetLanguageByName", text)  # contexto da tela
        self.assertEqual(["glossario"], [x["name"] for x in plan["specials"]])
        self.assertGreater(plan["specials"][0]["idRange"][0], max(a["idRange"][1] for a in plan["areas"]))
        write(os.path.join(d, "parte-glossario.md"), "## Glossário\n### GLO-401 — Observação de segurança\n- **Sinônimos:** Safety observation, SOC\n")
        self.assertEqual(0, re_pacote.faltando(plan["inventory"] and re_pacote.load_inv(inv_path), plan, "glossario", os.path.join(d, "parte-glossario.md")))


class SpecialsTests(unittest.TestCase):
    def test_architecture_gets_bank_and_module_specials_and_areas_do_not_create_their_kinds(self):
        m = Module()
        try:
            d = tempfile.mkdtemp()
            moddir = os.path.join(d, "mod")
            banco = os.path.join(moddir, "banco")
            write(os.path.join(banco, "tabelas", "TB_KZ_IDEIA.md"), "# TB_KZ_IDEIA\n| # | Coluna |\n| 1 | ID |\n")
            write(os.path.join(banco, "triggers", "TR_KZ_LOG.sql"), "CREATE TRIGGER TR_KZ_LOG ...")
            json.dump({"objects": [{"name": "TB_KZ_IDEIA", "kind": "tabela", "scope": "local", "reason": "tabela do módulo", "file": "tabelas/TB_KZ_IDEIA.md"},
                                   {"name": "TR_KZ_LOG", "kind": "trigger", "scope": "local", "reason": "trigger", "file": "triggers/TR_KZ_LOG.sql"}],
                       "jobs": [], "problems": ["msdb: sem permissão"]}, open(os.path.join(banco, "catalogo.json"), "w"))
            json.dump(m.inv, open(os.path.join(moddir, "inventario.json"), "w"))
            plan = {"module": "revamp-kaizen", "docType": "arquitetura", "inventory": os.path.join(moddir, "inventario.json"), "idBase": 0,
                    "kinds": ["TEC", "CMP", "API", "DB", "EVT", "JOB", "INT", "CFG", "SQL", "TRG", "INF"],
                    "areas": re_pacote.plan_areas(m.inv, "arquitetura", 90, 400, 220, [])}
            made = re_pacote.especiais(plan, d, d, moddir, 90)
            self.assertEqual(["banco", "modulo"], [n for n, *_ in made])
            banco_txt = open(os.path.join(d, "pacote-banco.md"), encoding="utf-8").read()
            self.assertIn("TB_KZ_IDEIA", banco_txt)
            self.assertIn("msdb: sem permissão", banco_txt)
            allowed, taken = re_pacote.area_kinds(plan)
            self.assertNotIn("DB", allowed)
            self.assertIn("API", allowed)
            self.assertIn("TEC", taken)
            ranges = [a["idRange"] for a in plan["areas"]] + [x["idRange"] for x in plan["specials"]]
            self.assertEqual(len(ranges), len({r[0] for r in ranges}))
        finally:
            m.close()


class SynthesisTests(unittest.TestCase):
    """0066-ajustes2: visão e spec de arquitetura são escritas a partir dos levantamentos — sem áreas, sem código."""

    def test_synthesis_pack_has_full_items_the_doc_needs_and_titles_of_the_rest(self):
        d = tempfile.mkdtemp()
        func = os.path.join(d, "funcional.md")
        arq = os.path.join(d, "arquitetura.md")
        write(func, "# Levantamento funcional\n\n## Resumo do módulo\nO SOC registra observações de segurança.\n\n## Perfis e permissões\n"
                    "### PRF-001 — Administrador SOC\n- **Onde:** `a.asp:1`\nAbre tudo.\n\n## Regras de negócio\n### RN-001 — Observação exige área\ncorpo da regra\n")
        write(arq, "# Levantamento de arquitetura\n\n## Integrações\n### INT-001 — SOC → Plano de Ação: cria plano\n- **Módulos:** legado-actionplan\n"
                   "- **Mecanismo:** banco compartilhado\n\n## Endpoints\n### API-001 — soc_busca_ajax.asp\ncorpo\n")
        plan = {"module": "legado-soc", "docType": "spec-arquitetura", "areas": [], "idBase": 200, "kinds": ["ADR", "NFR", "SEQ", "GAP"]}
        out = re_pacote.sintese_pack("spec-arquitetura", {"funcional": func, "arquitetura": arq}, d, d, "legado-soc", plan)
        text = open(out, encoding="utf-8").read()
        self.assertIn("**Mecanismo:** banco compartilhado", text)   # INT inteiro (a spec desenha o contexto com ele)
        self.assertIn("**API** (1): API-001 soc_busca_ajax.asp", text)  # API só o título
        self.assertIn("**RN** (1): RN-001 Observação exige área", text)
        self.assertNotIn("corpo da regra", text)
        self.assertEqual([201, 300], plan["specials"][0]["idRange"])
        visao = re_pacote.sintese_pack("visao", {"funcional": func}, d, d, "legado-soc", {"docType": "visao", "areas": [], "idBase": 0})
        vt = open(visao, encoding="utf-8").read()
        self.assertIn("O SOC registra observações de segurança.", vt)  # resumo do módulo
        self.assertIn("Abre tudo.", vt)                                  # PRF inteiro (personas)


class TestCodeTests(unittest.TestCase):
    """0066-ajustes3: código de teste fora do inventário e das áreas (revamp-users: 38% do código era teste)."""

    def test_test_projects_and_files_are_skipped_but_lookalikes_are_not(self):
        d = tempfile.mkdtemp()
        write(os.path.join(d, "src", "App", "UserService.cs"), 'public class UserService { void A() { throw new DomainException("Usuário inativo não entra."); } }\n')
        write(os.path.join(d, "src", "App", "Latest.cs"), "public class Latest {}\n")
        write(os.path.join(d, "tests", "App.Tests", "UserServiceTests.cs"), 'class T { void B() { throw new DomainException("Usuário inativo não entra."); } }\n')
        write(os.path.join(d, "src", "App.UnitTests", "Other.cs"), "class O {}\n")
        write(os.path.join(d, "front", "user.component.spec.ts"), "describe('x', () => {});\n")
        inv = re_tool.inventory([("backend", d)])
        self.assertEqual(["src/App/UserService.cs"], sorted({it["file"] for it in inv["items"] if it["cat"] == "validacao"}))
        files = re_pacote.doc_files(inv, "funcional")
        self.assertEqual({"src/App/UserService.cs", "src/App/Latest.cs"}, set(files))


class DedupeTests(unittest.TestCase):
    def test_same_kind_and_title_from_two_areas_becomes_one_with_references_remapped(self):
        d = tempfile.mkdtemp()
        doc = os.path.join(d, "doc.md")
        write(doc, textwrap.dedent('''\
            ## Endpoints
            ### API-1201 — GET /users/{id}
            curto
            ### API-2305 — GET /users/{id}
            mais completo: retorna o usuário com perfis e times
            ### API-2306 — POST /users
            cria
            ## Regras de negócio
            ### RN-1210 — Usuário inativo não entra
            ver API-1201 e API-2306
            ### RN-2310 — Usuário inativo não entra
            regra igual, mas RN não é fundida (regras parecidas podem ser diferentes)
            '''))
        mapping = re_pacote.dedupe(doc)
        text = open(doc, encoding="utf-8").read()
        self.assertEqual({"API-1201": "API-2305"}, mapping)
        self.assertNotIn("### API-1201", text)
        self.assertIn("ver API-2305 e API-2306", text)
        self.assertEqual(2, text.count("Usuário inativo não entra"))


class JoinTests(unittest.TestCase):
    BASE = textwrap.dedent('''\
        # Levantamento funcional — kaizen

        ## Regras de negócio
        ### RN-001 — Regra antiga
        texto antigo
        ### RN-002 — Continua igual
        - **Onde:** `a.cs:1`

        ## Lacunas e pontos a confirmar
        ''')
    PART = textwrap.dedent('''\
        ## Regras de negócio
        ### RN-001 — Regra antiga (reescrita)
        texto novo, ver RN-301
        ### RN-301 — Regra nova da área
        usa UC-302 e revamp-users#RN-301
        ## Casos de uso
        ### UC-302 — Caso novo
        ''')

    def test_part_replaces_the_base_item_and_ids_are_compacted_with_references(self):
        d = tempfile.mkdtemp()
        write(os.path.join(d, "base.md"), self.BASE)
        write(os.path.join(d, "parte-a.md"), self.PART)
        out = os.path.join(d, "documento.md")
        self.assertEqual(0, re_pacote.juntar(out, os.path.join(d, "base.md"), [os.path.join(d, "parte-a.md")]))
        text = open(out, encoding="utf-8").read()
        self.assertNotIn("texto antigo", text)
        self.assertIn("RN-002 — Continua igual", text)
        mapping = re_pacote.compactar(out, 300, ["RN-001", "RN-002", "UC-001"])
        text = open(out, encoding="utf-8").read()
        self.assertEqual({"RN-301": "RN-003", "UC-302": "UC-002"}, mapping)
        self.assertIn("### RN-003 — Regra nova da área", text)
        self.assertIn("texto novo, ver RN-003", text)
        self.assertIn("usa UC-002 e revamp-users#RN-301", text)  # referência de outro módulo não muda
        # juntar de novo a partir da mesma base dá o mesmo resultado (idempotente)
        re_pacote.juntar(out, os.path.join(d, "base.md"), [os.path.join(d, "parte-a.md")])
        re_pacote.compactar(out, 300, ["RN-001", "RN-002", "UC-001"])
        self.assertEqual(text, open(out, encoding="utf-8").read())


class EvidenceTests(unittest.TestCase):
    def test_flags_missing_file_line_past_end_and_literal_not_in_cited_file(self):
        m = Module()
        try:
            doc = textwrap.dedent('''\
                ## Regras de negócio
                ### RN-001 — Aprovador obrigatório
                - **Onde:** `Controllers/IdeiaController.cs:9`
                Mensagem: "Informe o aprovador da etapa."
                ### RN-002 — Linha que não existe
                - **Onde:** `Controllers/IdeiaController.cs:999`
                ### RN-003 — Arquivo inventado
                - **Onde:** `Services/NaoExiste.cs:10`
                ### RN-004 — Literal inventado
                - **Onde:** `Controllers/IdeiaController.cs:9`
                Mensagem: "Esse texto nunca existiu no codigo"
                ''')
            path = os.path.join(m.tmp.name, "doc.md")
            write(path, doc)
            res = re_pacote.evidencia(path, m.inv)
            kinds = {(p["item"], p["kind"]) for p in res["problems"]}
            self.assertEqual({("RN-002", "linha"), ("RN-003", "arquivo"), ("RN-004", "literal")}, kinds)
        finally:
            m.close()


FAKE_SQL = r'''#!/usr/bin/env bash
# sql-query.sh falso: responde pelas palavras da consulta (o -q é o último argumento)
Q="${@: -1}"; DB=""; while [[ $# -gt 0 ]]; do [[ "$1" == -d ]] && DB="$2"; shift; done
python3 - "$DB" "$Q" <<'PY'
import json, sys
db, q = sys.argv[1], sys.argv[2]
def out(rows): print(json.dumps({"rows": rows, "truncated": False}))
if db == "msdb":
    print("The SELECT permission was denied on the object 'sysjobsteps'", file=sys.stderr); sys.exit(1)
if "FROM sys.objects" in q:
    out([{"id": 1, "name": "TB_KZ_IDEIA", "type": "U ", "sch": "dbo", "parent": 0, "created": "2026-01-01 00:00:00", "modified": "2026-02-01 00:00:00"},
         {"id": 2, "name": "TR_KZ_IDEIA_LOG", "type": "TR", "sch": "dbo", "parent": 1, "created": "2026-01-01 00:00:00", "modified": "2026-02-01 00:00:00"},
         {"id": 3, "name": "TB_OUTRO", "type": "U ", "sch": "dbo", "parent": 0, "created": "2026-01-01 00:00:00", "modified": "2026-02-01 00:00:00"}])
elif "sql_expression_dependencies" in q: out([{"src": 2, "dst": 1, "name": "TB_KZ_IDEIA"}])
elif "sys.sql_modules" in q: out([{"id": 2, "definition": "CREATE TRIGGER TR_KZ_IDEIA_LOG ON TB_KZ_IDEIA AFTER UPDATE AS INSERT ..."}])
elif "trigger_events" in q: out([{"id": 2, "ev": "UPDATE"}])
elif "sys.indexes" in q or "foreign_keys" in q or "check_constraints" in q: out([])
elif "sys.columns c" in q: out([{"tid": 1, "pos": 1, "name": "ID", "tipo": "int", "len": 4, "prec": 10, "scale": 0, "nulo": False, "ident": True, "calc": False, "padrao": None, "formula": None}])
elif "TB_SYS_Application" in q: out([{"id": "AAA", "name": "Kaizen", "alias": "KZ", "description": ""}])
elif "TB_WCM_MENU" in q: out([{"app": "aaa", "grp": "Cadastros", "item": "Ideias", "path": "/kaizen", "pathNew": None}])
else: out([])
PY
'''


class SnapshotTests(unittest.TestCase):
    def test_bank_snapshot_once_then_module_catalog_from_it_with_the_collection_gaps(self):
        d = tempfile.mkdtemp()
        sql = os.path.join(d, "sql-query.sh")
        write(sql, FAKE_SQL)
        os.chmod(sql, os.stat(sql).st_mode | stat.S_IEXEC)
        snap = os.path.join(d, "retrato")
        re_banco.retrato({"sql": sql, "host": "prod", "global": "DB_G", "local": ["DB_L1"], "out": snap, "environment": "DEMO", "prefix": []})
        meta = json.load(open(os.path.join(snap, "meta.json"), encoding="utf-8"))
        self.assertEqual({"DB_G", "DB_L1"}, set(meta["counts"]))
        self.assertTrue(any("jobs do SQL Agent não lidos" in p for p in meta["problems"]))
        with gzip.open(os.path.join(snap, "retrato.json.gz"), "rt", encoding="utf-8") as fh:
            self.assertIn("2", json.load(fh)["dbs"]["DB_G"]["defs"])

        out = os.path.join(d, "banco")
        os.makedirs(out)
        # sem --sql: o catálogo do módulo sai do retrato, sem tocar no banco
        re_banco.catalogo({"retrato": snap, "host": "prod", "global": "DB_G", "local": ["DB_L1"], "out": out, "environment": "DEMO",
                           "prefix": ["TB_KZ_"], "sigla": "KZ"})
        cat = json.load(open(os.path.join(out, "catalogo.json"), encoding="utf-8"))
        self.assertEqual("retrato", cat["source"]["kind"])
        names = {o["name"] for o in cat["objects"]}
        self.assertEqual({"TB_KZ_IDEIA", "TR_KZ_IDEIA_LOG"}, names)
        self.assertTrue(any(p.startswith("retrato: jobs do SQL Agent") for p in cat["problems"]))
        self.assertEqual("Kaizen", cat["application"]["name"])
        self.assertTrue(os.path.exists(os.path.join(out, "triggers", "TR_KZ_IDEIA_LOG.sql")))

    def test_aws_snapshot_is_used_only_when_fresh_and_covering_the_regions(self):
        d = tempfile.mkdtemp()
        re_infra.save_snapshot(d, "123", "perfil", ["us-east-1"], [{"svc": "sqs", "name": "KZ_QUEUE", "arn": "arn:x"}],
                               {"denied": [("us-east-1 ses list", "AccessDenied")], "failed": []})
        snap = re_infra.load_snapshot(d, "123", ["us-east-1"], 7)
        self.assertEqual("KZ_QUEUE", snap["resources"][0]["name"])
        self.assertIsNone(re_infra.load_snapshot(d, "123", ["us-east-1", "sa-east-1"], 7))
        self.assertIsNone(re_infra.load_snapshot(d, "123", ["us-east-1"], 0))
        self.assertIsNone(re_infra.load_snapshot(d, "999", ["us-east-1"], 7))


if __name__ == "__main__":
    unittest.main()
