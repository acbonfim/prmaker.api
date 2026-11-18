"""0097/S1: a engenharia reversa cita o caminho completo a partir da raiz do repositório e registra de que commit da
origin/master veio; o check acusa citação só com o nome do arquivo, citação ambígua e arquivo local do gerador.

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import importlib.util
import os
import subprocess
import sys
import tempfile
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

ENV = {**os.environ, "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@t", "GIT_COMMITTER_NAME": "t", "GIT_COMMITTER_EMAIL": "t@t"}


def git(cwd, *args):
    return subprocess.run(["git", "-C", cwd, *args], check=True, capture_output=True, text=True, env=ENV).stdout.strip()


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(text)


class FullPathTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.repo = os.path.join(self.tmp.name, "edv-solvace")
        os.makedirs(self.repo)
        git(self.repo, "init", "-q", "-b", "master")
        git(self.repo, "remote", "add", "origin", "https://github.com/x/edv-solvace.git")
        self.mod = os.path.join(self.repo, "solvace-asp", "systems", "sa3")
        write(os.path.join(self.mod, "sa3_registro.asp"), "<%\n' linha 2\nSQL = \"SELECT * FROM TB_SA3_A3\"\n%>\n")
        write(os.path.join(self.mod, "Views", "lista.cshtml"), "<div>a</div>\n")
        write(os.path.join(self.mod, "Outra", "lista.cshtml"), "<div>b</div>\n")
        git(self.repo, "add", "-A")
        git(self.repo, "commit", "-qm", "v1")
        self.inv = re_tool.inventory([("backend", self.mod)])

    def tearDown(self):
        self.tmp.cleanup()

    def test_inventory_records_repo_prefix_commit_and_the_source_of_each_item(self):
        src = self.inv["sources"][0]
        self.assertEqual(src["repo"], "edv-solvace")
        self.assertEqual(src["prefix"], "solvace-asp/systems/sa3")
        self.assertEqual(len(src["commit"]), 40)
        self.assertFalse(src["master"])
        self.assertTrue(all(it["src"] == 0 for it in self.inv["items"]))
        tab = next(it for it in self.inv["items"] if it["name"] == "TB_SA3_A3")
        self.assertEqual(re_tool.full_path(self.inv, tab["file"], tab["src"]), "solvace-asp/systems/sa3/sa3_registro.asp")

    def test_old_inventory_without_prefix_still_resolves_the_full_path(self):
        old = {"sources": [{"role": "backend", "path": self.mod}], "items": []}
        self.assertEqual(re_tool.full_path(old, "Views/lista.cshtml"), "solvace-asp/systems/sa3/Views/lista.cshtml")

    def test_check_flags_bare_name_ambiguous_and_generator_local_files(self):
        doc = os.path.join(self.tmp.name, "doc.md")
        write(doc, "# Doc\n\n## Regras de negócio\n\n"
                   "### RN-001 — Bom\n- **Onde:** `solvace-asp/systems/sa3/sa3_registro.asp:3`\n\n"
                   "### RN-002 — Com o nome do repo também vale\n- **Onde:** `edv-solvace/solvace-asp/systems/sa3/sa3_registro.asp:3`\n\n"
                   "### RN-003 — Só o nome\n- **Onde:** `sa3_registro.asp:3`\n\n"
                   "### RN-004 — Ambígua\n- **Onde:** `lista.cshtml:1`\n\n"
                   "### RN-005 — Local do gerador\n- **Onde:** `banco/catalogo.json:1` e `~/.prmake/reverse/x/inventario.json`\n")
        res = re_pacote.evidencia(doc, self.inv)
        kinds = {(p["item"], p["kind"]) for p in res["problems"]}
        self.assertNotIn("RN-001", {i for i, _ in kinds})
        self.assertNotIn("RN-002", {i for i, _ in kinds})
        self.assertIn(("RN-003", "sem-caminho"), kinds)
        self.assertIn("solvace-asp/systems/sa3/sa3_registro.asp:3",
                      next(p["detail"] for p in res["problems"] if p["item"] == "RN-003"))
        self.assertIn(("RN-004", "ambigua"), kinds)
        self.assertIn(("RN-005", "arquivo-local"), kinds)
        self.assertNotIn(("RN-005", "arquivo"), kinds)

    def test_pack_shows_full_path_and_the_master_commit(self):
        plan = {"module": "legado-rca", "docType": "funcional", "kinds": ["RN"], "checkpointEvery": 10,
                "areas": [{"name": "a", "idRange": [1, 100], "files": ["sa3_registro.asp"],
                           "units": [{"file": "sa3_registro.asp", "ranges": [[1, 4]]}]}]}
        out, _, _ = re_pacote.build_pack(self.inv, plan, plan["areas"][0], self.tmp.name, None, self.tmp.name)
        text = open(out, encoding="utf-8").read()
        self.assertIn("### `solvace-asp/systems/sa3/sa3_registro.asp` — linhas 1-4", text)
        self.assertIn("**Código:** edv-solvace@", text)

    def test_code_header_goes_under_the_title_and_is_replaced_not_duplicated(self):
        fontes = os.path.join(self.tmp.name, "fontes.tsv")
        write(fontes, "backend\t/x/.prmake-wt/master/edv-solvace/solvace-asp/systems/sa3\t247220610f@origin/master\tedv-solvace\n")
        doc = os.path.join(self.tmp.name, "d.md")
        write(doc, "# Levantamento\n\n## Resumo\ntexto\n")
        re_pacote.cabecalho(doc, fontes)
        write(fontes, "backend\t/x\t1111111111@origin/master\tedv-solvace\n")
        re_pacote.cabecalho(doc, fontes)
        text = open(doc, encoding="utf-8").read()
        self.assertEqual(text.count("<!-- codigo:"), 1)
        self.assertTrue(text.startswith("# Levantamento\n<!-- codigo: edv-solvace@1111111111 (origin/master) -->\n"))


if __name__ == "__main__":
    unittest.main()
