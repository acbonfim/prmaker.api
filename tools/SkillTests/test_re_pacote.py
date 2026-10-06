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
        write(os.path.join(self.root, "Views", "kz_lst_ideia.cshtml"), "<div>lista</div>\n<script>\nfunction listar(){ ewcmAlert('@LanguageHelper.GetLanguageByTerm(x, \"Nenhuma ideia encontrada na busca\")'); }\n</script>\n")
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

    def test_cartao_joins_instructions_template_and_module_keys(self):
        d = tempfile.mkdtemp()
        write(os.path.join(d, "sub.md"), "# Cartao")
        write(os.path.join(d, "modelo.md"), "## Regras de negócio")
        write(os.path.join(d, "modulos.tsv"), "revamp-users\tUsuários\tusuarios novo\n")
        out = re_pacote.cartao(os.path.join(d, "sub.md"), os.path.join(d, "modelo.md"), os.path.join(d, "modulos.tsv"), os.path.join(d, "c.md"))
        text = open(out, encoding="utf-8").read()
        self.assertIn("## Regras de negócio", text)
        self.assertIn("`revamp-users` Usuários · usuarios novo", text)


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
