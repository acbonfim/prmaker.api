"""0097/S2: reancoragem (sem IA) dos documentos publicados na origin/master — linhas mapeadas pelo git diff, caminho
completo quando único, itens cujo trecho citado mudou e só as linhas `Onde:` reescritas.

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import importlib.util
import os
import subprocess
import tempfile
import textwrap
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = os.path.join(HERE, "..", "..", "skills", "engenharia-reversa", "scripts")


def load(name):
    spec = importlib.util.spec_from_file_location(name, os.path.join(SCRIPTS, f"{name}.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


re_ancora = load("re_ancora")
ENV = {**os.environ, "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@t", "GIT_COMMITTER_NAME": "t", "GIT_COMMITTER_EMAIL": "t@t"}


def git(cwd, *args):
    return subprocess.run(["git", "-C", cwd, *args], check=True, capture_output=True, text=True, env=ENV).stdout.strip()


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(text)


class MapRangeTests(unittest.TestCase):
    def test_lines_shift_after_insertions_and_deletions(self):
        hunks = [(2, 0, 3, 2), (10, 2, 12, 3)]   # 2 linhas inseridas depois da 2; 10-11 viram 12-14
        self.assertEqual(re_ancora.map_range(hunks, 1, 1), (1, 1, False))
        self.assertEqual(re_ancora.map_range(hunks, 5, 6), (7, 8, False))
        self.assertEqual(re_ancora.map_range(hunks, 20, 20), (23, 23, False))
        self.assertTrue(re_ancora.map_range(hunks, 9, 10)[2])          # trecho toca a linha alterada
        self.assertTrue(re_ancora.map_range(hunks, 1, 4)[2])           # inserção no meio do trecho
        self.assertEqual(re_ancora.map_range([(5, 3, 4, 0)], 9, 9), (6, 6, False))   # 5-7 apagadas


class AncorarTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        r = self.repo = os.path.join(self.tmp.name, "edv-solvace")
        os.makedirs(r)
        git(r, "init", "-q", "-b", "master")
        write(os.path.join(r, "solvace-asp/systems/sa3/sa3_registro.asp"), "".join(f"linha {i}\n" for i in range(1, 21)))
        write(os.path.join(r, "solvace-asp/systems/sa3/sa3_ajax.asp"), "".join(f"ajax {i}\n" for i in range(1, 11)))
        write(os.path.join(r, "solvace-asp/systems/rca/sa3_ajax.asp"), "outro modulo\n")
        write(os.path.join(r, "solvace-core/helpers/Old.cs"), "a\nb\nc\n")
        git(r, "add", "-A"); git(r, "commit", "-qm", "v1")
        self.old = git(r, "rev-parse", "HEAD")
        reg = [f"linha {i}\n" for i in range(1, 21)]
        reg[0:0] = ["novo 1\n", "novo 2\n"]           # +2 no topo
        reg[13] = "linha 12 ALTERADA\n"               # a antiga linha 12 mudou
        write(os.path.join(r, "solvace-asp/systems/sa3/sa3_registro.asp"), "".join(reg))
        os.makedirs(os.path.join(r, "solvace-core/helpers/Services"))
        git(r, "mv", "solvace-core/helpers/Old.cs", "solvace-core/helpers/Services/Old.cs")
        git(r, "rm", "-q", "solvace-asp/systems/sa3/sa3_ajax.asp")
        git(r, "add", "-A"); git(r, "commit", "-qm", "v2")
        git(r, "update-ref", "refs/remotes/origin/master", "HEAD")

    def tearDown(self):
        self.tmp.cleanup()

    def run_doc(self, doc, old=None):
        repo = re_ancora.Repo("edv-solvace", self.repo, old if old is not None else self.old, prefixes=["solvace-asp/systems/sa3"])
        return re_ancora.ancorar(textwrap.dedent(doc), [repo])

    def test_lines_are_remapped_paths_completed_and_changed_snippets_listed(self):
        out, rep = self.run_doc('''\
            # Funcional
            <!-- codigo: edv-solvace@0000000 (hotfix/1) -->
            ## Regras
            ### RN-001 — Igual, só desloca
            - **Onde:** `sa3_registro.asp:3-4` (início) e `:6`
            Texto cita `sa3_registro.asp:3` no corpo e não muda.
            ### RN-002 — Trecho alterado
            - **Onde:** `solvace-asp/systems/sa3/sa3_registro.asp:11-12`
            ### RN-003 — Arquivo apagado (e o nome é ambíguo fora das pastas do módulo)
            - **Onde:** `sa3_ajax.asp:2`
            ### RN-004 — Renomeado sem mudança
            - **Onde:** `helpers/Old.cs:2`
            ''')
        self.assertIn("- **Onde:** `solvace-asp/systems/sa3/sa3_registro.asp:5-6` (início) e `:8`", out)
        self.assertIn("Texto cita `sa3_registro.asp:3` no corpo", out)          # fora do Onde: não reescreve
        self.assertIn("`solvace-core/helpers/Services/Old.cs:2`", out)
        self.assertIn("<!-- codigo: edv-solvace@", out)
        self.assertNotIn("0000000", out)
        stale = {s["itemId"]: s["reasons"][0] for s in rep["stale"]}
        self.assertEqual(set(stale), {"RN-002", "RN-003"})
        self.assertIn("trecho citado mudou", stale["RN-002"])
        self.assertIn("removido", stale["RN-003"])
        self.assertEqual(rep["stats"]["renomeado"], 1)
        self.assertGreaterEqual(rep["stats"]["caminho-completado"], 3)

    def test_without_the_old_commit_only_paths_are_completed(self):
        out, rep = self.run_doc('''\
            ### RN-001 — x
            - **Onde:** `sa3_registro.asp:3`
            ''', old="")
        self.assertIn("`solvace-asp/systems/sa3/sa3_registro.asp:3`", out)
        self.assertEqual(rep["stats"]["sem-commit-antigo"], 1)
        self.assertEqual(rep["stale"], [])

    def test_ambiguous_and_generator_local_citations_are_left_for_review(self):
        repo = re_ancora.Repo("edv-solvace", self.repo, self.old, prefixes=[])
        out, rep = re_ancora.ancorar("### RN-001 — x\n- **Onde:** `sa3_ajax.asp:1`, `banco/catalogo.json:1`\n", [repo])
        kinds = {u["situacao"] for u in rep["unresolved"]}
        self.assertEqual(kinds, {"ambigua", "arquivo-local"})
        self.assertIn("`sa3_ajax.asp:1`", out)

    def test_sources_come_from_the_published_session_then_the_header(self):
        pub = [{"path": "/Users/x/repos/solvace/edv-solvace/solvace-asp/systems/sa3", "commit": "e17613279d@hotfix/73821"},
               {"path": "/Users/x/.prmake-wt/master/edv-solvace-apps/projects/users", "commit": "8c25@origin/master"}]
        mod = [{"repository": "edv-solvace", "path": "solvace-asp/systems/sa3"}, {"repository": "edv-solvace-apps", "path": "projects/users"}]
        got = re_ancora.fontes(pub, mod, "# x\n<!-- codigo: revamp-Users@cf32da9 (origin/master) -->\n")
        self.assertEqual(got[0], ("edv-solvace", "e17613279d", ["solvace-asp/systems/sa3"]))
        self.assertEqual(got[1], ("edv-solvace-apps", "8c25", ["projects/users"]))
        self.assertEqual(got[2], ("revamp-Users", "cf32da9", []))


if __name__ == "__main__":
    unittest.main()
